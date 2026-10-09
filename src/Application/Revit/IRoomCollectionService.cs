namespace EgRoomTracker.Application.Revit;

/// <summary>
/// Collecte des Room (<c>OST_Rooms</c>) du document principal, sans filtre de vue active,
/// filtres explicites de phase. Retourne l'identité de chaque pièce.
/// </summary>
public interface IRoomCollectionService
{
    IReadOnlyList<CollectedRoom> CollectRooms(IReadOnlyList<string>? phaseIds);
}

/// <summary>
/// Identité d'une pièce collectée : <c>ElementId.Value</c> et <c>Element.UniqueId</c>. Permet d'exporter
/// l'identité d'une pièce même si la lecture de ses paramètres échoue (EXTRACTION_ERROR).
/// </summary>
public sealed record CollectedRoom(long ElementId, string UniqueId);
