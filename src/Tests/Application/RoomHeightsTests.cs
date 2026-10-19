using System.Text.Json.Nodes;
using EgRoomTracker.Application;
using EgRoomTracker.Application.Revit;
using EgRoomTracker.Contracts.Codes;
using EgRoomTracker.Contracts.Configuration;
using EgRoomTracker.Contracts.Results;
using EgRoomTracker.Logging;
using EgRoomTracker.Tests.Support;

namespace EgRoomTracker.Tests.Application;

/// <summary>Hauteurs des pièces par lancer de rayons (<c>roomHeights</c>) : renseignée, mesurée, effective.</summary>
public sealed class RoomHeightsTests
{
    private const long RoomA = 412345;
    private const long RoomB = 415002;

    private sealed class FakeHeights(Func<long, RoomHeightMeasurement> measure) : IRoomHeightService
    {
        public List<(long Id, double Step)> Calls { get; } = [];

        public RoomHeightMeasurement Measure(long roomElementId, double stepM)
        {
            Calls.Add((roomElementId, stepM));
            return measure(roomElementId);
        }
    }

    private static RoomHeightMeasurement WithObstacle(double declared, double measured) => new(
        declared, measured, "Ceilings", "Projet", new Dictionary<string, double> { ["Ceilings"] = measured, ["Floors"] = measured + 0.5 }, 12);

    private static RoomHeightMeasurement WithoutObstacle(double declared) => new(declared, null, null, null, null, 9);

    private static Pipeline Create(FakeHeights? heights, bool roomHeights = true, double step = 0.5) => new()
    {
        LoadSettings = () => new ExtractionSettings { SchemaVersion = "1.0", RoomHeights = roomHeights, HeightStepM = step },
        HeightService = heights,
    };

    [Fact]
    public void Measured_declared_and_effective_heights_are_separate()
    {
        var heights = new FakeHeights(id => id == RoomA ? WithObstacle(3.0, 2.45) : WithoutObstacle(3.1));
        var pipeline = Create(heights, step: 0.25);

        var result = pipeline.CreateService().Run(Pipeline.Request);

        var a = result.Rooms.Single(r => r.ElementId == RoomA.ToString()).Heights!;
        Assert.Equal((3.0, 2.45, 2.45, HeightSources.Measured, 12), (a.DeclaredM, a.MeasuredMinM, a.EffectiveM, a.EffectiveSource, a.Samples));
        Assert.Equal("Ceilings", a.LimitingCategory);
        Assert.Equal("Projet", a.LimitingDocument);
        Assert.Equal(["Ceilings", "Floors"], a.ByCategoryM!.Keys);

        // Sans obstacle : la hauteur renseignée est retenue (règle de TRC), la mesurée reste vide.
        var b = result.Rooms.Single(r => r.ElementId == RoomB.ToString()).Heights!;
        Assert.Equal((3.1, (double?)null, 3.1, HeightSources.Declared), (b.DeclaredM, b.MeasuredMinM, b.EffectiveM, b.EffectiveSource));
        Assert.Null(b.LimitingCategory);

        Assert.Equal(new HeightsSummary(2, 1, 1, 0), result.Summary.Heights);
        Assert.Equal(true, result.Summary.Options.RoomHeights);
        Assert.Equal(0.25, result.Summary.Options.HeightStepM);
        Assert.All(heights.Calls, call => Assert.Equal(0.25, call.Step));
        Assert.Contains(pipeline.Logs, l => l.EventCode == LogEventCodes.RoomHeightsMeasured);
    }

    [Fact]
    public void Without_declared_height_nor_obstacle_the_effective_height_is_empty()
    {
        var heights = new FakeHeights(_ => new RoomHeightMeasurement(null, null, null, null, null, 0));
        var result = Create(heights).CreateService().Run(Pipeline.Request);

        var room = result.Rooms[0].Heights!;
        Assert.Null(room.EffectiveM);
        Assert.Null(room.EffectiveSource);
    }

    [Fact]
    public void Zero_is_never_returned_as_a_height()
    {
        // Un service qui renverrait 0 pour « rien trouvé » ou « non renseigné » ne produit aucune hauteur nulle.
        var heights = new FakeHeights(_ => new RoomHeightMeasurement(0.0, 0.0, "Ceilings", "Projet", new Dictionary<string, double> { ["Ceilings"] = 0.0 }, 5));

        var result = Create(heights).CreateService().Run(Pipeline.Request);

        Assert.All(result.Rooms, room =>
        {
            var h = room.Heights!;
            Assert.Null(h.DeclaredM);
            Assert.Null(h.MeasuredMinM);
            Assert.Null(h.EffectiveM);
            Assert.Null(h.EffectiveSource);
            Assert.Null(h.LimitingCategory);
            Assert.Null(h.ByCategoryM);
        });
        Assert.Equal(new HeightsSummary(2, 0, 2, 0), result.Summary.Heights);
    }

    [Fact]
    public void No_hit_falls_back_to_the_declared_height_and_the_measured_one_stays_empty()
    {
        var heights = new FakeHeights(_ => WithoutObstacle(3.25));

        var result = Create(heights).CreateService().Run(Pipeline.Request);
        var json = JsonNode.Parse(ContractJson.Serialize(result))!;

        var h = json["rooms"]![0]!["hauteurs"]!;
        Assert.Null(h["mesuree_min_m"]);
        Assert.Equal(3.25, h["effective_m"]!.GetValue<double>());
        Assert.Equal("declared", h["source_effective"]!.GetValue<string>());
    }

