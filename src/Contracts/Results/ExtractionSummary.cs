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

    public required RoomCounts Counts { get; init; }

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
    IReadOnlyList<string> SharedParameterKeys);

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
