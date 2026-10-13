using System.Net;
using DES.Framework.Core.Exceptions;
using DES.Framework.Jobs.Services;
using DES.Modules.DocumentIntelligence.Data;
using DES.Modules.DocumentIntelligence.Domain;
using DES.Modules.DocumentIntelligence.Processing;
using DES.Modules.DocumentIntelligence.Providers;
using DES.Modules.Files.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DES.Modules.DocumentIntelligence.Features.v1.Attempts.RequestDocumentExtraction;

public interface IDocumentExtractionRequestService
{
    Task<Guid> RequestAsync(
        Guid ownerUserId,
        Guid submissionDocumentId,
        Guid sourceFileAssetId,
        string documentCode,
        CancellationToken cancellationToken = default);
}

internal sealed class DocumentExtractionRequestService(
    DocumentIntelligenceDbContext dbContext,
    IInternalFileArtifactService artifacts,
    IJobService jobService,
    IOptions<AspectPdfExtractionOptions> aspectOptions,
    IOptions<DiagramDrawingExtractionOptions> drawingOptions) : IDocumentExtractionRequestService
{
    public async Task<Guid> RequestAsync(
        Guid ownerUserId,
        Guid submissionDocumentId,
        Guid sourceFileAssetId,
        string documentCode,
        CancellationToken cancellationToken = default)
    {
        string tenantId = dbContext.TenantInfo?.Id
            ?? throw new UnauthorizedException("Tenant context is required.");
        bool isDrawing = string.Equals(documentCode.Trim(), "DWG", StringComparison.OrdinalIgnoreCase);
        AspectPdfExtractionOptions aspectSettings = aspectOptions.Value;
        DiagramDrawingExtractionOptions drawingSettings = drawingOptions.Value;
        bool enabled = isDrawing ? drawingSettings.Enabled : aspectSettings.Enabled;
        if (!enabled)
        {
            throw new CustomException(
                "Document extraction is unavailable.",
                (IEnumerable<string>?)null,
                HttpStatusCode.ServiceUnavailable);
        }

        long configuredMaxBytes = isDrawing ? drawingSettings.MaxSourceBytes : aspectSettings.MaxSourceBytes;
        long maxBytes = Math.Min(configuredMaxBytes, ExtractionRequestIdentity.AbsoluteMaxSourceBytes);
        string sourceSha256;
        await using (InternalFileArtifactContent source = await artifacts.OpenReadAsync(
            sourceFileAssetId,
            ownerUserId.ToString(),
            cancellationToken).ConfigureAwait(false))
        {
            if (source.SizeBytes > maxBytes)
            {
                throw new CustomException("The source document exceeds the extraction size limit.");
            }

            try
            {
                sourceSha256 = await ExtractionRequestIdentity.ComputeSha256Async(
                    source.Content,
                    maxBytes,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidDataException exception)
            {
                throw new CustomException("The source document exceeds the extraction size limit.", exception);
            }
        }

        string provider = (isDrawing ? drawingSettings.Provider : aspectSettings.Provider).Trim();
        string model = (isDrawing ? drawingSettings.Model : aspectSettings.Model)!.Trim();
        string extractorVersion = (isDrawing ? drawingSettings.ExtractorVersion : aspectSettings.ExtractorVersion).Trim();
        string idempotencyKey = ExtractionRequestIdentity.BuildIdempotencyKey(
            tenantId,
            submissionDocumentId,
            sourceSha256,
            provider,
            model,
            extractorVersion);

        var existing = await dbContext.ExtractionAttempts
            .Where(x => x.IdempotencyKey == idempotencyKey && x.Status != ExtractionAttemptStatus.DeadLettered)
            .Select(x => new { x.Id, x.Status })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            if (existing.Status == ExtractionAttemptStatus.Pending)
            {
                jobService.Enqueue<ExtractionProcessingJob>(
                    job => job.RunAsync(tenantId, existing.Id, CancellationToken.None));
            }
            return existing.Id;
        }

        ExtractionAttempt attempt = ExtractionAttempt.Create(
            tenantId,
            ownerUserId,
            submissionDocumentId,
            sourceFileAssetId,
            documentCode,
            sourceSha256,
            idempotencyKey,
            provider,
            model,
            extractorVersion);
        dbContext.ExtractionAttempts.Add(attempt);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        jobService.Enqueue<ExtractionProcessingJob>(
            job => job.RunAsync(tenantId, attempt.Id, CancellationToken.None));
        return attempt.Id;
    }
}
