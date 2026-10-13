namespace DES.Modules.Ppap.Processing;

public interface IPpapProcessingProvider
{
    Task<PpapProcessingResult> ProcessAsync(
        PpapProcessingRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record PpapProcessingRequest(
    string TenantId,
    Guid JobId,
    Guid SubmissionId,
    int SubmissionVersion,
    Guid OwnerUserId);

public sealed record PpapProcessingResult(
    bool Succeeded,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static PpapProcessingResult Success() => new(true, null, null);

    public static PpapProcessingResult Failure(string errorCode, string errorMessage) =>
        new(false, errorCode, errorMessage);
}
