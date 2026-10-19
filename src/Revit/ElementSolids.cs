using Autodesk.Revit.DB;

namespace EgRoomTracker.Revit;

/// <summary>Solides d'une instance en coordonnées de son document, familles imbriquées comprises (détail moyen, sans références).</summary>
internal static class ElementSolids
{
    public static List<Solid> Collect(Element element)
    {
        var solids = new List<Solid>();
        var geometry = element.get_Geometry(new Options
        {
            DetailLevel = ViewDetailLevel.Medium,
            ComputeReferences = false,
            IncludeNonVisibleObjects = false,
        });
        if (geometry is not null)
        {
            Walk(geometry, solids);
        }

        return solids;
    }

    private static void Walk(GeometryElement element, List<Solid> solids)
    {
        foreach (var geometryObject in element)
        {
            switch (geometryObject)
            {
                case Solid { Volume: > 0 } solid:
                    solids.Add(solid);
                    break;
                case GeometryInstance instance:
                    Walk(instance.GetInstanceGeometry(), solids);
                    break;
            }
        }
    }
}
