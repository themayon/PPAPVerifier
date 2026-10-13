namespace DES.Modules.DocumentIntelligence.Domain;

public enum ExtractionAttemptStatus
{
    Pending,
    Claimed,
    Submitted,
    Processing,
    Succeeded,
    InvalidDocument,
    Failed,
    TimedOut,
    DeadLettered
}
