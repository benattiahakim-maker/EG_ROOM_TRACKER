using System.Text.Json;
using System.Text.Json.Nodes;
using EgRoomTracker.Application.Revit;
using EgRoomTracker.Application.Units;
using EgRoomTracker.Contracts.Codes;
using EgRoomTracker.Contracts.Configuration;
using EgRoomTracker.Contracts.Results;
using EgRoomTracker.Tests.Support;

namespace EgRoomTracker.Tests.Application;

/// <summary>Finitions et paramètres partagés (EGR-10) : représentation des valeurs, provenance GUID, indicateurs.</summary>
public class FinishesAndSharedParametersTests
{
    private static readonly SharedParameterMapping CodeLocal = new("codeLocal", Guid.Parse("3f2b8c1e-6d4a-4b7e-9a51-2c0d8e7f4a10"));
    private static readonly SharedParameterMapping SurfaceUtile = new("surfaceUtile", Guid.Parse("0b6c1d2e-3f40-4a5b-8c6d-7e8f9a0b1c2d"));
    private static readonly SharedParameterMapping Inconnu = new("inconnu", Guid.Parse("11111111-2222-4333-8444-555555555555"));

    private sealed class FeetUnits : IUnitConversionService
    {
        public double ToMeters(double internalLength) => internalLength * 0.3048;
        public double ToSquareMeters(double internalArea) => internalArea * 0.3048 * 0.3048;
        public double ToCubicMeters(double internalVolume) => internalVolume * 0.3048 * 0.3048 * 0.3048;
    }

    private static RoomReadBuilder NewBuilder() => new(412345, "uid", VolumeStatus.NotComputed);

    [Fact]
    public void Finishes_KeepTextEmptyAndNull_AndFlagAbsentParameter()
    {
        var b = NewBuilder();

        var result = b.Finishes(
            floor: b.Text(ParameterReading.Of("Résine"), "ROOM_FINISH_FLOOR"),
            wall: b.Text(ParameterReading.Of("Peinture blanche"), "ROOM_FINISH_WALL"),
            ceiling: b.Text(ParameterReading.Of(""), "ROOM_FINISH_CEILING"),
            @base: b.Text(ParameterReading.Absent, "ROOM_FINISH_BASE")).Build();

        Assert.Equal(new RoomFinishes("Résine", "Peinture blanche", "", null), result.Room.Finishes);
        Assert.Equal([QualityFlagCodes.MissingParameter], result.Room.QualityFlags);
        Assert.Equal("ROOM_FINISH_BASE", Assert.Single(result.Anomalies).Details["parameter"]);
    }

    [Fact]
    public void Finishes_NoValue_IsNullWithoutFlag()
    {
        var b = NewBuilder();

        var result = b.Finishes(b.Text(ParameterReading.NoValue, "F"), b.Text(ParameterReading.NoValue, "W"),
            b.Text(ParameterReading.NoValue, "C"), b.Text(ParameterReading.NoValue, "B")).Build();

        Assert.Equal(RoomFinishes.Empty, result.Room.Finishes);
        Assert.Empty(result.Room.QualityFlags);
    }

    [Fact]
    public void SharedParameters_OneEntryPerMapping_WithGuidProvenance()
    {
        var result = NewBuilder().SharedParameters(
        [
            (CodeLocal, SharedParameterRead.Of("A-001")),
            (SurfaceUtile, SharedParameterRead.NoValue),
            (Inconnu, SharedParameterRead.NotInDocument),
        ]).Build();

        var shared = result.Room.SharedParameters;
        Assert.Equal(["codeLocal", "surfaceUtile", "inconnu"], shared.Keys);
        Assert.Equal(new SharedParameterValue(CodeLocal.Guid, "A-001"), shared["codeLocal"]);
        Assert.Equal(new SharedParameterValue(SurfaceUtile.Guid, null), shared["surfaceUtile"]);
        Assert.Equal(new SharedParameterValue(Inconnu.Guid, null), shared["inconnu"]);
        Assert.Empty(result.Room.QualityFlags);
        Assert.Empty(result.Anomalies);
    }

    [Fact]
    public void SharedParameter_AbsentFromRoom_IsNullAndMissingParameterWithKeyAndGuid()
    {
        var result = NewBuilder().SharedParameters([(CodeLocal, SharedParameterRead.Absent), (SurfaceUtile, SharedParameterRead.Absent)]).Build();

        Assert.Null(result.Room.SharedParameters["codeLocal"].Value);
        Assert.Equal([QualityFlagCodes.MissingParameter], result.Room.QualityFlags);
        Assert.Equal(2, result.Anomalies.Count);
        Assert.Equal("codeLocal", result.Anomalies[0].Details["parameter"]);
        Assert.Equal("3f2b8c1e-6d4a-4b7e-9a51-2c0d8e7f4a10", result.Anomalies[0].Details["guid"]);
    }

    [Fact]
    public void SharedParameter_EmptyString_IsKept()
    {
        var result = NewBuilder().SharedParameters([(CodeLocal, SharedParameterValues.FromText(""))]).Build();

        Assert.Equal("", result.Room.SharedParameters["codeLocal"].Value);
    }

