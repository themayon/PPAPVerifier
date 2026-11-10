using System.Net;
using DES.Framework.Core.Domain;
using DES.Framework.Core.Exceptions;
using DES.Modules.Rules.Contracts;

namespace DES.Modules.Rules.Domain;

public sealed class RuleColumn : AggregateRoot<Guid>, IHasTenant
{
    public const int NameMaxLength = 100;
    public const int KeyMaxLength = 128;
    public const int HeaderNamesMaxLength = 2000;
    public const int DescriptionMaxLength = 500;

    public string TenantId { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string Key { get; private set; } = default!;
    public RuleColumnKind Kind { get; private set; }
    public RuleColumnDataType DataType { get; private set; }
    public string? HeaderNames { get; private set; }
    public string? Description { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public long Version { get; private set; }

    private RuleColumn() { }

    public static RuleColumn Create(string tenantId, string name, string key, RuleColumnKind kind, RuleColumnDataType dataType, string? headerNames, string? description, DateTime utcNow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        var column = new RuleColumn { Id = Guid.CreateVersion7(), TenantId = tenantId, Kind = kind, IsActive = true, CreatedAtUtc = utcNow, Version = 1 };
        column.Apply(name, key, dataType, headerNames, description, utcNow);
        return column;
    }

    public void Update(long expectedVersion, string name, string key, RuleColumnDataType dataType, string? headerNames, string? description, bool isActive, DateTime utcNow)
    {
        if (Version != expectedVersion) throw new CustomException("The column was modified by another request.", (IEnumerable<string>?)null, HttpStatusCode.Conflict);
        Apply(name, key, dataType, headerNames, description, utcNow);
        IsActive = isActive;
        Version++;
    }

    public static bool IsValidKey(string? key) =>
        !string.IsNullOrWhiteSpace(key)
        && key.Trim().Length <= KeyMaxLength
        && key.Trim().Split('.').All(segment => segment.Length is > 0 and <= 64 && segment.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-'));

    private void Apply(string name, string key, RuleColumnDataType dataType, string? headerNames, string? description, DateTime utcNow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!IsValidKey(key)) throw new ArgumentException("Column key must be dot-separated letters, numbers, dashes, or underscores.", nameof(key));
        Name = Limit(name.Trim(), NameMaxLength, nameof(name));
        Key = key.Trim();
        DataType = dataType;
        HeaderNames = Optional(NormalizeHeaderNames(headerNames), HeaderNamesMaxLength, nameof(headerNames));
        Description = Optional(description?.Trim(), DescriptionMaxLength, nameof(description));
        UpdatedAtUtc = utcNow;
    }

    private static string? NormalizeHeaderNames(string? value) => string.IsNullOrWhiteSpace(value)
        ? null
        : string.Join(", ", value.Split([',', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase));

    private static string Limit(string value, int max, string name) => value.Length <= max ? value : throw new ArgumentOutOfRangeException(name);
    private static string? Optional(string? value, int max, string name) => string.IsNullOrWhiteSpace(value) ? null : Limit(value, max, name);
}
