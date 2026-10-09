using Autodesk.Revit.DB;
using EgRoomTracker.Application.Units;

namespace EgRoomTracker.Revit;

/// <summary>Conversions des unités internes Revit vers le SI via <see cref="UnitUtils"/> (section 3). Finalisation en EGR-09 (#9).</summary>
public sealed class RevitUnitConversionService : IUnitConversionService
{
    public double ToMeters(double internalLength) =>
        UnitUtils.ConvertFromInternalUnits(internalLength, UnitTypeId.Meters);

    public double ToSquareMeters(double internalArea) =>
        UnitUtils.ConvertFromInternalUnits(internalArea, UnitTypeId.SquareMeters);

    public double ToCubicMeters(double internalVolume) =>
        UnitUtils.ConvertFromInternalUnits(internalVolume, UnitTypeId.CubicMeters);
}
