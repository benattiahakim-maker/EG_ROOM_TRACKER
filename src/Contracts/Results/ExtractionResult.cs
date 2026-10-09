using EgRoomTracker.Contracts.Codes;

namespace EgRoomTracker.Contracts.Results;

/// <summary>
/// Racine de <c>result.json</c>, unique contrat de résultat (docs/schema/result.schema.json).
/// Aucun objet Revit : la sérialisation est indépendante de la lecture.
/// </summary>
public sealed record ExtractionResult
{
    public string SchemaVersion { get; init; } = SchemaVersions.Result;

    public required ExtractionSummary Summary { get; init; }

    /// <summary>Pièces triées par elementId numérique croissant ; vide si statut <c>failed</c>.</summary>
    public IReadOnlyList<RoomDto> Rooms { get; init; } = [];

    public IReadOnlyList<AnomalyDto> Anomalies { get; init; } = [];
}
