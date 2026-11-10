using System.Net;
using System.Text.Json;
using DES.Framework.Core.Exceptions;
using DES.Framework.Shared.Multitenancy;
using DES.Modules.Rules.Contracts;
using DES.Modules.Rules.Domain;
using DES.Modules.Rules.Persistence;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace DES.Modules.Rules.Runtime;

internal sealed class ComplexValidationPlanReader(
    RulesDbContext db,
    IMultiTenantContextAccessor<AppTenantInfo> tenantAccessor) : IComplexValidationPlanReader
{
    public async Task<IReadOnlyList<ComplexValidationPlanItem>> ReadAsync(
        Guid templateVersionId,
        CancellationToken cancellationToken = default)
    {
        if (templateVersionId == Guid.Empty) throw new ArgumentException("Template version is required.", nameof(templateVersionId));
        string tenantId = tenantAccessor.MultiTenantContext.TenantInfo?.Id
            ?? throw new UnauthorizedException("Tenant context is required.");

        RuleTemplate template = await db.RuleTemplates
            .AsNoTracking()
            .Where(item => item.TenantId == tenantId)
            .Include(item => item.Versions).ThenInclude(version => version.RuleAssignments)
            .AsSplitQuery()
            .SingleOrDefaultAsync(item => item.Versions.Any(version => version.Id == templateVersionId), cancellationToken)
            .ConfigureAwait(false)
            ?? throw new CustomException("Template version was not found.", (IEnumerable<string>?)null, HttpStatusCode.NotFound);
        RuleTemplateVersion templateVersion = template.Versions.Single(version => version.Id == templateVersionId);
        if (templateVersion.Status is not (RuleLifecycle.Published or RuleLifecycle.Retired))
        {
            throw new CustomException("Only a published or retired template version can be planned.", (IEnumerable<string>?)null, HttpStatusCode.Conflict);
        }

        Guid[] versionIds = templateVersion.RuleAssignments
            .Select(assignment => assignment.RuleVersionId)
            .Distinct()
            .ToArray();
        RuleDefinition[] definitions = await db.RuleDefinitions
            .AsNoTracking()
            .Where(definition => definition.TenantId == tenantId
                && definition.Versions.Any(version => versionIds.Contains(version.Id)))
            .Include(definition => definition.Versions.Where(version => versionIds.Contains(version.Id)))
            .AsSplitQuery()
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        Dictionary<Guid, (RuleDefinition Definition, RuleVersion Version)> versions = definitions
            .SelectMany(definition => definition.Versions.Select(version => (version.Id, definition, version)))
            .ToDictionary(item => item.Id, item => (item.definition, item.version));
        if (versions.Count != versionIds.Length
            || versions.Values.Any(item => item.Version.Status is not (RuleLifecycle.Published or RuleLifecycle.Retired)))
        {
            throw new CustomException("Every assigned rule version must exist in the same tenant and be published or retired.", (IEnumerable<string>?)null, HttpStatusCode.Conflict);
        }

        var plan = new List<ComplexValidationPlanItem>();
        foreach (TemplateRuleAssignment assignment in templateVersion.RuleAssignments
                     .OrderBy(item => item.DocumentCode, StringComparer.Ordinal)
                     .ThenBy(item => item.RuleVersionId))
        {
            cancellationToken.ThrowIfCancellationRequested();
            using JsonDocument parameters = JsonDocument.Parse(assignment.ParametersJson);
            if (!TryReadString(parameters.RootElement, "executorKey", out string? executorKey)) continue;
            (RuleDefinition definition, RuleVersion version) = versions[assignment.RuleVersionId];
            var documentCodes = new HashSet<string>(StringComparer.Ordinal);
            if (assignment.DocumentCode is not null) documentCodes.Add(assignment.DocumentCode);
            CollectAstDocumentCodes(version.AstJson, documentCodes);
            CollectParameterDocumentCodes(parameters.RootElement, documentCodes);
            if (documentCodes.Count == 0)
            {
                throw new CustomException($"Complex rule {definition.StableCode} does not declare a document dependency.", (IEnumerable<string>?)null, HttpStatusCode.Conflict);
            }
            plan.Add(new ComplexValidationPlanItem(
                version.Id,
                definition.StableCode,
                definition.Name,
                definition.Category,
                executorKey!,
                assignment.ParametersJson,
                documentCodes.OrderBy(code => code, StringComparer.Ordinal).ToArray()));
        }

        return plan;
    }

    private static void CollectAstDocumentCodes(string astJson, ISet<string> documentCodes)
    {
        using JsonDocument ast = JsonDocument.Parse(astJson);
        CollectAstDocumentCodes(ast.RootElement, documentCodes);
    }

    private static void CollectAstDocumentCodes(JsonElement value, ISet<string> documentCodes)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            if (TryReadString(value, "type", out string? type)
                && string.Equals(type, "field", StringComparison.Ordinal)
                && TryReadString(value, "documentCode", out string? documentCode))
            {
                documentCodes.Add(NormalizeDocumentCode(documentCode!));
            }
            foreach (JsonProperty property in value.EnumerateObject()) CollectAstDocumentCodes(property.Value, documentCodes);
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in value.EnumerateArray()) CollectAstDocumentCodes(item, documentCodes);
        }
    }

    private static void CollectParameterDocumentCodes(JsonElement parameters, ISet<string> documentCodes)
    {
        foreach (JsonProperty property in parameters.EnumerateObject())
        {
            if (property.Name.EndsWith("DocumentCode", StringComparison.OrdinalIgnoreCase)
                && property.Value.ValueKind == JsonValueKind.String
                && property.Value.GetString() is { } documentCode)
            {
                documentCodes.Add(NormalizeDocumentCode(documentCode));
            }
            else if (property.Name.EndsWith("DocumentCodes", StringComparison.OrdinalIgnoreCase)
                && property.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in property.Value.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String && item.GetString() is { } arrayDocumentCode)
                    {
                        documentCodes.Add(NormalizeDocumentCode(arrayDocumentCode));
                    }
                }
            }
        }
    }

    private static bool TryReadString(JsonElement value, string propertyName, out string? result)
    {
        foreach (JsonProperty property in value.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase)
                && property.Value.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(property.Value.GetString()))
            {
                result = property.Value.GetString()!.Trim();
                return true;
            }
        }
        result = null;
        return false;
    }

    private static string NormalizeDocumentCode(string value) =>
        TemplateDocumentRequirement.NormalizeDocumentCode(value);
}
