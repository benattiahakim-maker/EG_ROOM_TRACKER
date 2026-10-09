namespace EgRoomTracker.Contracts.Codes;

/// <summary>Indicateurs qualité de portée pièce (section 9.1). Aucun ne provoque d'échec global.</summary>
public static class QualityFlagCodes
{
    public const string Unplaced = "UNPLACED";
    public const string ZeroArea = "ZERO_AREA";
    public const string DuplicateNumber = "DUPLICATE_NUMBER";
    public const string MissingParameter = "MISSING_PARAMETER";
    public const string LevelNotFound = "LEVEL_NOT_FOUND";
    public const string ExtractionError = "EXTRACTION_ERROR";

    public static IReadOnlyList<string> All { get; } =
        [Unplaced, ZeroArea, DuplicateNumber, MissingParameter, LevelNotFound, ExtractionError];
}

/// <summary>Anomalies de portée document (section 9.1), présentes uniquement dans <c>anomalies</c>.</summary>
public static class DocumentAnomalyCodes
{
    public const string VolumeNotComputed = "VOLUME_NOT_COMPUTED";
    public const string LinkMissing = "LINK_MISSING";
    public const string RevitWarningOnOpen = "REVIT_WARNING_ON_OPEN";
    public const string SharedParameterNotFound = "SHARED_PARAMETER_NOT_FOUND";

    public static IReadOnlyList<string> All { get; } =
        [VolumeNotComputed, LinkMissing, RevitWarningOnOpen, SharedParameterNotFound];
}

/// <summary>Erreurs globales (section 9.2) : statut <c>failed</c>.</summary>
public static class ErrorCodes
{
    public const string ConfigInvalid = "CONFIG_INVALID";
    public const string ConfigSchemaUnsupported = "CONFIG_SCHEMA_UNSUPPORTED";
    public const string PathOutsideInput = "PATH_OUTSIDE_INPUT";
    public const string RvtNotFound = "RVT_NOT_FOUND";
    public const string RvtAmbiguous = "RVT_AMBIGUOUS";
    public const string RevitVersionIncompatible = "REVIT_VERSION_INCOMPATIBLE";
    public const string DocumentOpenFailed = "DOCUMENT_OPEN_FAILED";
    public const string DocumentCorrupted = "DOCUMENT_CORRUPTED";
    public const string ExportFailed = "EXPORT_FAILED";
    public const string UnexpectedError = "UNEXPECTED_ERROR";

    public static IReadOnlyList<string> All { get; } =
    [
        ConfigInvalid, ConfigSchemaUnsupported, PathOutsideInput, RvtNotFound, RvtAmbiguous,
        RevitVersionIncompatible, DocumentOpenFailed, DocumentCorrupted, ExportFailed, UnexpectedError,
    ];
}

/// <summary>Étapes de traitement, utilisées dans <c>summary.errors[].stage</c> et dans les logs (champ <c>stage</c>).</summary>
public static class Stages
{
    public const string Startup = "startup";
    public const string Configuration = "configuration";
    public const string Input = "input";
    public const string Document = "document";
    public const string Collection = "collection";
    public const string Quality = "quality";
    public const string Export = "export";
    public const string Completion = "completion";
    public const string Logging = "logging";
}

public static class SchemaVersions
{
    /// <summary>Version du contrat <c>result.json</c>.</summary>
    public const string Result = "1.0";

    /// <summary>Version majeure de <c>settings.json</c> prise en charge.</summary>
    public const int SettingsMajor = 1;
}
