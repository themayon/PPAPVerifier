using System.Net;
using System.Net.Mail;
using DES.Framework.Core.Domain;
using DES.Framework.Core.Exceptions;

namespace DES.Modules.Ppap.Domain;

public sealed record PpapSupplierProfile(
    string Name,
    string? TaxId = null,
    string? DunsNumber = null,
    string? Address = null,
    string? City = null,
    string? Country = null,
    string? SiteName = null,
    string? SiteAddress = null,
    bool SiteSameAsHeadquarters = false,
    string? ContactName = null,
    string? ContactJobTitle = null,
    string? Email = null,
    string? Phone = null,
    string? TierLevel = null,
    string? QualityGrade = null);

public sealed record PpapSupplierCertificateInput(Guid FileAssetId, string FileName, string CertificateType);

public sealed class PpapSupplier : AggregateRoot<Guid>, IHasTenant
{
    public const int MaxLinkedUsers = 50;
    public const int MaxCertificates = 20;
    public static readonly IReadOnlyList<string> CertificateTypes = ["ISO 9001", "IATF 16949", "ISO 14001", "Other"];
    public static readonly IReadOnlyList<string> TierLevels = ["Tier 1", "Tier 2", "Tier 3"];
    public static readonly IReadOnlyList<string> QualityGrades = ["A+", "A", "B", "C", "D"];

    private readonly List<PpapSupplierUser> _users = [];
    private readonly List<PpapSupplierCertificate> _certificates = [];

    public string TenantId { get; private set; } = default!;
    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string? TaxId { get; private set; }
    public string? DunsNumber { get; private set; }
    public string? Address { get; private set; }
    public string? City { get; private set; }
    public string? Country { get; private set; }
    public string? Region { get; private set; }
    public string? TierLevel { get; private set; }
    public string? QualityGrade { get; private set; }
    public string? SiteName { get; private set; }
    public string? SiteAddress { get; private set; }
    public bool SiteSameAsHeadquarters { get; private set; }
    public string? ContactName { get; private set; }
    public string? ContactJobTitle { get; private set; }
    public string? Email { get; private set; }
    public string? Phone { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public int Version { get; private set; }
    public IReadOnlyCollection<PpapSupplierUser> Users => _users.AsReadOnly();
    public IReadOnlyCollection<PpapSupplierCertificate> Certificates => _certificates.AsReadOnly();

    private PpapSupplier() { }

    public static PpapSupplier Create(string tenantId, string code, PpapSupplierProfile profile, IEnumerable<Guid> userIds, IEnumerable<PpapSupplierCertificateInput> certificates, DateTime utcNow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        var supplier = new PpapSupplier { Id = Guid.CreateVersion7(), TenantId = tenantId.Trim(), IsActive = true, CreatedAtUtc = utcNow, Version = 1 };
        supplier.Apply(code, profile, userIds, certificates, utcNow);
        return supplier;
    }

    public void Update(int expectedVersion, string code, PpapSupplierProfile profile, bool isActive, IEnumerable<Guid> userIds, IEnumerable<PpapSupplierCertificateInput> certificates, DateTime utcNow)
    {
        if (Version != expectedVersion) throw new CustomException("The supplier was modified by another request.", (IEnumerable<string>?)null, HttpStatusCode.Conflict);
        Apply(code, profile, userIds, certificates, utcNow);
        IsActive = isActive;
        Version = checked(Version + 1);
    }

    public static string? NormalizeDuns(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string digits = new(value.Where(char.IsAsciiDigit).ToArray());
        if (digits.Length != 9 || value.Any(c => !(char.IsAsciiDigit(c) || c is '-' or ' '))) throw new ArgumentException("DUNS number must be 9 digits (00-000-0000).", nameof(value));
        return digits;
    }

    private void Apply(string code, PpapSupplierProfile profile, IEnumerable<Guid> userIds, IEnumerable<PpapSupplierCertificateInput> certificates, DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(userIds);
        ArgumentNullException.ThrowIfNull(certificates);
        string normalizedCode = Required(code, 50, nameof(code)).ToUpperInvariant();
        if (normalizedCode.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))) throw new ArgumentException("Supplier code may contain letters, numbers, dots, dashes, or underscores.", nameof(code));
        string? email = Optional(profile.Email, 256, nameof(profile.Email));
        if (email is not null && !MailAddress.TryCreate(email, out _)) throw new ArgumentException("Email is invalid.", nameof(profile));
        Guid[] ids = userIds.Where(x => x != Guid.Empty).Distinct().ToArray();
        if (ids.Length > MaxLinkedUsers) throw new ArgumentOutOfRangeException(nameof(userIds));
        PpapSupplierCertificateInput[] files = certificates.Where(x => x.FileAssetId != Guid.Empty).DistinctBy(x => x.FileAssetId).ToArray();
        if (files.Length > MaxCertificates) throw new ArgumentOutOfRangeException(nameof(certificates));

