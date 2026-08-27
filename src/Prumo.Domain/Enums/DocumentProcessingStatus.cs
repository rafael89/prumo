namespace Prumo.Domain.Enums;

public enum DocumentProcessingStatus
{
    Discovered,
    Downloaded,
    WaitingProcessing,
    Processing,
    Processed,
    Error,
    Ignored
}
