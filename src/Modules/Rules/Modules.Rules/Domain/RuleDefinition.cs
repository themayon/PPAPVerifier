using System.Net;
using System.Text.Json;
using DES.Framework.Core.Domain;
using DES.Framework.Core.Exceptions;
using DES.Modules.Rules.Contracts;

namespace DES.Modules.Rules.Domain;

public sealed class RuleDefinition : AggregateRoot<Guid>, IHasTenant
{
    private readonly List<RuleVersion> _versions = [];
    public string TenantId { get; private set; } = default!;
    public string StableCode { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string? Description { get; private set; }
    public RuleCategory Category { get; private set; }
    public RuleLifecycle Lifecycle { get; private set; }
    public Guid? CurrentPublishedVersionId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public long Version { get; private set; }
    public IReadOnlyCollection<RuleVersion> Versions => _versions.AsReadOnly();
    private RuleDefinition() { }

    public static RuleDefinition Create(string tenantId, string stableCode, string name, string? description, RuleCategory category, string astJson, string outcomeJson, Guid userId, DateTime utcNow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        var definition = new RuleDefinition
        {
            Id = Guid.CreateVersion7(), TenantId = tenantId, StableCode = NormalizeCode(stableCode), Name = NormalizeRequired(name, 200),
            Description = NormalizeOptional(description, 2000), Category = category, Lifecycle = RuleLifecycle.Draft,
            CreatedAtUtc = utcNow, UpdatedAtUtc = utcNow, Version = 1,
        };
        definition._versions.Add(RuleVersion.Create(definition.Id, 1, astJson, outcomeJson, userId, utcNow));
        return definition;
    }

    public void UpdateDraft(Guid versionId, long expectedVersion, string name, string? description, RuleCategory category, string astJson, string outcomeJson, DateTime utcNow)
    {
        GuardVersion(expectedVersion);
        RuleVersion version = Find(versionId);
        version.UpdateDraft(astJson, outcomeJson);
        Name = NormalizeRequired(name, 200); Description = NormalizeOptional(description, 2000); Category = category;
        Touch(utcNow);
    }

    public void SubmitForReview(Guid versionId, long expectedVersion, DateTime utcNow)
    {
        GuardVersion(expectedVersion); Find(versionId).SubmitForReview(); Lifecycle = RuleLifecycle.InReview; Touch(utcNow);
    }

    public void Publish(Guid versionId, long expectedVersion, bool retireCurrent, Guid userId, DateTime utcNow)
    {
        GuardVersion(expectedVersion);
        RuleVersion selected = Find(versionId);
        RuleVersion? current = CurrentPublishedVersionId is Guid currentId ? _versions.Single(v => v.Id == currentId) : null;
        if (current is not null && current.Id != selected.Id)
        {
            if (!retireCurrent) Conflict("A published version already exists.");
            current.Retire(utcNow);
        }
        selected.Publish(userId, utcNow);
        CurrentPublishedVersionId = selected.Id; Lifecycle = RuleLifecycle.Published; Touch(utcNow);
    }

    public void Retire(Guid versionId, long expectedVersion, DateTime utcNow)
    {
        GuardVersion(expectedVersion); RuleVersion selected = Find(versionId); selected.Retire(utcNow);
        if (CurrentPublishedVersionId == selected.Id) CurrentPublishedVersionId = null;
        Lifecycle = _versions.Any(v => v.Status == RuleLifecycle.Published) ? RuleLifecycle.Published : RuleLifecycle.Retired;
        Touch(utcNow);
    }

    public RuleVersion ClonePublished(Guid versionId, long expectedVersion, Guid userId, DateTime utcNow)
    {
        GuardVersion(expectedVersion); RuleVersion source = Find(versionId);
        if (source.Status != RuleLifecycle.Published) Conflict("Only a published version can be cloned.");
        var clone = RuleVersion.Create(Id, _versions.Max(v => v.VersionNumber) + 1, source.AstJson, source.OutcomeJson, userId, utcNow);
        _versions.Add(clone); Lifecycle = RuleLifecycle.Draft; Touch(utcNow); return clone;
    }

    public void EnsureDeletable(long expectedVersion)
    {
        GuardVersion(expectedVersion);
        if (CurrentPublishedVersionId is not null || _versions.Any(v => v.Status is RuleLifecycle.Published or RuleLifecycle.Retired))
            Conflict("Only rules that have never been published can be deleted. Retire the rule instead.");
    }

    private RuleVersion Find(Guid id) => _versions.SingleOrDefault(v => v.Id == id) ?? throw new CustomException("Rule version was not found.", (IEnumerable<string>?)null, HttpStatusCode.NotFound);
    private void GuardVersion(long expected) { if (Version != expected) Conflict("The rule was modified by another request."); }
    private void Touch(DateTime now) { UpdatedAtUtc = now; Version++; }
    private static void Conflict(string message) => throw new CustomException(message, (IEnumerable<string>?)null, HttpStatusCode.Conflict);
    private static string NormalizeCode(string value) { string result = NormalizeRequired(value, 64).ToUpperInvariant(); if (result.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))) throw new ArgumentException("StableCode contains invalid characters.", nameof(value)); return result; }
    private static string NormalizeRequired(string value, int max) { ArgumentException.ThrowIfNullOrWhiteSpace(value); string result = value.Trim(); if (result.Length > max) throw new ArgumentOutOfRangeException(nameof(value)); return result; }
    private static string? NormalizeOptional(string? value, int max) { if (string.IsNullOrWhiteSpace(value)) return null; string result = value.Trim(); if (result.Length > max) throw new ArgumentOutOfRangeException(nameof(value)); return result; }
}

