using System.Net;
using System.Text;
using System.Text.Json;
using DES.Framework.Core.Domain;
using DES.Framework.Core.Exceptions;
using DES.Modules.Rules.Contracts;

namespace DES.Modules.Rules.Domain;

public sealed class RuleTemplate : AggregateRoot<Guid>, IHasTenant
{
    private readonly List<RuleTemplateVersion> _versions = [];
    private readonly List<RuleTemplateDocument> _documents = [];
    private readonly List<RuleTemplateRule> _rules = [];

    public string TenantId { get; private set; } = default!;
    public string StableCode { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string? Description { get; private set; }
    public RuleLifecycle Lifecycle { get; private set; }
    public Guid? CurrentPublishedVersionId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public long Version { get; private set; }
    public IReadOnlyCollection<RuleTemplateVersion> Versions => _versions.AsReadOnly();
    public IReadOnlyCollection<RuleTemplateDocument> Documents => _documents.AsReadOnly();
    public IReadOnlyCollection<RuleTemplateRule> Rules => _rules.AsReadOnly();

    private RuleTemplate() { }

    public static RuleTemplate CreateForSetup(string tenantId, string stableCode, string name, string? description, DateTime utcNow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        return new RuleTemplate
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            StableCode = NormalizeCode(stableCode),
            Name = NormalizeRequired(name, 200),
            Description = NormalizeOptional(description, 2000),
            Lifecycle = RuleLifecycle.Draft,
            CreatedAtUtc = utcNow,
            UpdatedAtUtc = utcNow,
            Version = 1,
        };
    }

    public void ConfigureSetup(long expectedVersion, string name, string? description, IEnumerable<TemplateSetupDocumentInput> documents, IEnumerable<TemplateSetupRuleInput> rules, DateTime utcNow)
    {
        GuardVersion(expectedVersion);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(rules);
        List<RuleTemplateDocument> nextDocuments = documents.Select(x => RuleTemplateDocument.Create(Id, x.DocumentCode, x.IsEnabled, x.Disposition, x.SortOrder)).ToList();
        if (nextDocuments.Select(x => x.DocumentCode).Distinct(StringComparer.Ordinal).Count() != nextDocuments.Count) Conflict("Template document codes must be unique.");
        HashSet<string> codes = nextDocuments.Select(x => x.DocumentCode).ToHashSet(StringComparer.Ordinal);
        List<RuleTemplateRule> nextRules = rules.Select(x => RuleTemplateRule.Create(Id, x.DocumentCode, x.RuleDefinitionId, x.IsEnabled)).ToList();
        if (nextRules.Any(x => !codes.Contains(x.DocumentCode))) Conflict("Every template rule must belong to a template document.");
        if (nextRules.Select(x => (x.DocumentCode, x.RuleDefinitionId)).Distinct().Count() != nextRules.Count) Conflict("A rule can be added to a document only once.");
        _documents.Clear();
        _documents.AddRange(nextDocuments);
        _rules.Clear();
        _rules.AddRange(nextRules);
        Name = NormalizeRequired(name, 200);
        Description = NormalizeOptional(description, 2000);
        Touch(utcNow);
    }

    public RuleTemplateVersion PublishSetup(IEnumerable<TemplateDocumentRequirementInput> requirements, IEnumerable<TemplateRuleAssignmentInput> assignments, Guid userId, DateTime utcNow)
    {
        RuleTemplateVersion version = RuleTemplateVersion.Create(Id, _versions.Count == 0 ? 1 : _versions.Max(v => v.VersionNumber) + 1, userId, utcNow);
        version.Configure(requirements, assignments);
        version.SubmitForReview();
        version.Publish(userId, utcNow);
        if (CurrentPublishedVersionId is Guid currentId) _versions.Single(v => v.Id == currentId).Retire(utcNow);
        _versions.Add(version);
        CurrentPublishedVersionId = version.Id;
        Lifecycle = RuleLifecycle.Published;
        Touch(utcNow);
        return version;
    }

