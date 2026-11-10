using DES.Framework.Shared.Persistence;
using Mediator;

namespace DES.Modules.Ppap.Contracts.v1.Parts;

public sealed record PpapPartDto(
    Guid Id,
    string PartName,
    string PartNumber,
    string DrawingRevision,
    Guid? RuleTemplateId,
    Guid? RuleTemplateVersionId,
    bool IsActive,
    int Version,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    Guid? SupplierId = null,
    int PpapLevel = 3,
    string Classification = "Standard",
    bool CanEdit = true,
    PpapPartSupplierSummaryDto? Supplier = null,
    PpapPartSubmissionSummaryDto? LatestSubmission = null);

public sealed record PpapPartSupplierSummaryDto(Guid Id, string Code, string Name, string? City, string? Country, bool IsActive);

public sealed record PpapPartSubmissionSummaryDto(
    Guid Id,
    string Status,
    string Decision,
    int PpapLevel,
    int TotalElements,
    int CompletedElements,
    Guid? LatestReportId,
    int OpenFindingCount,
    bool IsOwner,
    int Version,
    DateTime CreatedAtUtc,
    DateTime? SubmittedAtUtc,
    DateTime? DecidedAtUtc);

public sealed record PpapPartsSummaryDto(
    long TotalParts,
    long ActiveSubmissions,
    long PendingReview,
    long ApprovalsThisMonth,
    long ApprovalsLastMonth,
    long NewSubmissionsThisMonth,
    long NewSubmissionsLastMonth);

public sealed record SearchMyPpapPartsQuery : IQuery<PagedResponse<PpapPartDto>>, IPagedQuery
{
    public string? Search { get; init; }
    public Guid? SupplierId { get; init; }
    public string? Status { get; init; }
    public int? PpapLevel { get; init; }
    public string? Classification { get; init; }
    public int? PageNumber { get; set; } = 1;
    public int? PageSize { get; set; } = 20;
    public string? Sort { get; set; }
}

public sealed record GetPpapPartsSummaryQuery : IQuery<PpapPartsSummaryDto>;
public sealed record GetPartQuery(Guid PartId) : IQuery<PpapPartDto>;
public sealed record CreatePartCommand(string PartName, string PartNumber, string DrawingRevision, Guid? SupplierId = null, Guid? RuleTemplateId = null, int PpapLevel = 3, string? Classification = null) : ICommand<PpapPartDto>;
public sealed record UpdatePartCommand(Guid PartId, string PartName, string PartNumber, string DrawingRevision, int ExpectedVersion, Guid? SupplierId = null, int? PpapLevel = null, string? Classification = null) : ICommand<PpapPartDto>;
public sealed record AssignPartTemplateCommand(Guid PartId, Guid RuleTemplateId, int ExpectedVersion) : ICommand<PpapPartDto>;
public sealed record CreateDraftFromPartCommand(Guid PartId, int PpapLevel) : ICommand<Guid>;

public sealed record PpapSupplierCertificateDto(Guid FileAssetId, string FileName, string CertificateType, DateTime AddedAtUtc);
public sealed record PpapSupplierCertificateItem(Guid FileAssetId, string FileName, string CertificateType);

public sealed record PpapSupplierDto(
    Guid Id,
    string Code,
    string Name,
    string? ContactName,
    string? Email,
    string? Phone,
    string? Address,
    bool IsActive,
    IReadOnlyList<Guid> UserIds,
    int PartCount,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    int Version,
    string? TaxId = null,
    string? DunsNumber = null,
    string? City = null,
    string? Country = null,
    string? SiteName = null,
    string? SiteAddress = null,
    bool SiteSameAsHeadquarters = false,
    string? ContactJobTitle = null,
    IReadOnlyList<PpapSupplierCertificateDto>? Certificates = null,
    string? Region = null,
    string? TierLevel = null,
    string? QualityGrade = null,
    double? QualityScore = null,
    int ActivePpapCount = 0);

public sealed record PpapSuppliersSummaryDto(
    long TotalSuppliers,
    long ActiveSuppliers,
    long NewThisMonth,
    long NewLastMonth,
    double? AverageQualityScore,
    long ActivePpaps,
    int RegionCount,
    long CriticalIssues);

public sealed record GetPpapSuppliersSummaryQuery : IQuery<PpapSuppliersSummaryDto>;

public sealed record PpapSupplierProfileInput(
    string Name,
    string? TaxId,
    string? DunsNumber,
    string? Address,
    string? City,
    string? Country,
    string? SiteName,
    string? SiteAddress,
    bool SiteSameAsHeadquarters,
    string? ContactName,
    string? ContactJobTitle,
    string? Email,
    string? Phone,
    string? TierLevel = null,
    string? QualityGrade = null);

public sealed record SearchPpapSuppliersQuery : IQuery<PagedResponse<PpapSupplierDto>>, IPagedQuery
{
    public string? Search { get; init; }
    public bool? IsActive { get; init; }
    public string? Region { get; init; }
    public string? TierLevel { get; init; }
    public string? QualityGrade { get; init; }
    public int? PageNumber { get; set; } = 1;
    public int? PageSize { get; set; } = 20;
    public string? Sort { get; set; }
}

public sealed record GetPpapSupplierQuery(Guid SupplierId) : IQuery<PpapSupplierDto>;
public sealed record CreatePpapSupplierCommand(string? Code, PpapSupplierProfileInput Profile, IReadOnlyList<Guid>? UserIds, IReadOnlyList<PpapSupplierCertificateItem>? Certificates) : ICommand<PpapSupplierDto>;
public sealed record UpdatePpapSupplierCommand(Guid SupplierId, int ExpectedVersion, string Code, PpapSupplierProfileInput Profile, bool IsActive, IReadOnlyList<Guid>? UserIds, IReadOnlyList<PpapSupplierCertificateItem>? Certificates) : ICommand<PpapSupplierDto>;
