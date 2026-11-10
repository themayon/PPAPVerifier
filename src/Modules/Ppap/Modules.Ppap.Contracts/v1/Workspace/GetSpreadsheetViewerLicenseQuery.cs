using Mediator;

namespace DES.Modules.Ppap.Contracts.v1.Workspace;

public sealed record SpreadsheetViewerLicenseDto(string LicenseKey, DateTimeOffset? ExpiresAt);

public sealed record GetSpreadsheetViewerLicenseQuery : IQuery<SpreadsheetViewerLicenseDto>;
