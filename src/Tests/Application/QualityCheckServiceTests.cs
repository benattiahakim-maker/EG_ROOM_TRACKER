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

    // DUPLICATE_NUMBER et variantes (Refs #11).

    private static readonly ElementRef PhaseA = new("86961", "Nouvelle construction");
    private static readonly ElementRef PhaseB = new("86962", "Existant");

    private static RoomDto InOption(long id, string number, string? optionId, string? setId, ElementRef? phase = null) =>
        Rooms.Complete(id, number, phase ?? PhaseA) with
        {
            DesignOption = optionId is null ? null : new ElementRef(optionId, $"Variante {optionId}"),
            DesignOptionSetId = optionId is null ? null : setId,
        };

    private static string[] DuplicateIds(QualityReport report, string elementId) =>
        report.Anomalies
            .Where(a => a.Code == QualityFlagCodes.DuplicateNumber && a.ElementId == elementId)
            .Select(a => (string[])a.Details["elementIds"]!)
            .SingleOrDefault() ?? [];

    [Fact]
    public void DuplicateNumber_DifferentOptionsOfSameSet_AreNotDuplicates()
    {
        var report = _service.Evaluate([InOption(1, "101", "500", "400"), InOption(2, "101", "501", "400")], [], Computed);

        Assert.All(report.Rooms, r => Assert.DoesNotContain(QualityFlagCodes.DuplicateNumber, r.QualityFlags));
        Assert.DoesNotContain(report.Anomalies, a => a.Code == QualityFlagCodes.DuplicateNumber);
    }

    [Fact]
    public void DuplicateNumber_SameOption_AreDuplicates()
    {
        var report = _service.Evaluate([InOption(1, "101", "500", "400"), InOption(2, "101", "500", "400")], [], Computed);

        Assert.All(report.Rooms, r => Assert.Contains(QualityFlagCodes.DuplicateNumber, r.QualityFlags));
        Assert.Equal(["1", "2"], DuplicateIds(report, "1"));
    }

    [Fact]
    public void DuplicateNumber_MainModelAndOption_AreDuplicates()
    {
        var report = _service.Evaluate([InOption(1, "101", null, null), InOption(2, "101", "500", "400")], [], Computed);

        Assert.All(report.Rooms, r => Assert.Contains(QualityFlagCodes.DuplicateNumber, r.QualityFlags));
    }

    [Fact]
    public void DuplicateNumber_OptionsOfDifferentSets_AreDuplicates()
    {
        var report = _service.Evaluate([InOption(1, "101", "500", "400"), InOption(2, "101", "600", "410")], [], Computed);

        Assert.All(report.Rooms, r => Assert.Contains(QualityFlagCodes.DuplicateNumber, r.QualityFlags));
    }

    [Fact]
    public void DuplicateNumber_DifferentPhases_AreNotDuplicates()
    {
        var report = _service.Evaluate([InOption(1, "101", null, null, PhaseA), InOption(2, "101", null, null, PhaseB)], [], Computed);

        Assert.DoesNotContain(report.Anomalies, a => a.Code == QualityFlagCodes.DuplicateNumber);
    }

    [Fact]
    public void DuplicateNumber_MainModelRoom_IsComparedWithEveryOption_DetailsPerRoom()
    {
        // Principal 1 en conflit avec 2 et 3 ; 2 et 3 (même jeu, variantes différentes) s'excluent entre elles.
        var rooms = new[] { InOption(1, "101", null, null), InOption(2, "101", "500", "400"), InOption(3, "101", "501", "400") };

        var report = _service.Evaluate(rooms, [], Computed);

        Assert.All(report.Rooms, r => Assert.Contains(QualityFlagCodes.DuplicateNumber, r.QualityFlags));
        Assert.Equal(["1", "2", "3"], DuplicateIds(report, "1"));
        Assert.Equal(["1", "2"], DuplicateIds(report, "2"));
        Assert.Equal(["1", "3"], DuplicateIds(report, "3"));
        Assert.Equal("Numéro 101 partagé par 2 pièces.", report.Anomalies.First(a => a.Code == QualityFlagCodes.DuplicateNumber && a.ElementId == "2").Message);
    }

    [Fact]
    public void DuplicateNumber_UnknownOptionSet_IsReportedConservatively()
    {
        var report = _service.Evaluate([InOption(1, "101", "500", null), InOption(2, "101", "501", "400")], [], Computed);

        Assert.All(report.Rooms, r => Assert.Contains(QualityFlagCodes.DuplicateNumber, r.QualityFlags));
        Assert.All(report.Anomalies.Where(a => a.Code == QualityFlagCodes.DuplicateNumber),
            a => Assert.Equal(true, a.Details[QualityCheckService.OptionSetUnknownDetail]));
    }

    [Fact]
    public void DuplicateNumber_KnownSets_HaveNoOptionSetUnknownDetail()
    {
        var rooms = new[] { InOption(1, "101", null, null), InOption(2, "101", "500", "400"), InOption(3, "101", "600", "410") };

        var report = _service.Evaluate(rooms, [], Computed);

        var duplicates = report.Anomalies.Where(a => a.Code == QualityFlagCodes.DuplicateNumber).ToList();
        Assert.Equal(3, duplicates.Count);
        Assert.All(duplicates, a => Assert.False(a.Details.ContainsKey(QualityCheckService.OptionSetUnknownDetail)));
    }

    [Fact]
    public void DuplicateNumber_OptionSetUnknown_OnlyOnRoomsWhoseConflictRestsOnIt()
    {
        // 1 (principal) en conflit avec 2 et 3 sans jeu inconnu en jeu pour lui ; 2 (jeu inconnu) et 3 (variante différente).
        var rooms = new[] { InOption(1, "101", null, null), InOption(2, "101", "500", null), InOption(3, "101", "501", "400") };

        var report = _service.Evaluate(rooms, [], Computed);

        AnomalyDto Duplicate(string id) => report.Anomalies.Single(a => a.Code == QualityFlagCodes.DuplicateNumber && a.ElementId == id);
        Assert.False(Duplicate("1").Details.ContainsKey(QualityCheckService.OptionSetUnknownDetail));
        Assert.Equal(true, Duplicate("2").Details[QualityCheckService.OptionSetUnknownDetail]);
        Assert.Equal(true, Duplicate("3").Details[QualityCheckService.OptionSetUnknownDetail]);
        Assert.Equal(["1", "2", "3"], DuplicateIds(report, "2"));
    }

    [Fact]
    public void DuplicateNumber_SameOptionWithUnknownSet_HasNoOptionSetUnknownDetail()
    {
        var report = _service.Evaluate([InOption(1, "101", "500", null), InOption(2, "101", "500", null)], [], Computed);

        Assert.All(report.Anomalies.Where(a => a.Code == QualityFlagCodes.DuplicateNumber),
            a => Assert.False(a.Details.ContainsKey(QualityCheckService.OptionSetUnknownDetail)));
    }

    [Fact]
    public void DesignOptionSetId_IsNotSerialized()
    {
        var example = ContractJson.Deserialize(ResultSchema.LoadExample().ToJsonString());
        var result = example with { Rooms = [InOption(1, "101", "500", "400")], Anomalies = [] };

        var json = JsonNode.Parse(ContractJson.Serialize(result))!;

        Assert.False(json["rooms"]![0]!.AsObject().ContainsKey("designOptionSetId"));
        Assert.Equal("500", json["rooms"]![0]!["designOption"]!["elementId"]!.GetValue<string>());
        ResultSchema.AssertValid(json);
    }
}
