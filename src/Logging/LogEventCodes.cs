namespace EgRoomTracker.Logging;

/// <summary>Codes des événements minimaux (section 5).</summary>
public static class LogEventCodes
{
    public const string ExtractionStarted = "EXTRACTION_STARTED";
    public const string ConfigurationValidated = "CONFIGURATION_VALIDATED";
    public const string DocumentAccessible = "DOCUMENT_ACCESSIBLE";
    public const string CollectionCompleted = "COLLECTION_COMPLETED";
    public const string AnomaliesDetected = "ANOMALIES_DETECTED";
    public const string ExportCompleted = "EXPORT_COMPLETED";
    public const string ExtractionFailed = "EXTRACTION_FAILED";
    public const string ExtractionSummary = "EXTRACTION_SUMMARY";
}