public sealed class RuleVersion : BaseEntity<Guid>
{
    public Guid RuleDefinitionId { get; private set; }
    public int VersionNumber { get; private set; }
    public RuleLifecycle Status { get; private set; }
    public string AstJson { get; private set; } = default!;
    public string OutcomeJson { get; private set; } = default!;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? PublishedAtUtc { get; private set; }
    public DateTime? RetiredAtUtc { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    private RuleVersion() { }
    internal static RuleVersion Create(Guid definitionId, int number, string ast, string outcome, Guid userId, DateTime now) => new() { Id = Guid.CreateVersion7(), RuleDefinitionId = definitionId, VersionNumber = number, Status = RuleLifecycle.Draft, AstJson = ValidateAst(ast), OutcomeJson = ValidateOutcome(outcome), CreatedByUserId = userId, CreatedAtUtc = now };
    internal void UpdateDraft(string ast, string outcome) { if (Status != RuleLifecycle.Draft) Conflict("Only draft versions can be updated."); AstJson = ValidateAst(ast); OutcomeJson = ValidateOutcome(outcome); }
    internal void SubmitForReview() { if (Status != RuleLifecycle.Draft) Conflict("Only draft versions can be submitted."); Status = RuleLifecycle.InReview; }
    internal void Publish(Guid userId, DateTime now) { if (Status != RuleLifecycle.InReview) Conflict("Only versions in review can be published."); if (userId == Guid.Empty) throw new ArgumentException("Publisher is required.", nameof(userId)); Status = RuleLifecycle.Published; PublishedAtUtc = now; }
    internal void Retire(DateTime now) { if (Status != RuleLifecycle.Published) Conflict("Only published versions can be retired."); Status = RuleLifecycle.Retired; RetiredAtUtc = now; }
    private static string ValidateAst(string json) { _ = Runtime.RuleAstSerializer.Parse(json); return Canonicalize(json); }
    private static string ValidateOutcome(string json) { using JsonDocument doc = JsonDocument.Parse(json); if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new ArgumentException("Outcome must be a JSON object.", nameof(json)); return Canonicalize(json); }
    private static string Canonicalize(string json) { using JsonDocument doc = JsonDocument.Parse(json); return JsonSerializer.Serialize(doc.RootElement); }
    private static void Conflict(string message) => throw new CustomException(message, (IEnumerable<string>?)null, HttpStatusCode.Conflict);
}
