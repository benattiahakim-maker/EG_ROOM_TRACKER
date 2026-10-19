using System.Text.Json.Serialization;
using EgRoomTracker.Contracts.Configuration;

namespace EgRoomTracker.Contracts.Results;

/// <summary>Bloc <c>summary</c> de <c>result.json</c>.</summary>
public sealed record ExtractionSummary
{
    public required string CorrelationId { get; init; }

    public required string ExtractorVersion { get; init; }

    public required RevitInfo Revit { get; init; }

    /// <summary>Identifiant du moteur APS lorsqu'il est disponible (ex. <c>Autodesk.Revit+2026</c>).</summary>
    public string? ApsEngine { get; init; }

    /// <summary>Null uniquement si le RVT n'a pas pu être résolu.</summary>
    public SourceInfo? Source { get; init; }

    public required DateTimeOffset StartedAtUtc { get; init; }

    public required DateTimeOffset CompletedAtUtc { get; init; }

    public required AppliedOptions Options { get; init; }

    /// <summary>
    /// Traçabilité du calcul des volumes (schéma 1.3, EGR-16) ; null si l'extraction a échoué avant la préparation des volumes.
    /// </summary>
    public VolumeComputationSummary? VolumeComputation { get; init; }

    public required RoomCounts Counts { get; init; }

    /// <summary>Bilan de l'export des vues 3D des pièces (schéma 1.4) ; absent sans <c>roomImages</c>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ImagesSummary? Images { get; init; }

    /// <summary>Bilan de la mesure des hauteurs (schéma 1.4) ; absent sans <c>roomHeights</c>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public HeightsSummary? Heights { get; init; }

    public required ExtractionStatus Status { get; init; }

    /// <summary>Erreurs globales ; non vide si et seulement si <see cref="Status"/> = <see cref="ExtractionStatus.Failed"/>.</summary>
    public IReadOnlyList<GlobalError> Errors { get; init; } = [];
}

/// <summary>Version (<c>2025</c>, <c>2026</c>, <c>2027</c>) et build Revit du moteur.</summary>
public sealed record RevitInfo(string Version, string? Build);

public sealed record SourceInfo(string FileName, string RelativePath, bool? IsWorkshared);

public sealed record AppliedOptions(
    IReadOnlyList<string>? PhaseIds,
    VolumeMode VolumeMode,
    IReadOnlyList<string> SharedParameterKeys,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RoomParameters = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? Elements = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? RoomImages = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? ImageWidthPx = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? RoomHeights = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? HeightStepM = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? LinkedElements = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? OutputFormat = null);

/// <summary>Export des vues 3D des pièces : pièces visées, images écrites, pièces sans image.</summary>
public sealed record ImagesSummary(int Requested, int Exported, int Failed);

/// <summary>
/// Calcul des volumes (schéma 1.3) : réglage <c>ComputeVolumes</c> du document à l'ouverture (null si non lu),
/// activation temporaire en mode <c>compute</c>, et annulation de la transaction temporaire (null sans transaction).
/// </summary>
public sealed record VolumeComputationSummary(bool? DocumentComputeVolumes, bool ComputeApplied, bool? TransactionRolledBack);

public sealed record RoomCounts(int RoomsCollected, int RoomsExported, int RoomsWithAnomalies);

/// <summary>Erreur globale (section 9.2) ; <see cref="Code"/> issu de <see cref="Codes.ErrorCodes"/>.</summary>
public sealed record GlobalError(string Code, string Stage, string Message);

/// <summary>Statut final (section 9.3).</summary>
public enum ExtractionStatus
{
    Succeeded,
    SucceededWithWarnings,
    Failed,
}
