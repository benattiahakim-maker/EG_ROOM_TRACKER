using System.Text.Json.Nodes;
using EgRoomTracker.Application.Quality;
using EgRoomTracker.Application.Revit;
using EgRoomTracker.Contracts.Codes;
using EgRoomTracker.Contracts.Configuration;
using EgRoomTracker.Contracts.Results;
using EgRoomTracker.Tests.Support;

namespace EgRoomTracker.Tests.Application;

/// <summary>Compléments EGR-11 aux tests de spécification de Pending/ : détails, ordre des anomalies, contrat complet.</summary>
public class QualityCheckServiceTests
{
    private static readonly VolumeComputationState NotComputed = new(VolumeMode.Preserve, VolumesComputed: false);
    private static readonly VolumeComputationState Computed = new(VolumeMode.Preserve, VolumesComputed: true);

    private readonly QualityCheckService _service = new();

    [Fact]
    public void DuplicateNumber_DetailsListAllElementIdsNumerically()
    {
        var rooms = new[] { Rooms.Complete(415002, "001"), Rooms.Complete(9, "001"), Rooms.Complete(412345, "001") };

        var report = _service.Evaluate(rooms, [], Computed);

        var anomaly = report.Anomalies.First(a => a.Code == QualityFlagCodes.DuplicateNumber);
        Assert.Equal("001", anomaly.Details["number"]);
        Assert.Equal(["9", "412345", "415002"], (string[])anomaly.Details["elementIds"]!);
        Assert.Equal("Numéro 001 partagé par 3 pièces.", anomaly.Message);
    }

    [Fact]
    public void VolumeNotComputed_Details_CountRoomsAffected()
    {
        var rooms = new[] { Rooms.Complete(1, "001"), Rooms.Complete(2, "002") with { VolumeStatus = VolumeStatus.NotAvailable } };

        var anomaly = Assert.Single(_service.Evaluate(rooms, [], NotComputed).Anomalies);

        Assert.Equal("preserve", anomaly.Details["volumeMode"]);
        Assert.Equal(1, anomaly.Details["roomsAffected"]);
    }

    [Fact]
    public void Anomalies_AreDocumentFirst_ThenByRoomOrder_ReaderBeforeQuality()
    {
        var rooms = new[]
        {
            Rooms.Complete(412345, "001"),
            Rooms.Complete(412398, "002"),
            Rooms.Complete(415002, "001") with { AreaM2 = 0, QualityFlags = [QualityFlagCodes.Unplaced] },
        };

        var report = _service.Evaluate(rooms, [Rooms.RoomAnomaly(QualityFlagCodes.Unplaced, "415002")], NotComputed);

        Assert.Equal(
            [
                (DocumentAnomalyCodes.VolumeNotComputed, (string?)null),
                (QualityFlagCodes.DuplicateNumber, "412345"),
                (QualityFlagCodes.Unplaced, "415002"),
                (QualityFlagCodes.DuplicateNumber, "415002"),
            ],
            report.Anomalies.Select(a => (a.Code, a.ElementId)));
        Assert.Equal([QualityFlagCodes.Unplaced, QualityFlagCodes.DuplicateNumber], report.Rooms[2].QualityFlags);
    }

    [Fact]
    public void ExtractionErrorRoom_IsKept_WithoutQualityFlagsAdded()
    {
        var error = new RoomDto { ElementId = "5", UniqueId = "u5", VolumeStatus = VolumeStatus.NotAvailable, QualityFlags = [QualityFlagCodes.ExtractionError] };

        var report = _service.Evaluate([error, Rooms.Complete(6, "006")], [Rooms.RoomAnomaly(QualityFlagCodes.ExtractionError, "5", AnomalySeverity.Error)], Computed);

        Assert.Equal(2, report.Rooms.Count);
        Assert.Equal([QualityFlagCodes.ExtractionError], report.Rooms[0].QualityFlags);
        Assert.Equal(ExtractionStatus.SucceededWithWarnings, report.Status);
    }

    [Fact]
    public void Pipeline_WithRealQualityService_ProducesSchemaValidResult()
    {
        var pipeline = new TestPipeline
        {
            RoomIds = [412345, 412398, 415002],
            ReadRoom = id => id == 415002
                ? new RoomReadResult(Rooms.Complete(id, "001") with { AreaM2 = 0, QualityFlags = [QualityFlagCodes.Unplaced] },
                    [Rooms.RoomAnomaly(QualityFlagCodes.Unplaced, "415002")])
                : new RoomReadResult(Rooms.Complete(id, id == 412345 ? "001" : "002"), []),
            Evaluate = new QualityCheckService().Evaluate,
        };

        var result = pipeline.Run();
        var json = JsonNode.Parse(ContractJson.Serialize(result))!;

        Assert.Equal(ExtractionStatus.SucceededWithWarnings, result.Summary.Status);
        Assert.Equal(["DUPLICATE_NUMBER"], json["rooms"]![0]!["qualityFlags"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.Equal(["UNPLACED", "DUPLICATE_NUMBER"], json["rooms"]![2]!["qualityFlags"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.Equal("VOLUME_NOT_COMPUTED", json["anomalies"]![0]!["code"]!.GetValue<string>());
        ResultSchema.AssertValid(json);
    }
}
