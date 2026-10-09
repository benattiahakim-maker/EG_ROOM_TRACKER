using Autodesk.Revit.DB;
using EgRoomTracker.Application.Revit;

namespace EgRoomTracker.Revit;

/// <summary>Collecte des Room du document principal. Squelette ; implémentation et filtres de phase en EGR-07 (#7).</summary>
public sealed class RoomCollectionService(RevitExecutionContext context) : IRoomCollectionService
{
    /// <summary>Catégorie collectée : pièces uniquement (pas d'espaces MEP, surfaces ni étiquettes).</summary>
    internal const BuiltInCategory RoomCategory = BuiltInCategory.OST_Rooms;

    public IReadOnlyList<CollectedRoom> CollectRooms(IReadOnlyList<string>? phaseIds)
    {
        _ = context;
        throw new NotImplementedException("EGR-07 : collecte de toutes les pièces.");
    }
}