    [Fact]
    public void Values_ByStorageType()
    {
        var units = new FeetUnits();

        Assert.Equal(SharedParameterRead.NoValue, SharedParameterValues.FromText(null));
        Assert.Equal(SharedParameterRead.Of(1L), SharedParameterValues.FromInteger(1));
        Assert.Equal(SharedParameterRead.Of(10 * 0.3048), SharedParameterValues.FromDouble(10, SharedParameterMeasure.Length, units));
        Assert.Equal(SharedParameterRead.Of(100 * 0.3048 * 0.3048), SharedParameterValues.FromDouble(100, SharedParameterMeasure.Area, units));
        Assert.Equal(SharedParameterRead.Of(1000 * 0.3048 * 0.3048 * 0.3048), SharedParameterValues.FromDouble(1000, SharedParameterMeasure.Volume, units));
        Assert.Equal(SharedParameterRead.Of(0.25), SharedParameterValues.FromDouble(0.25, SharedParameterMeasure.Other, units));
        Assert.Equal(SharedParameterRead.Of(0.0), SharedParameterValues.FromDouble(0, SharedParameterMeasure.Area, units));
        Assert.Equal(SharedParameterRead.NoValue, SharedParameterValues.FromDouble(double.NaN, SharedParameterMeasure.Length, units));
        Assert.Equal(SharedParameterRead.Of("311"), SharedParameterValues.FromElementId(311));
        Assert.Equal(SharedParameterRead.NoValue, SharedParameterValues.FromElementId(-1));
        Assert.Equal(SharedParameterRead.NoValue, SharedParameterValues.FromElementId(null));
    }

    [Fact]
    public void Json_SharedValues_AreStringsOrNumbers()
    {
        var room = NewBuilder().SharedParameters(
        [
            (CodeLocal, SharedParameterRead.Of("007")),
            (SurfaceUtile, SharedParameterValues.FromDouble(100, SharedParameterMeasure.Area, new FeetUnits())),
            (Inconnu, SharedParameterValues.FromInteger(3)),
        ]).Build().Room;

        var json = JsonDocument.Parse(JsonSerializer.Serialize(room)).RootElement.GetProperty("SharedParameters");

        Assert.Equal(JsonValueKind.String, json.GetProperty("codeLocal").GetProperty("Value").ValueKind);
        Assert.Equal("007", json.GetProperty("codeLocal").GetProperty("Value").GetString());
        Assert.Equal(JsonValueKind.Number, json.GetProperty("surfaceUtile").GetProperty("Value").ValueKind);
        Assert.Equal(3, json.GetProperty("inconnu").GetProperty("Value").GetInt64());
    }

    [Fact]
    public void Pipeline_AddsDocumentLevelSharedParameterAnomalies_Once()
    {
        var anomaly = new AnomalyDto
        {
            Code = DocumentAnomalyCodes.SharedParameterNotFound,
            Severity = AnomalySeverity.Warning,
            Scope = AnomalyScope.Document,
            Message = "inconnu",
        };
        var pipeline = new Pipeline { SharedParameterAnomalies = [anomaly] };

        var result = pipeline.CreateService().Run(Pipeline.Request);

        Assert.Single(result.Anomalies, a => a.Code == DocumentAnomalyCodes.SharedParameterNotFound);
    }

    [Fact]
    public void ExtractionErrorRoom_KeepsOneNullEntryPerMapping()
    {
        var pipeline = new Pipeline { ReadRoom = _ => throw new InvalidOperationException("lecture") };

        var result = pipeline.CreateService().Run(Pipeline.Request);

        Assert.All(result.Rooms, r =>
        {
            Assert.Contains(QualityFlagCodes.ExtractionError, r.QualityFlags);
            Assert.Equal(new SharedParameterValue(CodeLocal.Guid, null), r.SharedParameters["codeLocal"]);
        });
    }

    [Fact]
    public void ContractJson_SharedValuesAndFinishes_AreSchemaValid()
    {
        var pipeline = new TestPipeline
        {
            RoomIds = [412345],
            LoadSettings = () => new ExtractionSettings
            {
                SchemaVersion = "1.0",
                SharedParameters = [CodeLocal, SurfaceUtile, Inconnu],
            },
            ReadRoom = id => NewBuilder()
                .Finishes("Résine", "Peinture blanche", "", null)
                .SharedParameters(
                [
                    (CodeLocal, SharedParameterRead.Of("007")),
                    (SurfaceUtile, SharedParameterValues.FromDouble(100, SharedParameterMeasure.Area, new FeetUnits())),
                    (Inconnu, SharedParameterRead.NotInDocument),
                ]).Build(),
        };

        var json = JsonNode.Parse(ContractJson.Serialize(pipeline.Run()))!;

        var shared = json["rooms"]![0]!["sharedParameters"]!;
        Assert.Equal("007", shared["codeLocal"]!["value"]!.GetValue<string>());
        Assert.Equal(100 * 0.3048 * 0.3048, shared["surfaceUtile"]!["value"]!.GetValue<double>());
        Assert.Null(shared["inconnu"]!["value"]);
        Assert.Equal("", json["rooms"]![0]!["finishes"]!["ceiling"]!.GetValue<string>());
        ResultSchema.AssertValid(json);
    }
}