    [Fact]
    public void Failing_room_is_counted_and_others_are_kept()
    {
        var heights = new FakeHeights(id => id == RoomA ? throw new InvalidOperationException("rayon") : WithObstacle(3.0, 2.6));
        var pipeline = Create(heights);

        var result = pipeline.CreateService().Run(Pipeline.Request);

        Assert.Empty(result.Summary.Errors);
        Assert.Null(result.Rooms.Single(r => r.ElementId == RoomA.ToString()).Heights);
        Assert.NotNull(result.Rooms.Single(r => r.ElementId == RoomB.ToString()).Heights);
        Assert.Equal(new HeightsSummary(2, 1, 0, 1), result.Summary.Heights);
        var anomaly = Assert.Single(result.Anomalies, a => a.Code == DocumentAnomalyCodes.HeightMeasurementFailed);
        Assert.Equal(AnomalySeverity.Warning, anomaly.Severity);
        Assert.Equal(1, anomaly.Details["failed"]);
        Assert.Equal("InvalidOperationException", anomaly.Details["reason"]);
        Assert.Contains(pipeline.Logs, l => l.EventCode == LogEventCodes.RoomHeightsFailed);
    }

    [Fact]
    public void Missing_service_is_reported_not_fatal()
    {
        var result = Create(null).CreateService().Run(Pipeline.Request);

        Assert.Empty(result.Summary.Errors);
        Assert.Equal(new HeightsSummary(2, 0, 0, 2), result.Summary.Heights);
        Assert.Contains(result.Anomalies, a => a.Code == DocumentAnomalyCodes.HeightMeasurementFailed);
    }

    [Fact]
    public void Unplaced_rooms_are_not_measured()
    {
        var heights = new FakeHeights(_ => WithObstacle(3.0, 2.5));
        var pipeline = Create(heights);
        pipeline.ReadRoom = room => new RoomReadResult(
            new RoomDto
            {
                ElementId = room.ElementId.ToString(),
                UniqueId = room.UniqueId,
                VolumeStatus = VolumeStatus.NotComputed,
                QualityFlags = room.ElementId == RoomB ? [QualityFlagCodes.Unplaced] : [],
            },
            []);

        var result = pipeline.CreateService().Run(Pipeline.Request);

        Assert.Equal([RoomA], heights.Calls.Select(c => c.Id));
        Assert.Equal(new HeightsSummary(1, 1, 0, 0), result.Summary.Heights);
    }

    [Fact]
    public void Disabled_by_default_calls_nothing_and_writes_no_new_property()
    {
        var heights = new FakeHeights(_ => WithObstacle(3.0, 2.5));

        var result = Create(heights, roomHeights: false).CreateService().Run(Pipeline.Request);
        var json = JsonNode.Parse(ContractJson.Serialize(result))!;

        Assert.Empty(heights.Calls);
        Assert.Null(result.Summary.Heights);
        Assert.False(json["rooms"]![0]!.AsObject().ContainsKey("hauteurs"));
        Assert.False(json["summary"]!.AsObject().ContainsKey("heights"));
        Assert.False(json["summary"]!["options"]!.AsObject().ContainsKey("roomHeights"));
        ResultSchema.AssertValid(json);
    }

    [Fact]
    public void Contract_WithHeights_IsSchemaValid()
    {
        var heights = new FakeHeights(id => id == RoomA ? WithObstacle(3.0, 2.45) : WithoutObstacle(3.1));

        var result = Create(heights).CreateService().Run(Pipeline.Request);
        var json = JsonNode.Parse(ContractJson.Serialize(result))!;

        var room = json["rooms"]!.AsArray().Single(r => r!["id_revit"]!.GetValue<string>() == RoomB.ToString())!["hauteurs"]!;
        Assert.Null(room["mesuree_min_m"]);
        Assert.Equal("declared", room["source_effective"]!.GetValue<string>());
        Assert.False(room.AsObject().ContainsKey("limitingCategory"));
        Assert.Equal(1, json["summary"]!["heights"]!["measured"]!.GetValue<int>());
        ResultSchema.AssertValid(json);
    }

    [Fact]
    public void Accumulator_ignores_hits_too_close_and_keeps_the_minimum_per_category()
    {
        var accumulator = new HeightHitAccumulator();

        Assert.False(accumulator.Add("Floors", "Projet", 0.05));
        Assert.True(accumulator.Add("Floors", "Projet", 3.2));
        Assert.True(accumulator.Add("Ceilings", "Lien CVC", 2.6));
        Assert.True(accumulator.Add("Ceilings", "Lien CVC", 2.8));
        Assert.True(accumulator.Add("Floors", "Projet", 3.0));
        Assert.False(accumulator.Add("Roofs", "Projet", double.NaN));

        Assert.Equal(2.6, accumulator.MinM);
        Assert.Equal("Ceilings", accumulator.LimitingCategory);
        Assert.Equal("Lien CVC", accumulator.LimitingDocument);
        Assert.Equal([("Ceilings", 2.6), ("Floors", 3.0)], accumulator.ByCategoryM!.Select(p => (p.Key, p.Value)));
    }

    [Fact]
    public void Accumulator_is_empty_without_valid_hit()
    {
        var accumulator = new HeightHitAccumulator();
        accumulator.Add("Floors", "Projet", 0.0);

        Assert.Null(accumulator.MinM);
        Assert.Null(accumulator.LimitingCategory);
        Assert.Null(accumulator.ByCategoryM);
    }
}