    public static RuleTemplate Create(string tenantId, string stableCode, string name, string? description, Guid userId, DateTime utcNow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        if (userId == Guid.Empty) throw new ArgumentException("Creator is required.", nameof(userId));
        var template = new RuleTemplate
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            StableCode = NormalizeCode(stableCode),
            Name = NormalizeRequired(name, 200),
            Description = NormalizeOptional(description, 2000),
            Lifecycle = RuleLifecycle.Draft,
            CreatedAtUtc = utcNow,
            UpdatedAtUtc = utcNow,
            Version = 1,
        };
        template._versions.Add(RuleTemplateVersion.Create(template.Id, 1, userId, utcNow));
        return template;
    }

    public void ConfigureDraft(Guid versionId, long expectedVersion, string name, string? description, IEnumerable<TemplateDocumentRequirementInput> requirements, IEnumerable<TemplateRuleAssignmentInput> assignments, DateTime utcNow)
    {
        GuardVersion(expectedVersion);
        ArgumentNullException.ThrowIfNull(requirements);
        ArgumentNullException.ThrowIfNull(assignments);
        RuleTemplateVersion version = Find(versionId);
        version.Configure(requirements, assignments);
        Name = NormalizeRequired(name, 200);
        Description = NormalizeOptional(description, 2000);
        Lifecycle = RuleLifecycle.Draft;
        Touch(utcNow);
    }

    public void SubmitForReview(Guid versionId, long expectedVersion, DateTime utcNow)
    {
        GuardVersion(expectedVersion);
        Find(versionId).SubmitForReview();
        Lifecycle = RuleLifecycle.InReview;
        Touch(utcNow);
    }

    public void Publish(Guid versionId, long expectedVersion, bool retireCurrent, Guid userId, DateTime utcNow)
    {
        GuardVersion(expectedVersion);
        RuleTemplateVersion selected = Find(versionId);
        RuleTemplateVersion? current = CurrentPublishedVersionId is Guid currentId ? _versions.Single(v => v.Id == currentId) : null;
        if (current is not null && current.Id != selected.Id)
        {
            if (!retireCurrent) Conflict("A published template version already exists.");
            current.Retire(utcNow);
        }
        selected.Publish(userId, utcNow);
        CurrentPublishedVersionId = selected.Id;
        Lifecycle = RuleLifecycle.Published;
        Touch(utcNow);
    }

    public void Retire(Guid versionId, long expectedVersion, DateTime utcNow)
    {
        GuardVersion(expectedVersion);
        RuleTemplateVersion selected = Find(versionId);
        selected.Retire(utcNow);
        if (CurrentPublishedVersionId == selected.Id) CurrentPublishedVersionId = null;
        Lifecycle = _versions.Any(v => v.Status == RuleLifecycle.Published) ? RuleLifecycle.Published : RuleLifecycle.Retired;
        Touch(utcNow);
    }

    public RuleTemplateVersion ClonePublished(Guid versionId, long expectedVersion, Guid userId, DateTime utcNow)
    {
        GuardVersion(expectedVersion);
        RuleTemplateVersion source = Find(versionId);
        if (source.Status != RuleLifecycle.Published) Conflict("Only a published template version can be cloned.");
        RuleTemplateVersion clone = source.Clone(_versions.Max(v => v.VersionNumber) + 1, userId, utcNow);
        _versions.Add(clone);
        Lifecycle = RuleLifecycle.Draft;
        Touch(utcNow);
        return clone;
    }

    private RuleTemplateVersion Find(Guid id) => _versions.SingleOrDefault(v => v.Id == id) ?? throw new CustomException("Template version was not found.", (IEnumerable<string>?)null, HttpStatusCode.NotFound);
    private void GuardVersion(long expected) { if (Version != expected) Conflict("The template was modified by another request."); }
    private void Touch(DateTime now) { UpdatedAtUtc = now; Version++; }
    private static void Conflict(string message) => throw new CustomException(message, (IEnumerable<string>?)null, HttpStatusCode.Conflict);
    private static string NormalizeCode(string value) { string result = NormalizeRequired(value, 64).ToUpperInvariant(); if (result.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))) throw new ArgumentException("StableCode contains invalid characters.", nameof(value)); return result; }
    private static string NormalizeRequired(string value, int max) { ArgumentException.ThrowIfNullOrWhiteSpace(value); string result = value.Trim(); if (result.Length > max) throw new ArgumentOutOfRangeException(nameof(value)); return result; }
    private static string? NormalizeOptional(string? value, int max) { if (string.IsNullOrWhiteSpace(value)) return null; string result = value.Trim(); if (result.Length > max) throw new ArgumentOutOfRangeException(nameof(value)); return result; }
}

