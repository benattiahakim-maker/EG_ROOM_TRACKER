namespace EgRoomTracker.Application.Revit;

/// <summary>
/// Passage libre d'une porte déduit des croisements d'une ligne de coupe avec les faces de sa géométrie, sans Revit ni
/// paramètres (calcul testé hors Revit ; la collecte des croisements est dans <c>DoorGeometry</c>).
/// <para>
/// Une ligne horizontale traverse, de gauche à droite : la face extérieure du dormant, sa face intérieure (le bord du
/// passage), les faces du ou des vantaux, puis la face intérieure et la face extérieure du dormant de droite. Le passage
/// libre est donc la distance entre la <b>2<sup>e</sup></b> et l'<b>avant-dernière</b> face, quel que soit le nombre de
/// vantaux. Une ligne verticale traverse : le bas du vantail, son haut, la sous-face du linteau, le dessus du linteau ;
/// la hauteur libre va du <b>bas</b> à l'<b>avant-dernière</b> face.
/// </para>
/// Sans dormant (moins de 4 croisements), le passage est l'écart entre la première et la dernière face. Valeurs en mètres.
/// </summary>
public static class OpeningMeasure
{
    /// <summary>Écart en dessous duquel deux croisements sont une même face (faces confondues, tolérance de calcul).</summary>
    public const double SameFaceToleranceM = 0.002;

    public const double MinWidthM = 0.30;
    public const double MaxWidthM = 6.00;
    public const double MinHeightM = 0.50;
    public const double MaxHeightM = 5.00;

    /// <summary>Croisements triés, ceux distants de moins de <see cref="SameFaceToleranceM"/> fusionnés.</summary>
    public static IReadOnlyList<double> Distinct(IEnumerable<double> crossings)
    {
        var result = new List<double>();
        foreach (var value in crossings.Where(double.IsFinite).Order())
        {
            if (result.Count == 0 || value - result[^1] > SameFaceToleranceM)
            {
                result.Add(value);
            }
        }

        return result;
    }

    /// <summary>Largeur de passage libre le long d'une ligne horizontale ; null s'il y a moins de 2 faces ou une valeur invraisemblable.</summary>
    public static double? ClearWidth(IEnumerable<double> crossings)
    {
        var c = Distinct(crossings);
        if (c.Count < 2)
        {
            return null;
        }

        var width = c.Count >= 4 ? c[^2] - c[1] : c[^1] - c[0];
        return width is >= MinWidthM and <= MaxWidthM ? width : null;
    }

    /// <summary>Hauteur libre le long d'une ligne verticale ; null s'il y a moins de 2 faces ou une valeur invraisemblable.</summary>
    public static double? ClearHeight(IEnumerable<double> crossings)
    {
        var c = Distinct(crossings);
        if (c.Count < 2)
        {
            return null;
        }

        var height = c.Count >= 4 ? c[^2] - c[0] : c[^1] - c[0];
        return height is >= MinHeightM and <= MaxHeightM ? height : null;
    }

    /// <summary>Médiane des mesures valides de plusieurs lignes de coupe ; null si aucune.</summary>
    public static double? Median(IEnumerable<double?> measures)
    {
        var values = measures.Where(m => m.HasValue).Select(m => m!.Value).Order().ToList();
        if (values.Count == 0)
        {
            return null;
        }

        var middle = values.Count / 2;
        return values.Count % 2 == 1 ? values[middle] : (values[middle - 1] + values[middle]) / 2;
    }
}
