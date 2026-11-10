namespace DES.Modules.Ppap.Domain;

public enum DocumentProcessingStatus
{
    NotQueued = 0,
    Queued = 1,
    Converting = 2,
    Extracting = 3,
    Validating = 4,
    Ready = 5,
    Failed = 6,
    Superseded = 7
}
