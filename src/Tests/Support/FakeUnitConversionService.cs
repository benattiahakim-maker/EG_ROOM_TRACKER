using EgRoomTracker.Application.Units;

namespace EgRoomTracker.Tests.Support;

/// <summary>
/// Doublure de <see cref="IUnitConversionService"/> sans Revit : unités internes Revit (pied décimal)
/// vers le SI avec les facteurs exacts (1 ft = 0,3048 m), sans arrondi (décision D7).
/// </summary>
internal sealed class FakeUnitConversionService : IUnitConversionService
{
    public const double MetersPerFoot = 0.3048;

    public List<(string Kind, double Value)> Calls { get; } = [];

    public double ToMeters(double internalLength)
    {
        Calls.Add(("length", internalLength));
        return internalLength * MetersPerFoot;
    }

    public double ToSquareMeters(double internalArea)
    {
        Calls.Add(("area", internalArea));
        return internalArea * MetersPerFoot * MetersPerFoot;
    }

    public double ToCubicMeters(double internalVolume)
    {
        Calls.Add(("volume", internalVolume));
        return internalVolume * MetersPerFoot * MetersPerFoot * MetersPerFoot;
    }
}
