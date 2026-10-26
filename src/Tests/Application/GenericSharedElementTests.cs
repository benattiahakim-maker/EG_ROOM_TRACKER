using System.Text.Json.Nodes;
using EgRoomTracker.Application.Generic;
using EgRoomTracker.Contracts.Generic;
using EgRoomTracker.Contracts.Results;
using EgRoomTracker.Tests.Support;

namespace EgRoomTracker.Tests.Application;

/// <summary>Éléments répétés sous chaque pièce : garde-fou de taille, collisions d'id, ordre stable des refs.</summary>
public sealed class GenericSharedElementTests
{
    private static string Id(JsonNode? node) => node!["id"]!.GetValue<string>();

    private static IEnumerable<string> Refs(JsonNode obj, string key) =>
        obj["properties"]!.AsArray().Where(p => p!["key"]!.GetValue<string>() == key).Select(p => p!["value"]!.GetValue<string>());

    private static RoomElementDto Equipment(string id) => GenericFixtures.Equipment(id);

    private static ExtractionResult Result(IEnumerable<RoomDto> rooms)
    {
        var list = rooms.ToList();
        var result = GenericFixtures.Result();
        return result with { Rooms = list, Anomalies = [], Summary = result.Summary with { Counts = new RoomCounts(list.Count, list.Count, 0) } };
    }

    private static RoomDto Room(int id, params RoomElementDto[] equipment) =>
        GenericFixtures.Room(id, id.ToString(System.Globalization.CultureInfo.InvariantCulture), GenericFixtures.Box(id * 5000.0), null, equipment);

    private static ExtractionResult SharedBy(int rooms, params string[] elementIds) =>
        Result(Enumerable.Range(1, rooms).Select(i => Room(i, [.. elementIds.Select(Equipment)])));

    private static GenericWarning? ManyRooms(GenericEnvelope envelope) => envelope.Warnings.SingleOrDefault(w => w.Code == "SHARED_ELEMENT_MANY_ROOMS");

    [Fact]
    public void ElementInMoreThanTwentyRooms_GivesOneInfo_WithCodeCountAndThreshold_AndRepetitionsAreNotLimited()
    {
        var envelope = GenericMapper.Map(SharedBy(21, "1"));
        var json = JsonNode.Parse(GenericJson.Serialize(envelope))!;

        var info = ManyRooms(envelope)!;
        Assert.Equal("info", info.Severity);
        Assert.Contains("SHARED_ELEMENT_MANY_ROOMS", info.Message, StringComparison.Ordinal);
        Assert.Contains("1 élément(s)", info.Message, StringComparison.Ordinal);
        Assert.Contains("20 pièces par élément", info.Message, StringComparison.Ordinal);
        Assert.True(info.Message.Length <= GenericWarning.MaxMessageLength);
        Assert.Equal(21, GenericEnvelopeChecks.AllObjects(json).Count(o => Id(o) == "element-equip-uid-1"));
        Assert.Equal("done", envelope.Status); // une information ne rend jamais l'enveloppe partial
        GenericEnvelopeChecks.AssertValid(json);
    }

    [Fact]
    public void ElementInExactlyTwentyRooms_GivesNoInfo() =>
        Assert.Null(ManyRooms(GenericMapper.Map(SharedBy(20, "1"))));

    [Fact]
    public void SeveralElementsOverTheThreshold_StillGiveASingleInfo_WithTheirCount()
    {
        var envelope = GenericMapper.Map(SharedBy(25, "1", "2", "3"));

        Assert.Single(envelope.Warnings, w => w.Code == "SHARED_ELEMENT_MANY_ROOMS");
        Assert.Contains("3 élément(s)", ManyRooms(envelope)!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RoomsWithTheSameElementId_GetSuffixedIds_AndEachCopyKeepsItsRefs()
    {
        var shared = Equipment("1");
        var rooms = new[] { 3, 2, 1 }.Select(i => Room(100, shared) with { UniqueId = $"room-uid-dup-{i}", Number = i.ToString(System.Globalization.CultureInfo.InvariantCulture) }).ToList();

        var envelope = JsonNode.Parse(GenericJson.Serialize(GenericMapper.Map(Result(rooms))))!;

        var roomIds = GenericEnvelopeChecks.AllObjects(envelope).Select(Id).Where(id => System.Text.RegularExpressions.Regex.IsMatch(id, "^room-100(-[0-9]+)?$")).ToList();
        Assert.Equal(["room-100", "room-100-2", "room-100-3"], roomIds.OrderBy(i => i, StringComparer.Ordinal));
        var copy = GenericEnvelopeChecks.AllObjects(envelope).First(o => Id(o) == "element-equip-uid-1");
        Assert.Equal(["room-100", "room-100-2", "room-100-3"], Refs(copy, "room").OrderBy(i => i, StringComparer.Ordinal));
        Assert.Contains("room-100-3-equipements", GenericEnvelopeChecks.AllObjects(envelope).Select(Id));
        GenericEnvelopeChecks.AssertValid(envelope);
    }

    [Fact]
    public void RoomRefsOfASharedElement_AreInNaturalRoomIdOrder_WhateverTheInputOrder()
    {
        var shared = Equipment("1");
        var forward = Result(new[] { 2, 10, 1, 3 }.Select(i => Room(i, shared)));
        var backward = Result(new[] { 3, 1, 10, 2 }.Select(i => Room(i, shared)));

        var a = JsonNode.Parse(GenericJson.Serialize(GenericMapper.Map(forward)))!;
        var b = JsonNode.Parse(GenericJson.Serialize(GenericMapper.Map(backward)))!;

        var copy = GenericEnvelopeChecks.AllObjects(a).First(o => Id(o) == "element-equip-uid-1");
        Assert.Equal(["room-1", "room-2", "room-3", "room-10"], Refs(copy, "room"));
        Assert.Equal(copy.ToJsonString(), GenericEnvelopeChecks.AllObjects(b).First(o => Id(o) == "element-equip-uid-1").ToJsonString());
    }

    [Fact]
    public void DoorSides_AreInTheSameStableOrder()
    {
        var fromDoor = GenericFixtures.Door("1", ElementRoomRelation.From);
        var toDoor = fromDoor with { Relation = ElementRoomRelation.To };
        var result = Result([GenericFixtures.Room(10, "10", GenericFixtures.Box(), [toDoor], null), GenericFixtures.Room(2, "2", GenericFixtures.Box(9000), [fromDoor], null)]);

        var envelope = JsonNode.Parse(GenericJson.Serialize(GenericMapper.Map(result)))!;

        var door = GenericEnvelopeChecks.AllObjects(envelope).First(o => Id(o) == "element-door-uid-1");
        Assert.Equal(["room-2", "room-10"], Refs(door, "room"));
        Assert.Equal(["room-2"], Refs(door, "room_from"));
        Assert.Equal(["room-10"], Refs(door, "room_to"));
    }
}
