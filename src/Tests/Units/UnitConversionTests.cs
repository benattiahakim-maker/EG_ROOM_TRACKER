using System.Text.Json;
using System.Text.Json.Nodes;
using EgRoomTracker.Application.Revit;
using EgRoomTracker.Application.Units;
using EgRoomTracker.Contracts.Results;
using EgRoomTracker.Tests.Support;

namespace EgRoomTracker.Tests.Units;

/// <summary>
/// Quantités converties vers le SI (section 3) via une doublure d'<see cref="IUnitConversionService"/>
/// (l'implémentation UnitUtils n'est testable que dans Revit, EGR-09/EGR-18) : précision complète jusqu'au JSON,
/// distinction entre valeur nulle (0) et valeur absente (null).
/// </summary>
public class UnitConversionTests
{
    private readonly IUnitConversionService _units = new FakeUnitConversionService();

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

        var r = Serialize(room)["rooms"]![0]!;

        Assert.Equal(area, r["areaM2"]!.GetValue<double>());
        Assert.Equal(height, r["unboundedHeightM"]!.GetValue<double>());
        Assert.Equal(volume, r["volumeM3"]!.GetValue<double>());
    }

    [Fact]
    public void ZeroQuantity_StaysZero_AndAbsentQuantity_StaysNull()
    {
        var room = Rooms.Complete(412345) with
        {
            AreaM2 = _units.ToSquareMeters(0d),
            UnboundedHeightM = null,
            VolumeStatus = VolumeStatus.Computed,
            VolumeM3 = _units.ToCubicMeters(0d),
        };

        var json = Serialize(room);

        var r = json["rooms"]![0]!;
        Assert.Equal(JsonValueKind.Number, r["areaM2"]!.GetValueKind());
        Assert.Equal(0d, r["areaM2"]!.GetValue<double>());
        Assert.Equal(0d, r["volumeM3"]!.GetValue<double>());
        Assert.True(r.AsObject().ContainsKey("unboundedHeightM"));
        Assert.Null(r["unboundedHeightM"]);
        ResultSchema.AssertValid(json);
    }

    private static JsonNode Serialize(RoomDto room)
    {
        var pipeline = new TestPipeline { RoomIds = [412345], ReadRoom = _ => new RoomReadResult(room, []) };
        return JsonNode.Parse(ContractJson.Serialize(pipeline.Run()))!;
    }
}
