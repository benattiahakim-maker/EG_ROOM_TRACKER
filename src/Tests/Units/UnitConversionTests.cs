using System.Text.Json.Nodes;
using EgRoomTracker.Application.Revit;
using EgRoomTracker.Application.Units;
using EgRoomTracker.Contracts.Results;
using EgRoomTracker.Tests.Support;

namespace EgRoomTracker.Tests.Units;

/// <summary>
/// Conversions vers le SI (section 3) via une doublure de <see cref="IUnitConversionService"/> : l'implémentation
/// de production (<c>RevitUnitConversionService</c>, UnitUtils) n'est testable que dans Revit (EGR-09, EGR-18).
/// Vérifie les facteurs de référence et que les valeurs converties traversent DTO et JSON sans arrondi.
/// </summary>
public class UnitConversionTests
{
    private readonly IUnitConversionService _units = new FakeUnitConversionService();

    [Theory]
    [InlineData(1d, 0.3048)]
    [InlineData(10d, 3.048)]
    [InlineData(8.858267716535433, 2.7)]
    [InlineData(0d, 0d)]
    public void ToMeters_UsesExactFootFactor(double feet, double meters) =>
        Assert.Equal(meters, _units.ToMeters(feet), 12);

    [Theory]
    [InlineData(1d, 0.09290304)]
    [InlineData(198.32504942787662, 18.425)]
    [InlineData(0d, 0d)]
    public void ToSquareMeters_UsesSquaredFactor(double squareFeet, double squareMeters) =>
        Assert.Equal(squareMeters, _units.ToSquareMeters(squareFeet), 10);

    [Theory]
    [InlineData(1d, 0.028316846592)]
    [InlineData(1756.8163827272538, 49.7475)]
    [InlineData(0d, 0d)]
    public void ToCubicMeters_UsesCubedFactor(double cubicFeet, double cubicMeters) =>
        Assert.Equal(cubicMeters, _units.ToCubicMeters(cubicFeet), 9);

    [Fact]
    public void Conversions_AreWithinAcceptanceTolerances()
    {
        // Tolérances proposées en section 8 (décision D5) : 0,001 m, 0,001 m², 0,001 m³.
        Assert.InRange(_units.ToMeters(8.858) - 2.7, -0.001, 0.001);
        Assert.InRange(_units.ToSquareMeters(198.325) - 18.425, -0.001, 0.001);
        Assert.InRange(_units.ToCubicMeters(1756.816) - 49.7475, -0.001, 0.001);
    }

    [Fact]
    public void ZeroIsPreserved_NotTurnedIntoNull()
    {
        Assert.Equal(0d, _units.ToSquareMeters(0d));
        Assert.Equal(0d, _units.ToCubicMeters(0d));
    }

    [Fact]
    public void ConvertedValues_AreSerializedAtFullPrecision()
    {
        var area = _units.ToSquareMeters(123.456789);
        var height = _units.ToMeters(9.87654321);
        var volume = _units.ToCubicMeters(1234.5678);
        var room = Rooms.Complete(412345) with
        {
            AreaM2 = area,
            UnboundedHeightM = height,
            VolumeStatus = VolumeStatus.Computed,
            VolumeM3 = volume,
        };
        var pipeline = new TestPipeline { RoomIds = [412345], ReadRoom = _ => new RoomReadResult(room, []) };

        var json = JsonNode.Parse(ContractJson.Serialize(pipeline.Run()))!;

        var r = json["rooms"]![0]!;
        Assert.Equal(area, r["areaM2"]!.GetValue<double>());
        Assert.Equal(height, r["unboundedHeightM"]!.GetValue<double>());
        Assert.Equal(volume, r["volumeM3"]!.GetValue<double>());
        ResultSchema.AssertValid(json);
    }
}