public sealed class RuleTemplateVersion : BaseEntity<Guid>
{
    private readonly List<TemplateDocumentRequirement> _documentRequirements = [];
    private readonly List<TemplateRuleAssignment> _ruleAssignments = [];

    public Guid RuleTemplateId { get; private set; }
    public int VersionNumber { get; private set; }
    public RuleLifecycle Status { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? PublishedAtUtc { get; private set; }
    public DateTime? RetiredAtUtc { get; private set; }
    public IReadOnlyCollection<TemplateDocumentRequirement> DocumentRequirements => _documentRequirements.AsReadOnly();
    public IReadOnlyCollection<TemplateRuleAssignment> RuleAssignments => _ruleAssignments.AsReadOnly();

    private RuleTemplateVersion() { }

    internal static RuleTemplateVersion Create(Guid templateId, int number, Guid userId, DateTime now)
    {
        if (userId == Guid.Empty) throw new ArgumentException("Creator is required.", nameof(userId));
        return new() { Id = Guid.CreateVersion7(), RuleTemplateId = templateId, VersionNumber = number, Status = RuleLifecycle.Draft, CreatedByUserId = userId, CreatedAtUtc = now };
    }

    internal void Configure(IEnumerable<TemplateDocumentRequirementInput> requirements, IEnumerable<TemplateRuleAssignmentInput> assignments)
    {
        if (Status != RuleLifecycle.Draft) Conflict("Only draft template versions can be configured.");
        List<TemplateDocumentRequirement> replacementRequirements = requirements.Select(x => TemplateDocumentRequirement.Create(Id, x.DocumentCode, x.IsRequired, x.SortOrder, x.Disposition)).ToList();
        List<TemplateRuleAssignment> replacementAssignments = assignments.Select(x => TemplateRuleAssignment.Create(Id, x.RuleVersionId, x.DocumentCode, x.ParametersJson)).ToList();
        if (replacementRequirements.Select(x => x.DocumentCode).Distinct(StringComparer.Ordinal).Count() != replacementRequirements.Count) Conflict("Document requirement codes must be unique within a template version.");
        if (replacementAssignments.Select(x => (x.RuleVersionId, x.DocumentCode)).Distinct().Count() != replacementAssignments.Count) Conflict("Rule assignment keys must be unique within a template version.");
        _documentRequirements.Clear();
        _documentRequirements.AddRange(replacementRequirements);
        _ruleAssignments.Clear();
        _ruleAssignments.AddRange(replacementAssignments);
    }

    internal void SubmitForReview() { if (Status != RuleLifecycle.Draft) Conflict("Only draft template versions can be submitted."); Status = RuleLifecycle.InReview; }
    internal void Publish(Guid userId, DateTime now) { if (Status != RuleLifecycle.InReview) Conflict("Only template versions in review can be published."); if (userId == Guid.Empty) throw new ArgumentException("Publisher is required.", nameof(userId)); Status = RuleLifecycle.Published; PublishedAtUtc = now; }
    internal void Retire(DateTime now) { if (Status != RuleLifecycle.Published) Conflict("Only published template versions can be retired."); Status = RuleLifecycle.Retired; RetiredAtUtc = now; }

    internal RuleTemplateVersion Clone(int number, Guid userId, DateTime now)
    {
        RuleTemplateVersion clone = Create(RuleTemplateId, number, userId, now);
        clone._documentRequirements.AddRange(_documentRequirements.Select(x => TemplateDocumentRequirement.Create(clone.Id, x.DocumentCode, x.IsRequired, x.SortOrder, x.Disposition)));
        clone._ruleAssignments.AddRange(_ruleAssignments.Select(x => TemplateRuleAssignment.Create(clone.Id, x.RuleVersionId, x.DocumentCode, x.ParametersJson)));
        return clone;
    }

    private static void Conflict(string message) => throw new CustomException(message, (IEnumerable<string>?)null, HttpStatusCode.Conflict);
}

public sealed class TemplateDocumentRequirement : BaseEntity<Guid>
{
    public Guid TemplateVersionId { get; private set; }
    public string DocumentCode { get; private set; } = default!;
    public bool IsRequired { get; private set; }
    public TemplateDocumentDisposition Disposition { get; private set; }
    public int SortOrder { get; private set; }

    private TemplateDocumentRequirement() { }

