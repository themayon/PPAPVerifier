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

        (RuleTemplateVersion templateVersion, HashSet<Guid> archivedAssignments, Dictionary<Guid, LoadedRule> versions) =
            await LoadTemplateAsync(tenantId, request.TemplateVersionId, cancellationToken).ConfigureAwait(false);

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
                     .Where(x => !archivedAssignments.Contains(x.RuleVersionId))
                     .OrderBy(x => x.DocumentCode, StringComparer.Ordinal)
                     .ThenBy(x => x.RuleVersionId))
        {
            cancellationToken.ThrowIfCancellationRequested();
            LoadedRule loadedRule = versions[assignment.RuleVersionId];
            Guid definitionId = loadedRule.DefinitionId;
            RuleVersion version = loadedRule.Version;
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

    public async Task<IReadOnlyList<RuleEvaluationPreviewResult>> PreviewAsync(
        RuleEvaluationRequest request,
        string documentCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentCode);
        string tenantId = tenantAccessor.MultiTenantContext.TenantInfo?.Id
            ?? throw new UnauthorizedException("Tenant context is required.");
        string targetCode = TemplateDocumentRequirement.NormalizeDocumentCode(documentCode);

        (RuleTemplateVersion templateVersion, HashSet<Guid> archivedAssignments, Dictionary<Guid, LoadedRule> versions) =
            await LoadTemplateAsync(tenantId, request.TemplateVersionId, cancellationToken).ConfigureAwait(false);

        Dictionary<string, JsonElement> documents = CloneDocuments(request.Documents);
        var context = new RuleEvaluationContext(documents);
        var results = new List<RuleEvaluationPreviewResult>();
        foreach (TemplateRuleAssignment assignment in templateVersion.RuleAssignments
                     .Where(x => !archivedAssignments.Contains(x.RuleVersionId))
                     .OrderBy(x => x.DocumentCode, StringComparer.Ordinal)
                     .ThenBy(x => x.RuleVersionId))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsComplexExecutorAssignment(assignment.ParametersJson)) continue;

            LoadedRule rule = versions[assignment.RuleVersionId];
            var referencedCodes = new HashSet<string>(StringComparer.Ordinal);
            if (assignment.DocumentCode is not null) referencedCodes.Add(assignment.DocumentCode);
            try
            {
                ComplexValidationPlanReader.CollectAstDocumentCodes(rule.Version.AstJson, referencedCodes);
            }
            catch (JsonException)
            {
                referencedCodes.Clear();
                if (assignment.DocumentCode is not null) referencedCodes.Add(assignment.DocumentCode);
            }

            if (!referencedCodes.Contains(targetCode) || !referencedCodes.All(documents.ContainsKey)) continue;

            RuleEvaluationResult result = TemplateEvaluationOrchestrator.Evaluate(rule.Version.AstJson, assignment.DocumentCode, context, assignment.ParametersJson);
            results.Add(new RuleEvaluationPreviewResult(
                rule.DefinitionId,
                rule.Version.Id,
                rule.StableCode,
                rule.Name,
                assignment.DocumentCode,
                result.Status,
                result.Reason,
                OutcomeMessageParser.Parse(rule.Version.OutcomeJson, result.Status),
                TemplateEvaluationOrchestrator.SerializeTrace(result.Trace)));
        }

        return results;
    }

    private async Task<(RuleTemplateVersion TemplateVersion, HashSet<Guid> ArchivedAssignments, Dictionary<Guid, LoadedRule> Versions)> LoadTemplateAsync(
        string tenantId,
        Guid templateVersionId,
        CancellationToken cancellationToken)
    {
        RuleTemplate template = await db.RuleTemplates
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .Include(x => x.Versions).ThenInclude(x => x.RuleAssignments)
            .AsSplitQuery()
            .SingleOrDefaultAsync(x => x.Versions.Any(v => v.Id == templateVersionId), cancellationToken)
            .ConfigureAwait(false)
            ?? throw new CustomException("Template version was not found.", (IEnumerable<string>?)null, HttpStatusCode.NotFound);

        RuleTemplateVersion templateVersion = template.Versions.Single(x => x.Id == templateVersionId);
        if (templateVersion.Status is not (RuleLifecycle.Published or RuleLifecycle.Retired))
        {
            throw new CustomException("Only a published or retired template version can be evaluated.", (IEnumerable<string>?)null, HttpStatusCode.Conflict);
        }

        Guid[] assignedVersionIds = templateVersion.RuleAssignments.Select(x => x.RuleVersionId).Distinct().ToArray();
        var definitions = await db.RuleDefinitions
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.Versions.Any(v => assignedVersionIds.Contains(v.Id)))
            .Select(x => new { x.Id, x.StableCode, x.Name, x.CurrentPublishedVersionId, IsArchived = x.ArchivedAtUtc != null, AssignedVersionIds = x.Versions.Where(v => assignedVersionIds.Contains(v.Id)).Select(v => v.Id).ToList() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        HashSet<Guid> archivedAssignments = definitions.Where(x => x.IsArchived).SelectMany(x => x.AssignedVersionIds).ToHashSet();
        definitions = definitions.Where(x => !x.IsArchived).ToList();
        Guid[] loadIds = definitions.SelectMany(x => x.AssignedVersionIds).Concat(definitions.Where(x => x.CurrentPublishedVersionId.HasValue).Select(x => x.CurrentPublishedVersionId!.Value)).Distinct().ToArray();
        Dictionary<Guid, RuleVersion> loaded = await db.RuleVersions.AsNoTracking().Where(x => loadIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken).ConfigureAwait(false);
        Dictionary<Guid, LoadedRule> versions = definitions
            .SelectMany(d => d.AssignedVersionIds.Select(assigned => (Assigned: assigned, Definition: d, Effective: d.CurrentPublishedVersionId ?? assigned)))
            .ToDictionary(x => x.Assigned, x => new LoadedRule(x.Definition.Id, x.Definition.StableCode, x.Definition.Name, loaded[x.Effective]));
        if (versions.Count + archivedAssignments.Count != assignedVersionIds.Length
            || versions.Values.Any(x => x.Version.Status is not (RuleLifecycle.Published or RuleLifecycle.Retired)))
        {
            throw new CustomException("Every assigned rule version must exist in the same tenant and be published or retired.", (IEnumerable<string>?)null, HttpStatusCode.Conflict);
        }

        return (templateVersion, archivedAssignments, versions);
    }

    private static bool IsComplexExecutorAssignment(string parametersJson)
    {
        if (string.IsNullOrWhiteSpace(parametersJson)) return false;
        try
        {
            using JsonDocument parameters = JsonDocument.Parse(parametersJson);
            return parameters.RootElement.ValueKind == JsonValueKind.Object
                && ComplexValidationPlanReader.TryReadString(parameters.RootElement, "executorKey", out _);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private sealed record LoadedRule(Guid DefinitionId, string StableCode, string Name, RuleVersion Version);

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
