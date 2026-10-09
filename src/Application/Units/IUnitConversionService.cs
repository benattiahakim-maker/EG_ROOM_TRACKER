namespace EgRoomTracker.Application.Units;

/// <summary>
/// Conversions numériques explicites des unités internes Revit vers le SI (m, m², m³).
/// Sans arrondi de présentation (décision D7). Implémentation : EGR-09 (#9).
/// </summary>
public interface IUnitConversionService
{
    double ToMeters(double internalLength);

    double ToSquareMeters(double internalArea);

    double ToCubicMeters(double internalVolume);
}
