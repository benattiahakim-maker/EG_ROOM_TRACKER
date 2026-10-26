using EgRoomTracker.Application.Units;
using EgRoomTracker.Contracts.Results;

namespace EgRoomTracker.Application.Revit;

/// <summary>Quantités SI d'une pièce et état du volume (section 3).</summary>
public sealed record RoomQuantityValues(double? AreaM2, double? UnboundedHeightM, double? VolumeM3, VolumeStatus VolumeStatus);

/// <summary>
/// Règles de conversion des quantités (EGR-09), sans appel Revit : valeurs brutes en unités internes Revit,
/// converties par <see cref="IUnitConversionService"/> (UnitUtils côté Revit), sans arrondi (D7).
/// </summary>
public static class RoomQuantities
{
    /// <param name="rawArea">Surface en unités internes ; null si illisible.</param>
    /// <param name="rawUnboundedHeight">Hauteur non bornée en unités internes ; null si illisible.</param>
    /// <param name="readVolume">Lecture du volume en unités internes, appelée seulement si le calcul est actif et la pièce placée ; null ou exception si illisible.</param>
    /// <param name="volumesComputed">Réglage du document (<c>AreaVolumeSettings.ComputeVolumes</c>).</param>
    /// <param name="isPlaced">Placement vérifié (<c>Location</c> non nulle).</param>
    public static RoomQuantityValues Convert(
        double? rawArea,
        double? rawUnboundedHeight,
        Func<double?> readVolume,
        bool volumesComputed,
        bool isPlaced,
        IUnitConversionService units)
    {
        var area = ConvertFinite(rawArea, units.ToSquareMeters);
        var height = ConvertFinite(rawUnboundedHeight, units.ToMeters);

        if (!volumesComputed)
        {
            // Calcul désactivé : jamais 0 présenté comme un volume calculé.
            return new RoomQuantityValues(area, height, null, VolumeStatus.NotComputed);
        }

        if (!isPlaced)
        {
            // Pièce non placée : aucun volume géométrique disponible (Revit affiche « Non placée »).
            return new RoomQuantityValues(area, height, null, VolumeStatus.NotAvailable);
        }

        double? volume;
        try
        {
            volume = ConvertFinite(readVolume(), units.ToCubicMeters);
        }
        catch (Exception)
        {
            volume = null;
        }

        return volume is null
            ? new RoomQuantityValues(area, height, null, VolumeStatus.NotAvailable)
            : new RoomQuantityValues(area, height, volume, VolumeStatus.Computed);
    }

    /// <summary>
    /// Altitude d'un niveau (<c>Level.Elevation</c>, unités internes = pieds) en mètres, sans arrondi ; null si la valeur n'est pas finie
    /// (<c>UnitUtils</c> lève sur NaN ou l'infini) : le niveau est alors trié après ceux qui ont une altitude.
    /// </summary>
    public static double? LevelElevationM(double rawElevation, IUnitConversionService units) => ConvertFinite(rawElevation, units.ToMeters);

    /// <summary>Conversion d'une valeur finie ; null pour une valeur absente, NaN ou infinie (UnitUtils lève sur une valeur non finie).</summary>
    internal static double? ConvertFinite(double? raw, Func<double, double> convert)
    {
        if (raw is not { } value || !double.IsFinite(value))
        {
            return null;
        }

        var converted = convert(value);
        return double.IsFinite(converted) ? converted : null;
    }
}
