using Autodesk.Revit.DB;
using EgRoomTracker.Application.Revit;
using EgRoomTracker.Application.Units;

namespace EgRoomTracker.Revit;

/// <summary>
/// Passage libre d'une porte (largeur et hauteur en mètres) mesuré sur sa géométrie, sans lire de paramètre de famille.
/// Les solides de l'instance (dormant, vantaux, y compris familles imbriquées) sont recoupés par des lignes de coupe
/// tracées dans les axes de la porte : trois lignes horizontales à mi-hauteur, à trois profondeurs du mur, pour la
/// largeur ; six lignes verticales pour la hauteur. Chaque ligne donne une mesure par <see cref="OpeningMeasure"/>, la
/// valeur retenue est la médiane (robuste à une ligne qui frôle un détail). Les axes viennent de
/// <c>HandOrientation</c> et <c>FacingOrientation</c> (à défaut de la transformation de l'instance), donc la mesure
/// ne dépend ni de la ligne d'axe du mur, ni d'un mur droit, ni du type d'hôte.
/// </summary>
internal static class DoorGeometry
{
    private static readonly double[] DepthFractions = [0.25, 0.5, 0.75];
    private static readonly double[] WidthFractions = [0.35, 0.65];

    public sealed record Opening(double? WidthM, double? HeightM);

    /// <summary>Passage libre ; null si la géométrie est absente ou inexploitable (la mesure ne lève jamais).</summary>
    public static Opening? Measure(FamilyInstance door, IUnitConversionService units)
    {
        try
        {
            return MeasureCore(door, units);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static Opening? MeasureCore(FamilyInstance door, IUnitConversionService units)
    {
        var solids = ElementSolids.Collect(door);
        if (solids.Count == 0)
        {
            return null;
        }

        var transform = door.GetTransform();
        var hand = Horizontal(door.HandOrientation) ?? Horizontal(transform.BasisX);
        var facingRaw = Horizontal(door.FacingOrientation) ?? Horizontal(transform.BasisY);
        if (hand is null || facingRaw is null)
        {
            return null;
        }

        // Base orthonormée (main, profondeur, verticale) pour passer des coordonnées monde aux abscisses de coupe.
        var facing = Horizontal(facingRaw - (hand * facingRaw.DotProduct(hand)));
        if (facing is null)
        {
            return null;
        }

        var (tMin, tMax, fMin, fMax, zMin, zMax) = Extents(solids, hand, facing);
        if (!(tMax > tMin) || !(zMax > zMin))
        {
            return null;
        }

        var pad = UnitUtils.ConvertToInternalUnits(0.3, UnitTypeId.Meters);
        var sectionZ = zMin + Math.Min(UnitUtils.ConvertToInternalUnits(1.0, UnitTypeId.Meters), 0.5 * (zMax - zMin));

        var widths = new List<double?>();
        foreach (var depth in DepthFractions)
        {
            var f = fMin + (depth * (fMax - fMin));
            var line = Line.CreateBound(At(hand, facing, tMin - pad, f, sectionZ), At(hand, facing, tMax + pad, f, sectionZ));
            widths.Add(OpeningMeasure.ClearWidth(Crossings(solids, line).Select(p => units.ToMeters(p.DotProduct(hand)))));
        }

        var heights = new List<double?>();
        foreach (var across in WidthFractions)
        {
            var t = tMin + (across * (tMax - tMin));
            foreach (var depth in DepthFractions)
            {
                var f = fMin + (depth * (fMax - fMin));
                var line = Line.CreateBound(At(hand, facing, t, f, zMin - pad), At(hand, facing, t, f, zMax + pad));
                heights.Add(OpeningMeasure.ClearHeight(Crossings(solids, line).Select(p => units.ToMeters(p.Z))));
            }
        }

        var width = OpeningMeasure.Median(widths);
        var height = OpeningMeasure.Median(heights);
        return width is null && height is null ? null : new Opening(width, height);
    }

    private static XYZ At(XYZ hand, XYZ facing, double t, double f, double z) =>
        (hand * t) + (facing * f) + new XYZ(0, 0, z);

    /// <summary>Points d'intersection de la ligne avec toutes les faces des solides.</summary>
    private static IEnumerable<XYZ> Crossings(IEnumerable<Solid> solids, Line line)
    {
        foreach (var solid in solids)
        {
            foreach (Face face in solid.Faces)
            {
                if (face.Intersect(line, out var results) != SetComparisonResult.Overlap || results is null)
                {
                    continue;
                }

                foreach (IntersectionResult result in results)
                {
                    yield return result.XYZPoint;
                }
            }
        }
    }

    /// <summary>Étendue des solides sur la main, la profondeur et la verticale (extrémités des arêtes).</summary>
    private static (double TMin, double TMax, double FMin, double FMax, double ZMin, double ZMax) Extents(
        IEnumerable<Solid> solids, XYZ hand, XYZ facing)
    {
        double tMin = double.MaxValue, tMax = double.MinValue, fMin = double.MaxValue, fMax = double.MinValue;
        double zMin = double.MaxValue, zMax = double.MinValue;
        foreach (var solid in solids)
        {
            foreach (Edge edge in solid.Edges)
            {
                var curve = edge.AsCurve();
                foreach (var point in new[] { curve.GetEndPoint(0), curve.GetEndPoint(1) })
                {
                    var t = point.DotProduct(hand);
                    var f = point.DotProduct(facing);
                    tMin = Math.Min(tMin, t);
                    tMax = Math.Max(tMax, t);
                    fMin = Math.Min(fMin, f);
                    fMax = Math.Max(fMax, f);
                    zMin = Math.Min(zMin, point.Z);
                    zMax = Math.Max(zMax, point.Z);
                }
            }
        }

        return (tMin, tMax, fMin, fMax, zMin, zMax);
    }

    /// <summary>Projection horizontale normée ; null si le vecteur est (presque) vertical ou nul.</summary>
    private static XYZ? Horizontal(XYZ? vector)
    {
        if (vector is null)
        {
            return null;
        }

        var flat = new XYZ(vector.X, vector.Y, 0);
        return flat.GetLength() < 1e-6 ? null : flat.Normalize();
    }
}
