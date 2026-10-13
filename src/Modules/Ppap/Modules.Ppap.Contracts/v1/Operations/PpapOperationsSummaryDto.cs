namespace DES.Modules.Ppap.Contracts.v1.Operations;

public sealed record PpapOperationsSummaryDto(
    long TotalSubmissions,
    long QueuedJobs,
    long ProcessingJobs,
    long CompletedJobs,
    long FailedJobs,
    long PendingTopupRequests,
    long TotalAvailableCredits,
    long TotalReservedCredits);
