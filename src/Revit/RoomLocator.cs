using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace EgRoomTracker.Revit;

/// <summary>
/// Pièce du modèle principal qui contient un élément, du modèle principal ou d'un lien, <b>une seule pièce par élément</b> :
/// <list type="number">
/// <item>la pièce qui contient le point d'insertion (<c>Document.GetRoomAtPoint</c>), puis le centre de la boîte englobante, puis un
/// point bas de cette boîte (un élément posé au plafond a son origine au-dessus de la pièce) ;</item>
/// <item>à défaut, la pièce dont le volume commun avec le solide de l'élément est le plus grand (intersection booléenne avec
/// <c>Room.ClosedShell</c>).</item>
/// </list>
/// Pour un élément d'un lien, la boîte, le point et les solides sont ramenés dans le repère du modèle principal par la
/// transformation totale du lien (<c>RevitLinkInstance.GetTotalTransform</c>) avant toute comparaison.
/// </summary>
public sealed class RoomLocator(Document host)
{
    /// <summary>Volume commun minimal pour retenir une pièce (pieds cubes, seuil de TRC).</summary>
    private const double MinOverlapVolume = 1e-6;

    private readonly Lazy<IReadOnlyList<Room>> _rooms = new(() => LoadRooms(host));
    private readonly Dictionary<long, Solid?> _roomSolids = [];

    /// <summary>La pièce de l'élément, null s'il n'est dans aucune pièce.</summary>
    public Room? Locate(FamilyInstance instance, Transform toHost)
    {
        var box = WorldBox(instance, toHost);
        foreach (var point in CandidatePoints(instance, toHost, box))
        {
            if (RoomAt(point) is { } room)
            {
                return room;
            }
        }

        return box is null ? null : LargestOverlap(instance, toHost, box);
    }

    private Room? RoomAt(XYZ point)
    {
        try
        {
            return host.GetRoomAtPoint(point);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static List<XYZ> CandidatePoints(FamilyInstance instance, Transform toHost, BoundingBoxXYZ? box)
    {
        var points = new List<XYZ>(3);
        if (instance.Location is LocationPoint location)
        {
            points.Add(toHost.OfPoint(location.Point));
        }

        if (box is not null)
        {
            var center = (box.Min + box.Max) / 2;
            points.Add(center);
            points.Add(new XYZ(center.X, center.Y, box.Min.Z + (0.1 * (box.Max.Z - box.Min.Z))));
        }

        return points;
    }

    private Room? LargestOverlap(FamilyInstance instance, Transform toHost, BoundingBoxXYZ box)
    {
        var solids = ElementSolids.Collect(instance)
            .Select(solid => toHost.IsIdentity ? solid : SolidUtils.CreateTransformed(solid, toHost))
            .ToList();
        if (solids.Count == 0)
        {
            return null;
        }

        Room? best = null;
        var bestVolume = MinOverlapVolume;
        foreach (var room in _rooms.Value)
        {
            var roomBox = room.get_BoundingBox(null);
            if (roomBox is null || !Intersects(roomBox, box) || RoomSolid(room) is not { } roomSolid)
            {
                continue;
            }

            var volume = 0.0;
            foreach (var solid in solids)
            {
                try
                {
                    volume += BooleanOperationsUtils.ExecuteBooleanOperation(roomSolid, solid, BooleanOperationsType.Intersect)?.Volume ?? 0;
                }
                catch (Exception)
                {
                    // Intersection impossible pour ce couple de solides : ignoré.
                }
            }

            if (volume > bestVolume)
            {
                (best, bestVolume) = (room, volume);
            }
        }

        return best;
    }

    private Solid? RoomSolid(Room room)
    {
        var id = room.Id.Value;
        if (_roomSolids.TryGetValue(id, out var cached))
        {
            return cached;
        }

        Solid? solid = null;
        try
        {
            if (room.ClosedShell is { } shell)
            {
                solid = shell.OfType<Solid>().FirstOrDefault(s => s.Volume > 0);
            }
        }
        catch (Exception)
        {
            // Pièce sans enveloppe fermée.
        }

        _roomSolids[id] = solid;
        return solid;
    }

    /// <summary>Boîte englobante de l'élément dans le repère du modèle principal (les 8 coins transformés).</summary>
    private static BoundingBoxXYZ? WorldBox(Element element, Transform toHost)
    {
        var box = element.get_BoundingBox(null);
        if (box is null)
        {
            return null;
        }

        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
        for (var corner = 0; corner < 8; corner++)
        {
            var point = toHost.OfPoint(new XYZ(
                (corner & 1) == 0 ? box.Min.X : box.Max.X,
                (corner & 2) == 0 ? box.Min.Y : box.Max.Y,
                (corner & 4) == 0 ? box.Min.Z : box.Max.Z));
            (minX, maxX) = (Math.Min(minX, point.X), Math.Max(maxX, point.X));
            (minY, maxY) = (Math.Min(minY, point.Y), Math.Max(maxY, point.Y));
            (minZ, maxZ) = (Math.Min(minZ, point.Z), Math.Max(maxZ, point.Z));
        }

        return new BoundingBoxXYZ { Min = new XYZ(minX, minY, minZ), Max = new XYZ(maxX, maxY, maxZ) };
    }

    private static bool Intersects(BoundingBoxXYZ a, BoundingBoxXYZ b) =>
        a.Min.X <= b.Max.X && a.Max.X >= b.Min.X
        && a.Min.Y <= b.Max.Y && a.Max.Y >= b.Min.Y
        && a.Min.Z <= b.Max.Z && a.Max.Z >= b.Min.Z;

    private static IReadOnlyList<Room> LoadRooms(Document document) =>
        new FilteredElementCollector(document)
            .OfCategory(BuiltInCategory.OST_Rooms)
            .WhereElementIsNotElementType()
            .OfType<Room>()
            .Where(room => room.Location is not null && room.Area > 0)
            .ToList();
}
