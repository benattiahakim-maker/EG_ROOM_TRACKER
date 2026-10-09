using System.Globalization;
using System.Text.Json;
using EgRoomTracker.Application.Revit;
using EgRoomTracker.Application.Units;
using EgRoomTracker.Contracts.Results;

namespace EgRoomTracker.Tests.Application;

/// <summary>Quantités et état du volume (EGR-09) : conversion SI, volumeStatus, valeurs non finies.</summary>
public class RoomQuantitiesTests
{
    /// <summary>Facteur exact pied → mètre (0,3048), comme les tests d'unités EGR-17.</summary>
    private sealed class FeetUnits : IUnitConversionService
    {
        private const double Foot = 0.3048;
        public double ToMeters(double internalLength) => internalLength * Foot;
        public double ToSquareMeters(double internalArea) => internalArea * Foot * Foot;
        public double ToCubicMeters(double internalVolume) => internalVolume * Foot * Foot * Foot;
    }

    private static readonly IUnitConversionService Units = new FeetUnits();

    [Fact]
    public void Computed_ConvertsAllQuantitiesWithoutRounding()
    {
        var q = RoomQuantities.Convert(100.0, 10.0, () => 1000.0, volumesComputed: true, isPlaced: true, Units);

        Assert.Equal(100.0 * 0.3048 * 0.3048, q.AreaM2);
        Assert.Equal(10.0 * 0.3048, q.UnboundedHeightM);
        Assert.Equal(1000.0 * 0.3048 * 0.3048 * 0.3048, q.VolumeM3);
        Assert.Equal(VolumeStatus.Computed, q.VolumeStatus);
    }

    [Fact]
    public void ComputationDisabled_VolumeNullNotComputed_AndVolumeNotRead()
    {
        var read = false;
        var q = RoomQuantities.Convert(100.0, 10.0, () => { read = true; return 0.0; }, volumesComputed: false, isPlaced: true, Units);

        Assert.Null(q.VolumeM3);
        Assert.Equal(VolumeStatus.NotComputed, q.VolumeStatus);
        Assert.False(read);
        Assert.NotNull(q.AreaM2);
    }

    [Fact]
    public void ComputationDisabled_UnplacedRoom_StaysNotComputed()
    {
        var q = RoomQuantities.Convert(0.0, 8.0, () => 0.0, volumesComputed: false, isPlaced: false, Units);

        Assert.Equal(0.0, q.AreaM2);
        Assert.Null(q.VolumeM3);
        Assert.Equal(VolumeStatus.NotComputed, q.VolumeStatus);
    }

    [Fact]
    public void Computed_UnplacedRoom_VolumeNotAvailable()
    {
        var q = RoomQuantities.Convert(0.0, 8.0, () => 0.0, volumesComputed: true, isPlaced: false, Units);

        Assert.Equal(0.0, q.AreaM2);
        Assert.Null(q.VolumeM3);
        Assert.Equal(VolumeStatus.NotAvailable, q.VolumeStatus);
    }

    [Fact]
    public void Computed_VolumeReadThrows_NotAvailable()
    {
        var q = RoomQuantities.Convert(10.0, 8.0, () => throw new InvalidOperationException("lecture"), volumesComputed: true, isPlaced: true, Units);

        Assert.Null(q.VolumeM3);
        Assert.Equal(VolumeStatus.NotAvailable, q.VolumeStatus);
        Assert.NotNull(q.AreaM2);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NonFiniteValues_BecomeNull_AndVolumeNotAvailable(double raw)
    {
        var q = RoomQuantities.Convert(raw, raw, () => raw, volumesComputed: true, isPlaced: true, Units);

        Assert.Null(q.AreaM2);
        Assert.Null(q.UnboundedHeightM);
        Assert.Null(q.VolumeM3);
        Assert.Equal(VolumeStatus.NotAvailable, q.VolumeStatus);
    }

    [Fact]
    public void AbsentRawValues_BecomeNull_WithoutCallingConversion()
    {
        var q = RoomQuantities.Convert(null, null, () => null, volumesComputed: true, isPlaced: true, new ThrowingUnits());

        Assert.Null(q.AreaM2);
        Assert.Null(q.UnboundedHeightM);
        Assert.Equal(VolumeStatus.NotAvailable, q.VolumeStatus);
    }

    [Fact]
    public void Builder_AppliesQuantities_AndReplacesInitialStatus()
    {
        var q = RoomQuantities.Convert(100.0, 10.0, () => 1000.0, volumesComputed: true, isPlaced: true, Units);

        var room = new RoomReadBuilder(1, "uid", VolumeStatus.NotAvailable).Placement(true).Quantities(q).Build().Room;

        Assert.Equal(q.AreaM2, room.AreaM2);
        Assert.Equal(q.UnboundedHeightM, room.UnboundedHeightM);
        Assert.Equal(q.VolumeM3, room.VolumeM3);
        Assert.Equal(VolumeStatus.Computed, room.VolumeStatus);
    }

    [Fact]
    public void Json_NumbersAreCultureIndependent()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
            var q = RoomQuantities.Convert(100.0, 10.0, () => 1000.0, volumesComputed: true, isPlaced: true, Units);
            var room = new RoomReadBuilder(1, "uid", VolumeStatus.NotAvailable).Placement(true).Quantities(q).Build().Room;

            var json = JsonSerializer.Serialize(room);

            Assert.Contains("3.048", json);
            Assert.DoesNotContain("3,048", json);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    private sealed class ThrowingUnits : IUnitConversionService
    {
        public double ToMeters(double internalLength) => throw new InvalidOperationException();
        public double ToSquareMeters(double internalArea) => throw new InvalidOperationException();
        public double ToCubicMeters(double internalVolume) => throw new InvalidOperationException();
    }
}
