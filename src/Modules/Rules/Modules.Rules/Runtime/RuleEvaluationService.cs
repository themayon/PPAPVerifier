using System.Net;
using System.Text;
using System.Text.Json;
using DES.Framework.Core.Exceptions;
using DES.Framework.Shared.Multitenancy;
using DES.Modules.Rules.Contracts;
using DES.Modules.Rules.Domain;
using DES.Modules.Rules.Persistence;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace DES.Modules.Rules.Runtime;

internal sealed class RuleEvaluationService(
    RulesDbContext db,
    IMultiTenantContextAccessor<AppTenantInfo> tenantAccessor,
    TimeProvider timeProvider) : IRuleEvaluationService
{
    public async Task<Guid> EvaluateAsync(RuleEvaluationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        string tenantId = tenantAccessor.MultiTenantContext.TenantInfo?.Id
            ?? throw new UnauthorizedException("Tenant context is required.");

        Guid? existingRunId = await db.RuleEvaluationRuns
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId
                && x.SubjectId == request.SubjectId
                && x.TemplateVersionId == request.TemplateVersionId)
            .Select(x => (Guid?)x.Id)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (existingRunId.HasValue)
        {
            return existingRunId.Value;
        }

        RuleTemplate template = await db.RuleTemplates
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .Include(x => x.Versions).ThenInclude(x => x.RuleAssignments)
            .AsSplitQuery()
            .SingleOrDefaultAsync(x => x.Versions.Any(v => v.Id == request.TemplateVersionId), cancellationToken)
            .ConfigureAwait(false)
            ?? throw new CustomException("Template version was not found.", (IEnumerable<string>?)null, HttpStatusCode.NotFound);

        RuleTemplateVersion templateVersion = template.Versions.Single(x => x.Id == request.TemplateVersionId);
        if (templateVersion.Status is not (RuleLifecycle.Published or RuleLifecycle.Retired))
        {
            throw new CustomException("Only a published or retired template version can be evaluated.", (IEnumerable<string>?)null, HttpStatusCode.Conflict);
        }

        Guid[] assignedVersionIds = templateVersion.RuleAssignments.Select(x => x.RuleVersionId).Distinct().ToArray();
        List<RuleDefinition> definitions = await db.RuleDefinitions
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.Versions.Any(v => assignedVersionIds.Contains(v.Id)))
            .Include(x => x.Versions.Where(v => assignedVersionIds.Contains(v.Id)))
            .AsSplitQuery()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        Dictionary<Guid, (Guid DefinitionId, RuleVersion Version)> versions = definitions
            .SelectMany(x => x.Versions.Select(v => (DefinitionId: x.Id, Version: v)))
            .ToDictionary(x => x.Version.Id);
        if (versions.Count != assignedVersionIds.Length
            || versions.Values.Any(x => x.Version.Status is not (RuleLifecycle.Published or RuleLifecycle.Retired)))
        {
            throw new CustomException("Every assigned rule version must exist in the same tenant and be published or retired.", (IEnumerable<string>?)null, HttpStatusCode.Conflict);
        }

        Dictionary<string, JsonElement> documents = CloneDocuments(request.Documents);
        var context = new RuleEvaluationContext(documents);
        DateTime utcNow = timeProvider.GetUtcNow().UtcDateTime;
        RuleEvaluationRun run = RuleEvaluationRun.Start(
            tenantId,
            templateVersion.Id,
            request.SubjectId,
            request.OwnerUserId,
            request.InputSchemaVersion,
            utcNow);

        foreach (TemplateRuleAssignment assignment in templateVersion.RuleAssignments
                     .OrderBy(x => x.DocumentCode, StringComparer.Ordinal)
                     .ThenBy(x => x.RuleVersionId))
        {
            cancellationToken.ThrowIfCancellationRequested();
            (Guid definitionId, RuleVersion version) = versions[assignment.RuleVersionId];
            RuleEvaluationResult result = TemplateEvaluationOrchestrator.Evaluate(version.AstJson, assignment.DocumentCode, context, assignment.ParametersJson);
            string traceJson = TemplateEvaluationOrchestrator.SerializeTrace(result.Trace);
            run.AppendResult(
                definitionId,
                version.Id,
                assignment.DocumentCode,
                result.Status,
                result.Reason,
                traceJson,
                timeProvider.GetUtcNow().UtcDateTime);
        }

        run.Complete(timeProvider.GetUtcNow().UtcDateTime);
        db.RuleEvaluationRuns.Add(run);
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return run.Id;
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            Guid? concurrentRunId = await db.RuleEvaluationRuns
                .AsNoTracking()
                .Where(x => x.TenantId == tenantId
                    && x.SubjectId == request.SubjectId
                    && x.TemplateVersionId == request.TemplateVersionId)
                .Select(x => (Guid?)x.Id)
                .SingleOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            if (concurrentRunId.HasValue)
            {
                return concurrentRunId.Value;
            }

            throw;
        }
    }

    private static Dictionary<string, JsonElement> CloneDocuments(IReadOnlyDictionary<string, string> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var documents = new Dictionary<string, JsonElement>(source.Count, StringComparer.Ordinal);
        foreach ((string code, string json) in source.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            using JsonDocument document = JsonDocument.Parse(json);
            documents.Add(code.Trim().ToUpperInvariant(), document.RootElement.Clone());
        }

        return documents;
    }
}

internal static class TemplateEvaluationOrchestrator
{
    internal static RuleEvaluationResult Evaluate(string astJson, string? assignedDocumentCode, RuleEvaluationContext context, string? parametersJson = null)
    {
        IReadOnlyDictionary<string, JsonElement> parameters;
        try
        {
            parameters = RuleParameterContext.Parse(parametersJson);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            return new RuleEvaluationResult(RuleEvaluationStatus.Error, "Stored assignment parameters are invalid.", []);
        }

        if (assignedDocumentCode is not null && !context.Documents.ContainsKey(assignedDocumentCode))
        {
            return new RuleEvaluationResult(RuleEvaluationStatus.NotEvaluated, "Assigned document was not supplied.", []);
        }

        try
        {
            return new RuleEvaluator().Evaluate(RuleAstSerializer.Parse(astJson), context with { Parameters = parameters });
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            return new RuleEvaluationResult(RuleEvaluationStatus.Error, "Stored rule AST is invalid.", []);
        }
    }

    internal static string SerializeTrace(IReadOnlyList<RuleTrace> trace)
    {
        string json = JsonSerializer.Serialize(trace);
        return Encoding.UTF8.GetByteCount(json) <= RuleEvaluationResultRecord.MaxTraceBytes ? json : "[]";
    }
}
