using System.Net;
using DES.Framework.Core.Domain;
using DES.Framework.Core.Exceptions;
using DES.Modules.Rules.Contracts;

namespace DES.Modules.Rules.Domain;

public sealed class DocumentSetting : AggregateRoot<Guid>, IHasTenant
{
    public const int MaxRules = 1000;

    private readonly List<DocumentSettingRule> _rules = [];

    public string TenantId { get; private set; } = default!;
    public string DocumentCode { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public bool IsEnabled { get; private set; }
    public int SortOrder { get; private set; }
    public TemplateDocumentDisposition DefaultDisposition { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public long Version { get; private set; }
    public IReadOnlyCollection<DocumentSettingRule> Rules => _rules.AsReadOnly();

    private DocumentSetting() { }

    public static DocumentSetting Create(string tenantId, string documentCode, string name, int sortOrder, DateTime utcNow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        return new DocumentSetting
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            DocumentCode = TemplateDocumentRequirement.NormalizeDocumentCode(documentCode),
            Name = NormalizeName(name),
            IsEnabled = true,
            SortOrder = NormalizeSortOrder(sortOrder),
            DefaultDisposition = TemplateDocumentDisposition.Submit,
            UpdatedAtUtc = utcNow,
            Version = 1,
        };
    }

    public void Update(long expectedVersion, string name, bool isEnabled, int sortOrder, TemplateDocumentDisposition defaultDisposition, IEnumerable<Guid> ruleDefinitionIds, DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(ruleDefinitionIds);
        if (Version != expectedVersion) throw new CustomException("The document setting was modified by another request.", (IEnumerable<string>?)null, HttpStatusCode.Conflict);
        if (!Enum.IsDefined(defaultDisposition)) throw new ArgumentOutOfRangeException(nameof(defaultDisposition));
        Guid[] ids = ruleDefinitionIds.Where(x => x != Guid.Empty).Distinct().ToArray();
        if (ids.Length > MaxRules) throw new ArgumentOutOfRangeException(nameof(ruleDefinitionIds));
        Name = NormalizeName(name);
        IsEnabled = isEnabled;
        SortOrder = NormalizeSortOrder(sortOrder);
        DefaultDisposition = defaultDisposition;
        _rules.RemoveAll(x => !ids.Contains(x.RuleDefinitionId));
        foreach (Guid id in ids.Where(id => _rules.All(x => x.RuleDefinitionId != id))) _rules.Add(DocumentSettingRule.Create(Id, id));
        UpdatedAtUtc = utcNow;
        Version++;
    }

    private static string NormalizeName(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        string result = value.Trim();
        return result.Length <= 200 ? result : throw new ArgumentOutOfRangeException(nameof(value));
    }

    private static int NormalizeSortOrder(int value) => value is >= 1 and <= 1000 ? value : throw new ArgumentOutOfRangeException(nameof(value));
}

public sealed class DocumentSettingRule : BaseEntity<Guid>
{
    public Guid DocumentSettingId { get; private set; }
    public Guid RuleDefinitionId { get; private set; }

    private DocumentSettingRule() { }

    internal static DocumentSettingRule Create(Guid settingId, Guid ruleDefinitionId) => new() { Id = Guid.CreateVersion7(), DocumentSettingId = settingId, RuleDefinitionId = ruleDefinitionId };
}
