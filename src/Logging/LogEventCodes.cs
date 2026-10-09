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
    public const string RoomExtractionFailed = "ROOM_EXTRACTION_FAILED";
    public const string DocumentCloseFailed = "DOCUMENT_CLOSE_FAILED";
    public const string RoomCollectionDetails = "ROOM_COLLECTION_DETAILS";

    /// <summary>Préparation des volumes (EGR-16) : mode, réglage du document, activation temporaire, annulation.</summary>
    public const string VolumeComputation = "VOLUME_COMPUTATION";

    /// <summary>volumeMode compute : activation temporaire du calcul impossible ; volumes non disponibles.</summary>
    public const string VolumeComputeFailed = "VOLUME_COMPUTE_FAILED";
}
