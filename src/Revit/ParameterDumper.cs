using System.Globalization;
using Autodesk.Revit.DB;
using EgRoomTracker.Application.Revit;
using EgRoomTracker.Application.Units;

namespace EgRoomTracker.Revit;

/// <summary>
/// Liste tous les paramètres d'un élément <b>à plat</b> <c>{ nom : valeur }</c> (<c>roomParameters: all</c>, schéma 1.4), comme
/// <c>areaM2</c> ou <c>volumeM3</c> : un paramètre, une propriété, donc une colonne dans le tableau de résultats. Tous les paramètres de
/// l'élément (<see cref="Element.Parameters"/>), y compris ceux masqués dans la palette Propriétés ; appelé sur une instance pour ses
/// paramètres d'occurrence, sur son <see cref="ElementType"/> pour ses paramètres de type.
/// <list type="bullet">
/// <item>valeur typée selon le stockage : texte, entier, ElementId en chaîne, double converti en SI pour longueur, surface, volume et
/// nombre sans unité (comme les paramètres partagés, EGR-10) ;</item>
/// <item>autre grandeur (angle, pourcentage…) : la valeur affichée par Revit (<c>AsValueString</c>), faute de conversion fiable ;</item>
/// <item>paramètre sans valeur : <c>null</c> ; paramètre illisible : omis ;</item>
/// <item>triés par nom (sortie déterministe) ; un nom en double est suffixé « (2) », « (3) ».</item>
/// </list>
/// </summary>
internal static class ParameterDumper
{
    public static IReadOnlyDictionary<string, object?> Dump(Element element, IUnitConversionService units)
    {
        var values = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        foreach (Parameter parameter in element.Parameters)
        {
            try
            {
                var (name, value) = Read(parameter, units);
                values[UniqueName(values, name)] = value;
            }
            catch (Exception)
            {
                // Paramètre illisible : omis, les autres restent exportés.
            }
        }

        return values;
    }

    private static (string Name, object? Value) Read(Parameter parameter, IUnitConversionService units)
    {
        var name = parameter.Definition?.Name ?? string.Empty;
        if (!parameter.HasValue)
        {
            return (name, null);
        }

        object? typed = parameter.StorageType switch
        {
            StorageType.String => parameter.AsString(),
            StorageType.Integer => (long)parameter.AsInteger(),
            StorageType.Double => DoubleValue(parameter, units),
            StorageType.ElementId => parameter.AsElementId()?.Value.ToString(CultureInfo.InvariantCulture),
            _ => null,
        };

        // Grandeur non convertie (angle, pourcentage…) : la valeur affichée par Revit.
        return (name, typed ?? DisplayedValue(parameter));
    }

    private static object? DoubleValue(Parameter parameter, IUnitConversionService units)
    {
        var read = SharedParameterValues.FromDouble(parameter.AsDouble(), RoomParameterReader.MeasureOf(parameter.Definition), units);
        return read.State == SharedParameterState.HasValue ? read.Value : null;
    }

    private static string? DisplayedValue(Parameter parameter)
    {
        try
        {
            return parameter.AsValueString();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Nom sans doublon : « (sans nom) » si vide, puis « nom (2) », « nom (3) »…</summary>
    private static string UniqueName(SortedDictionary<string, object?> values, string name)
    {
        var key = string.IsNullOrEmpty(name) ? "(sans nom)" : name;
        if (!values.ContainsKey(key))
        {
            return key;
        }

        for (var index = 2; ; index++)
        {
            var candidate = $"{key} ({index.ToString(CultureInfo.InvariantCulture)})";
            if (!values.ContainsKey(candidate))
            {
                return candidate;
            }
        }
    }
}