    internal static TemplateDocumentRequirement Create(Guid versionId, string documentCode, bool required, int sortOrder, TemplateDocumentDisposition? disposition = null)
    {
        if (sortOrder is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(sortOrder));
        TemplateDocumentDisposition normalizedDisposition = disposition ?? (required ? TemplateDocumentDisposition.Submit : TemplateDocumentDisposition.Retain);
        if (!Enum.IsDefined(normalizedDisposition)) throw new ArgumentOutOfRangeException(nameof(disposition));
        bool normalizedRequired = normalizedDisposition != TemplateDocumentDisposition.Retain;
        if (disposition.HasValue && required != normalizedRequired) throw new ArgumentException("IsRequired must match the supplied disposition.", nameof(required));
        return new() { Id = Guid.CreateVersion7(), TemplateVersionId = versionId, DocumentCode = NormalizeDocumentCode(documentCode), IsRequired = normalizedRequired, Disposition = normalizedDisposition, SortOrder = sortOrder };
    }

    internal static string NormalizeDocumentCode(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        string result = value.Trim().ToUpperInvariant();
        if (result.Length > 32) throw new ArgumentOutOfRangeException(nameof(value));
        if (result.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))) throw new ArgumentException("DocumentCode contains invalid characters.", nameof(value));
        return result;
    }
}

public sealed class RuleTemplateDocument : BaseEntity<Guid>
{
    public Guid RuleTemplateId { get; private set; }
    public string DocumentCode { get; private set; } = default!;
    public bool IsEnabled { get; private set; }
    public TemplateDocumentDisposition Disposition { get; private set; }
    public int SortOrder { get; private set; }

    private RuleTemplateDocument() { }

    internal static RuleTemplateDocument Create(Guid templateId, string documentCode, bool isEnabled, TemplateDocumentDisposition disposition, int sortOrder)
    {
        if (sortOrder is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(sortOrder));
        if (!Enum.IsDefined(disposition)) throw new ArgumentOutOfRangeException(nameof(disposition));
        return new() { Id = Guid.CreateVersion7(), RuleTemplateId = templateId, DocumentCode = TemplateDocumentRequirement.NormalizeDocumentCode(documentCode), IsEnabled = isEnabled, Disposition = disposition, SortOrder = sortOrder };
    }
}

public sealed class RuleTemplateRule : BaseEntity<Guid>
{
    public Guid RuleTemplateId { get; private set; }
    public string DocumentCode { get; private set; } = default!;
    public Guid RuleDefinitionId { get; private set; }
    public bool IsEnabled { get; private set; }

    private RuleTemplateRule() { }

    internal static RuleTemplateRule Create(Guid templateId, string documentCode, Guid ruleDefinitionId, bool isEnabled)
    {
        if (ruleDefinitionId == Guid.Empty) throw new ArgumentException("Rule is required.", nameof(ruleDefinitionId));
        return new() { Id = Guid.CreateVersion7(), RuleTemplateId = templateId, DocumentCode = TemplateDocumentRequirement.NormalizeDocumentCode(documentCode), RuleDefinitionId = ruleDefinitionId, IsEnabled = isEnabled };
    }
}

public sealed class TemplateRuleAssignment : BaseEntity<Guid>
{
    public Guid TemplateVersionId { get; private set; }
    public Guid RuleVersionId { get; private set; }
    public string? DocumentCode { get; private set; }
    public string ParametersJson { get; private set; } = default!;

    private TemplateRuleAssignment() { }

    internal static TemplateRuleAssignment Create(Guid versionId, Guid ruleVersionId, string? documentCode, string parametersJson)
    {
        if (ruleVersionId == Guid.Empty) throw new ArgumentException("Rule version is required.", nameof(ruleVersionId));
        return new() { Id = Guid.CreateVersion7(), TemplateVersionId = versionId, RuleVersionId = ruleVersionId, DocumentCode = string.IsNullOrWhiteSpace(documentCode) ? null : TemplateDocumentRequirement.NormalizeDocumentCode(documentCode), ParametersJson = NormalizeParameters(parametersJson) };
    }

    private static string NormalizeParameters(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        if (Encoding.UTF8.GetByteCount(json) > 16 * 1024) throw new ArgumentOutOfRangeException(nameof(json));
        using JsonDocument document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new ArgumentException("Parameters must be a JSON object.", nameof(json));
        string canonical = JsonSerializer.Serialize(document.RootElement);
        if (Encoding.UTF8.GetByteCount(canonical) > 16 * 1024) throw new ArgumentOutOfRangeException(nameof(json));
        return canonical;
    }
}
