using System.Text.Json.Nodes;
using EgRoomTracker.Application.Generic;
using EgRoomTracker.Contracts.Results;
using EgRoomTracker.Tests.Support;

namespace EgRoomTracker.Tests.Application;

/// <summary>Sortie generic : objets « Niveau » à la racine (altitude croissante), locaux dessous en tri naturel du numéro.</summary>
public sealed class GenericLevelTests
{
    private static JsonNode Envelope(ExtractionResult result) => JsonNode.Parse(GenericJson.Serialize(GenericMapper.Map(result)))!;

    private static string Id(JsonNode? node) => node!["id"]!.GetValue<string>();

    private static IEnumerable<string> ChildIds(JsonNode node) => ((JsonArray)node["children"]!).Select(Id);

    private static RoomDto Room(long id, string number, LevelRef? level) => GenericFixtures.Room(id, number, null) with { Level = level };

    private static LevelRef Level(string id, string name, double? elevationM) => new(id, $"lvl-{id}", name) { ElevationM = elevationM };

    private static ExtractionResult With(params RoomDto[] rooms)
    {
        var result = GenericFixtures.Result();
        return result with { Rooms = rooms, Summary = result.Summary with { Counts = new RoomCounts(rooms.Length, rooms.Length, 0) } };
    }

    [Theory]
    [InlineData("2", "10", -1)]
    [InlineData("10", "2", 1)]
    [InlineData("a2", "A10", -1)]
    [InlineData("R1-02", "R1-2", 0)]
    [InlineData("101", "101B", -1)]
    [InlineData("A", "a", 0)]
    [InlineData("", "1", 1)]
    [InlineData(null, "1", 1)]
    [InlineData("1", "", -1)]
    [InlineData("", null, 0)]
    public void NaturalOrder_ComparesDigitRunsAsNumbers_IgnoringCase_AndPutsEmptyLast(string? x, string? y, int expected) =>
        Assert.Equal(expected, Math.Sign(NaturalOrder.Instance.Compare(x, y)));

    [Fact]
    public void Levels_AreTheRoots_SortedByElevation_WithTheNoLevelObjectLast()
    {
        var result = With(
            Room(1, "1", Level("30", "R+2", 6.0)),
            Room(2, "2", null),
            Room(3, "3", Level("10", "RDC", 0.0)),
            Room(4, "4", Level("20", "R+1", 3.05)),
            Room(5, "5", Level("40", "Inconnu", null)),
            Room(6, "6", Level("5", "SS1", -3.0)));

        var roots = Envelope(result)["objects"]!.AsArray();

        Assert.Equal(["Niveau", "Niveau", "Niveau", "Niveau", "Niveau", "Niveau"], roots.Select(o => o!["properties"]!.AsArray().Single(p => p!["key"]!.GetValue<string>() == "categorie_libelle")!["value"]!.GetValue<string>()));
        Assert.Equal(["SS1", "RDC", "R+1", "R+2", "Inconnu", "Sans niveau"], roots.Select(o => o!["name"]!.GetValue<string>()));
        Assert.Equal("level-none", Id(roots[5]));
        Assert.Equal(["room-6"], ChildIds(roots[0]!));
    }

    [Fact]
    public void Rooms_UnderALevel_AreInNaturalNumberOrder_TiedByElementId()
    {
        var level = Level("10", "RDC", 0.0);
        var result = With(
            Room(9, "10", level),
            Room(8, "2", level),
            Room(7, "B1", level),
            Room(6, "b01", level),
            Room(5, "1", level),
            Room(4, "", level),
            Room(3, "2", level));

        var root = Envelope(result)["objects"]!.AsArray().Single()!;

        Assert.Equal(["room-5", "room-3", "room-8", "room-9", "room-6", "room-7", "room-4"], ChildIds(root));
    }

    [Fact]
    public void LevelObject_CarriesCategoryLabelRevitIdsAndAltitudeInMeters()
    {
        var result = With(Room(1, "1", Level("311", "R+0", 3.0504)));

        var level = Envelope(result)["objects"]!.AsArray().Single()!;
        var props = level["properties"]!.AsArray().ToDictionary(p => p!["key"]!.GetValue<string>(), p => p!);

        Assert.Equal("level-lvl-311", Id(level));
        Assert.Equal("Level", props["Category"]["value"]!.GetValue<string>());
        Assert.Equal("Niveau", props["categorie_libelle"]["value"]!.GetValue<string>());
        Assert.Equal("lvl-311", props["unique_id"]["value"]!.GetValue<string>());
        Assert.Equal("311", props["id_revit"]["value"]!.GetValue<string>());
        Assert.Equal(3.05, props["altitude"]["value"]!.GetValue<double>());
        Assert.Equal("m", props["altitude"]["unit"]!.GetValue<string>());
        Assert.DoesNotContain("viewerId", props.Keys);
        GenericEnvelopeChecks.AssertValid(Envelope(result));
    }

    [Fact]
    public void RoomsWithoutLevel_AreUnderASingleNoLevelObject_WithoutAltitude()
    {
        var result = With(Room(1, "1", null), Room(2, "2", null));

        var level = Envelope(result)["objects"]!.AsArray().Single()!;

        Assert.Equal("Sans niveau", level["name"]!.GetValue<string>());
        Assert.Equal(["room-1", "room-2"], ChildIds(level));
        Assert.DoesNotContain(level["properties"]!.AsArray(), p => p!["key"]!.GetValue<string>() == "altitude");
    }

    [Fact]
    public void ARoomIsListedOnce_AndRefsStillResolve()
    {
        var result = GenericFixtures.Result();

        var envelope = Envelope(result);

        GenericEnvelopeChecks.AssertValid(envelope);
        Assert.Equal(3, GenericEnvelopeChecks.AllObjects(envelope).Count(o => System.Text.RegularExpressions.Regex.IsMatch(Id(o), "^room-[0-9]+$")));
        Assert.Equal("Niveau", envelope["objects"]!.AsArray().First()!["properties"]!.AsArray().Single(p => p!["key"]!.GetValue<string>() == "categorie_libelle")!["value"]!.GetValue<string>());
    }
}
