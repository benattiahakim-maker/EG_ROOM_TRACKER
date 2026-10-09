namespace EgRoomTracker.Application.Revit;

/// <summary>
/// Collecte des Room (<c>OST_Rooms</c>) du document principal, sans filtre de vue active,
/// filtres explicites de phase. Retourne les <c>ElementId.Value</c>.
/// </summary>
public interface IRoomCollectionService
{
    IReadOnlyList<long> CollectRoomIds(IReadOnlyList<string>? phaseIds);
}
