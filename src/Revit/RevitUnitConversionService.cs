using Autodesk.Revit.DB;
using EgRoomTracker.Application.Units;

namespace EgRoomTracker.Revit;

/// <summary>Conversions des unités internes Revit vers le SI via <see cref="UnitUtils.ConvertFromInternalUnits(double, ForgeTypeId)"/> et <see cref="UnitTypeId"/> (section 3, EGR-09). Lève ArgumentException sur une valeur non finie : filtrée en amont par RoomQuantities.</summary>
public sealed class RevitUnitConversionService : IUnitConversionService
{
    public double ToMeters(double internalLength) =>
        UnitUtils.ConvertFromInternalUnits(internalLength, UnitTypeId.Meters);

    public double ToSquareMeters(double internalArea) =>
        UnitUtils.ConvertFromInternalUnits(internalArea, UnitTypeId.SquareMeters);

    public double ToCubicMeters(double internalVolume) =>
        UnitUtils.ConvertFromInternalUnits(internalVolume, UnitTypeId.CubicMeters);
}
