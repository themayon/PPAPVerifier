using System.Text.Json;
using DES.Framework.Core.Exceptions;
using DES.Framework.Shared.Multitenancy;
using DES.Modules.Rules.Contracts;
using DES.Modules.Rules.Domain;
using DES.Modules.Rules.Persistence;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace DES.Modules.Rules.Runtime;

internal sealed class RuleEvaluationResultReader(
    RulesDbContext db,
    IMultiTenantContextAccessor<AppTenantInfo> tenantAccessor) : IRuleEvaluationResultReader
{
    public async Task<RuleEvaluationRunDetails> ReadAsync(
        Guid evaluationRunId,
        Guid ownerUserId,
        Guid? subjectId = null,
        CancellationToken cancellationToken = default)
    {
        if (evaluationRunId == Guid.Empty) throw new ArgumentException("Evaluation run ID is required.", nameof(evaluationRunId));
        if (ownerUserId == Guid.Empty) throw new ArgumentException("Owner user ID is required.", nameof(ownerUserId));
        if (subjectId == Guid.Empty) throw new ArgumentException("Subject ID must not be empty.", nameof(subjectId));

        string tenantId = tenantAccessor.MultiTenantContext.TenantInfo?.Id
            ?? throw new UnauthorizedException("Tenant context is required.");
        RuleEvaluationRun run = await db.RuleEvaluationRuns
            .AsNoTracking()
            .Include(x => x.Results)
            .Where(x => x.TenantId == tenantId
                && x.Id == evaluationRunId
                && x.OwnerUserId == ownerUserId
                && (!subjectId.HasValue || x.SubjectId == subjectId.Value))
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException($"Rule evaluation {evaluationRunId} was not found.");

        Guid[] definitionIds = run.Results.Select(x => x.RuleDefinitionId).Distinct().ToArray();
        Guid[] versionIds = run.Results.Select(x => x.RuleVersionId).Distinct().ToArray();
        List<RuleDefinition> definitions = await db.RuleDefinitions
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && definitionIds.Contains(x.Id))
            .Include(x => x.Versions.Where(v => versionIds.Contains(v.Id)))
            .AsSplitQuery()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        Dictionary<Guid, RuleDefinition> definitionsById = definitions.ToDictionary(x => x.Id);
        Dictionary<Guid, RuleVersion> versionsById = definitions
            .SelectMany(x => x.Versions)
            .ToDictionary(x => x.Id);
        if (definitionsById.Count != definitionIds.Length
            || run.Results.Any(x => !versionsById.ContainsKey(x.RuleVersionId)))
        {
            throw new InvalidOperationException("The evaluation result snapshot references unavailable rule metadata.");
        }

        RuleEvaluationResultDetails[] results = run.Results
            .OrderBy(x => x.EvaluatedAtUtc)
            .ThenBy(x => x.Id)
            .Select(result =>
            {
                RuleDefinition definition = definitionsById[result.RuleDefinitionId];
                RuleVersion version = versionsById[result.RuleVersionId];
                return new RuleEvaluationResultDetails(
                    result.Id,
                    result.RuleDefinitionId,
                    result.RuleVersionId,
                    run.TemplateVersionId,
                    definition.StableCode,
                    definition.Name,
                    result.DocumentCode,
                    result.Status,
                    result.Reason,
                    OutcomeMessageParser.Parse(version.OutcomeJson, result.Status),
                    result.TraceJson,
                    result.EvaluatedAtUtc);
            })
            .ToArray();

        return new RuleEvaluationRunDetails(
            run.Id,
            run.TemplateVersionId,
            run.SubjectId,
            run.OwnerUserId,
            run.InputSchemaVersion,
            run.Status,
            run.CreatedAtUtc,
            run.CompletedAtUtc,
            run.Version,
            results);
    }
}

internal static class OutcomeMessageParser
{
    internal static string? Parse(string outcomeJson, RuleEvaluationStatus status)
    {
        if (string.IsNullOrWhiteSpace(outcomeJson)) return null;

        try
        {
            using JsonDocument document = JsonDocument.Parse(outcomeJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;

            JsonElement root = document.RootElement;
            if (TryGetProperty(root, "outcomes", out JsonElement outcomes)
                && outcomes.ValueKind == JsonValueKind.Object)
            {
                root = outcomes;
            }

            if (TryGetProperty(root, status.ToString(), out JsonElement configured))
            {
                return ReadMessage(configured);
            }

            return TryGetProperty(document.RootElement, "message", out JsonElement message)
                ? ReadString(message)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadMessage(JsonElement configured)
    {
        if (configured.ValueKind == JsonValueKind.String) return ReadString(configured);
        return configured.ValueKind == JsonValueKind.Object
            && TryGetProperty(configured, "message", out JsonElement message)
                ? ReadString(message)
                : null;
    }

    private static string? ReadString(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String) return null;
        string? message = value.GetString()?.Trim();
        return string.IsNullOrWhiteSpace(message) ? null : message;
    }

    private static bool TryGetProperty(JsonElement value, string name, out JsonElement property)
    {
        foreach (JsonProperty candidate in value.EnumerateObject())
        {
            if (string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                property = candidate.Value;
                return true;
            }
        }

        property = default;
        return false;
    }
}
