using System.Globalization;
using EgRoomTracker.Application.Units;

namespace EgRoomTracker.Application.Revit;

/// <summary>État de lecture d'un paramètre partagé sur une pièce (section 3, représentation des valeurs).</summary>
public enum SharedParameterState
{
    /// <summary>GUID inconnu dans le document : valeur null ; anomalie document SHARED_PARAMETER_NOT_FOUND (une fois).</summary>
    NotInDocument,

    /// <summary>GUID connu du document mais paramètre absent de la pièce : null et MISSING_PARAMETER.</summary>
    Absent,

    /// <summary>Paramètre présent sans valeur : null.</summary>
    NoValue,

    /// <summary>Valeur lue (chaîne, éventuellement vide, ou nombre).</summary>
    HasValue,
}

/// <summary>Lecture brute d'un paramètre partagé, indépendante de Revit.</summary>
public readonly record struct SharedParameterRead(SharedParameterState State, object? Value)
{
    public static SharedParameterRead NotInDocument { get; } = new(SharedParameterState.NotInDocument, null);

    public static SharedParameterRead Absent { get; } = new(SharedParameterState.Absent, null);

    public static SharedParameterRead NoValue { get; } = new(SharedParameterState.NoValue, null);

    public static SharedParameterRead Of(object value) => new(SharedParameterState.HasValue, value);
}

/// <summary>Grandeur d'un paramètre partagé de stockage double (spec Revit <c>Definition.GetDataType()</c>).</summary>
public enum SharedParameterMeasure
{
    /// <summary>Longueur (<c>SpecTypeId.Length</c>) : convertie en m.</summary>
    Length,

    /// <summary>Surface (<c>SpecTypeId.Area</c>) : convertie en m².</summary>
    Area,

    /// <summary>Volume (<c>SpecTypeId.Volume</c>) : converti en m³.</summary>
    Volume,

    /// <summary>Autre spec (nombre, pourcentage, angle…) : valeur interne Revit exportée telle quelle.</summary>
    Other,
}

/// <summary>
/// Conversion des valeurs de paramètres partagés selon leur stockage Revit (EGR-10), sans <c>AsValueString</c>
/// ni arrondi : texte tel quel, entier en nombre, double converti en SI pour longueur/surface/volume,
/// ElementId en chaîne (<c>ElementId.Value</c>, comme les autres identifiants du contrat).
/// </summary>
public static class SharedParameterValues
{
    public static SharedParameterRead FromText(string? value) =>
        value is null ? SharedParameterRead.NoValue : SharedParameterRead.Of(value);

    public static SharedParameterRead FromInteger(int value) => SharedParameterRead.Of((long)value);

    public static SharedParameterRead FromDouble(double value, SharedParameterMeasure measure, IUnitConversionService units)
    {
        var converted = RoomQuantities.ConvertFinite(value, measure switch
        {
            SharedParameterMeasure.Length => units.ToMeters,
            SharedParameterMeasure.Area => units.ToSquareMeters,
            SharedParameterMeasure.Volume => units.ToCubicMeters,
            _ => v => v,
        });
        return converted is { } number ? SharedParameterRead.Of(number) : SharedParameterRead.NoValue;
    }

    /// <summary>ElementId : null si absent ou invalide (<c>-1</c>).</summary>
    public static SharedParameterRead FromElementId(long? value) =>
        value is null or -1 ? SharedParameterRead.NoValue : SharedParameterRead.Of(value.Value.ToString(CultureInfo.InvariantCulture));
}
