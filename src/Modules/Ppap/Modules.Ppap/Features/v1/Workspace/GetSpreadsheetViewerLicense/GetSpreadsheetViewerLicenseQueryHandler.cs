using System.Net;
using DES.Framework.Core.Exceptions;
using DES.Modules.Ppap.Contracts.v1.Workspace;
using Mediator;
using Microsoft.Extensions.Options;

namespace DES.Modules.Ppap.Features.v1.Workspace.GetSpreadsheetViewerLicense;

public sealed class GetSpreadsheetViewerLicenseQueryHandler(
    IOptions<OmnexSpreadViewerOptions> options)
    : IQueryHandler<GetSpreadsheetViewerLicenseQuery, SpreadsheetViewerLicenseDto>
{
    public ValueTask<SpreadsheetViewerLicenseDto> Handle(
        GetSpreadsheetViewerLicenseQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        OmnexSpreadViewerOptions value = options.Value;
        if (!value.Enabled || string.IsNullOrWhiteSpace(value.LicenseKey))
        {
            throw new CustomException(
                "The spreadsheet viewer is not configured.",
                (IEnumerable<string>?)null,
                HttpStatusCode.ServiceUnavailable);
        }
        if (value.ExpiresAt is { } expiresAt && expiresAt <= DateTimeOffset.UtcNow)
        {
            throw new CustomException(
                "The spreadsheet viewer license has expired.",
                (IEnumerable<string>?)null,
                HttpStatusCode.ServiceUnavailable);
        }
        return ValueTask.FromResult(new SpreadsheetViewerLicenseDto(value.LicenseKey, value.ExpiresAt));
    }
}