        Code = normalizedCode;
        Name = Required(profile.Name, 200, nameof(profile.Name));
        TaxId = Optional(profile.TaxId, 50, nameof(profile.TaxId));
        DunsNumber = NormalizeDuns(profile.DunsNumber);
        Address = Optional(profile.Address, 500, nameof(profile.Address));
        City = Optional(profile.City, 100, nameof(profile.City));
        Country = Optional(profile.Country, 100, nameof(profile.Country));
        Region = PpapRegions.FromCountry(Country);
        TierLevel = OneOf(profile.TierLevel, TierLevels, nameof(profile.TierLevel));
        QualityGrade = OneOf(profile.QualityGrade, QualityGrades, nameof(profile.QualityGrade));
        SiteSameAsHeadquarters = profile.SiteSameAsHeadquarters;
        SiteName = Optional(profile.SiteName, 200, nameof(profile.SiteName));
        SiteAddress = SiteSameAsHeadquarters ? null : Optional(profile.SiteAddress, 500, nameof(profile.SiteAddress));
        ContactName = Optional(profile.ContactName, 200, nameof(profile.ContactName));
        ContactJobTitle = Optional(profile.ContactJobTitle, 200, nameof(profile.ContactJobTitle));
        Email = email;
        Phone = Optional(profile.Phone, 50, nameof(profile.Phone));

        _users.RemoveAll(x => !ids.Contains(x.UserId));
        foreach (Guid id in ids.Where(id => _users.All(x => x.UserId != id))) _users.Add(PpapSupplierUser.Create(Id, id));
        _certificates.RemoveAll(x => files.All(f => f.FileAssetId != x.FileAssetId));
        foreach (PpapSupplierCertificateInput file in files)
        {
            PpapSupplierCertificate? existing = _certificates.SingleOrDefault(x => x.FileAssetId == file.FileAssetId);
            if (existing is null) _certificates.Add(PpapSupplierCertificate.Create(Id, file, utcNow));
            else existing.Retype(file.CertificateType);
        }
        UpdatedAtUtc = utcNow;
    }

    private static string Required(string value, int max, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Value is required.", name);
        string result = value.Trim();
        return result.Length <= max ? result : throw new ArgumentOutOfRangeException(name);
    }

    private static string? OneOf(string? value, IReadOnlyList<string> allowed, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return allowed.FirstOrDefault(x => string.Equals(x, value.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Value must be one of: {string.Join(", ", allowed)}.", name);
    }

    internal static string? Optional(string? value, int max, string name) => string.IsNullOrWhiteSpace(value) ? null : Required(value, max, name);
}

public sealed class PpapSupplierUser : BaseEntity<Guid>
{
    public Guid SupplierId { get; private set; }
    public Guid UserId { get; private set; }

    private PpapSupplierUser() { }

    internal static PpapSupplierUser Create(Guid supplierId, Guid userId) => new() { Id = Guid.CreateVersion7(), SupplierId = supplierId, UserId = userId };
}

public sealed class PpapSupplierCertificate : BaseEntity<Guid>
{
    public Guid SupplierId { get; private set; }
    public Guid FileAssetId { get; private set; }
    public string FileName { get; private set; } = default!;
    public string CertificateType { get; private set; } = default!;
    public DateTime AddedAtUtc { get; private set; }

    private PpapSupplierCertificate() { }

    internal static PpapSupplierCertificate Create(Guid supplierId, PpapSupplierCertificateInput input, DateTime utcNow) => new()
    {
        Id = Guid.CreateVersion7(),
        SupplierId = supplierId,
        FileAssetId = input.FileAssetId,
        FileName = PpapSupplier.Optional(input.FileName, 260, nameof(input.FileName)) ?? throw new ArgumentException("File name is required.", nameof(input)),
        CertificateType = NormalizeType(input.CertificateType),
        AddedAtUtc = utcNow,
    };

    internal void Retype(string certificateType) => CertificateType = NormalizeType(certificateType);

    private static string NormalizeType(string value) =>
        PpapSupplier.CertificateTypes.FirstOrDefault(x => string.Equals(x, value?.Trim(), StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException("Certificate type must be ISO 9001, IATF 16949, ISO 14001, or Other.", nameof(value));
}
