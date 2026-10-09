using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using EgRoomTracker.Application.Revit;
using EgRoomTracker.Contracts.Codes;
using EgRoomTracker.Logging;

namespace EgRoomTracker.Revit;

/// <summary>
/// Collecte des pièces du document principal (EGR-07) : instances <see cref="Room"/> de catégorie
/// <c>OST_Rooms</c>, collecteur sur le document entier (aucun filtre de vue active), toutes phases par défaut (phase inconnue : CONFIG_INVALID),
/// variantes et pièces non placées incluses. Espaces MEP, surfaces et étiquettes exclus (autres catégories/types).
/// Les pièces des documents liés ne sont pas parcourues.
/// </summary>
public sealed class RoomCollectionService(RevitExecutionContext context, ILogService log) : IRoomCollectionService
{
    /// <summary>Catégorie collectée : pièces uniquement (pas d'espaces MEP, surfaces ni étiquettes).</summary>
    internal const BuiltInCategory RoomCategory = BuiltInCategory.OST_Rooms;

    public IReadOnlyList<CollectedRoom> CollectRooms(IReadOnlyList<string>? phaseIds)
    {
        var document = context.RequireDocument();
        var phases = ResolvePhases(document, PhaseSelection.Parse(phaseIds));

        var rooms = new List<CollectedRoom>();
        int unplaced = 0, inDesignOption = 0, excludedByPhase = 0;
        using (var collector = new FilteredElementCollector(document).OfCategory(RoomCategory).WhereElementIsNotElementType())
        {
            foreach (var element in collector)
            {
                if (element is not Room room)
                {
                    continue;
                }

                if (phases is not null && !phases.Contains(RoomPhaseId(room)))
                {
                    excludedByPhase++;
                    continue;
                }

                if (room.Location is null)
                {
                    unplaced++;
                }

                if (room.DesignOption is not null)
                {
                    inDesignOption++;
                }

                rooms.Add(new CollectedRoom(room.Id.Value, room.UniqueId));
            }
        }

        rooms.Sort((a, b) => a.ElementId.CompareTo(b.ElementId));
        log.Log(new LogEvent(LogLevel.Info, LogEventCodes.RoomCollectionDetails, Stages.Collection, $"{rooms.Count} pièce(s) collectée(s) dans le document principal.")
        {
            Data = new Dictionary<string, object?>
            {
                ["rooms"] = rooms.Count,
                ["unplaced"] = unplaced,
                ["inDesignOption"] = inDesignOption,
                ["excludedByPhase"] = excludedByPhase,
                ["phaseFilter"] = phases is null ? "all" : phases.Order().Select(p => p.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray(),
            },
        });
        return rooms;
    }

    /// <summary>Phase de la pièce (<c>ROOM_PHASE_ID</c>) ; <c>-1</c> si absente.</summary>
    private static long RoomPhaseId(Room room) =>
        room.get_Parameter(BuiltInParameter.ROOM_PHASE_ID)?.AsElementId()?.Value ?? ElementId.InvalidElementId.Value;

    /// <summary>Vérifie que les phases demandées existent dans le document (CONFIG_INVALID sinon).</summary>
    private static IReadOnlySet<long>? ResolvePhases(Document document, IReadOnlySet<long>? requested)
    {
        if (requested is null)
        {
            return null;
        }

        var known = new HashSet<long>();
        foreach (var phase in document.Phases.Cast<Phase>())
        {
            known.Add(phase.Id.Value);
        }

        // Phase inconnue : échec global CONFIG_INVALID (étape collection), jamais un résultat vide « réussi ».
        PhaseSelection.EnsureKnown(requested, known);
        return requested;
    }
}
