namespace DES.Modules.Ppap.Domain;

public enum PpapJobStatus
{
    Queued = 0,
    Processing = 1,
    Completed = 2,
    Failed = 3,
    Cancelled = 4
}
