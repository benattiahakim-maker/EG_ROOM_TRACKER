using System.Text.Json.Nodes;
using EgRoomTracker.Application.Generic;
using EgRoomTracker.Contracts.Results;
using EgRoomTracker.Tests.Support;

namespace EgRoomTracker.Tests.Application;

/// <summary>Sortie generic : groupes « Portes », « Équipements », « Terminaux » dans chaque pièce.</summary>
public sealed class GenericGroupTests
{
    private static JsonNode Envelope(ExtractionResult result) => JsonNode.Parse(GenericJson.Serialize(GenericMapper.Map(result)))!;

    private static JsonNode Find(JsonNode envelope, string id) => GenericEnvelopeChecks.AllObjects(envelope).First(o => o["id"]!.GetValue<string>() == id);

    private static string Id(JsonNode? node) => node!["id"]!.GetValue<string>();

    private static IEnumerable<string> ChildIds(JsonNode node) => ((JsonArray)node["children"]!).Select(Id);

    private static string Prop(JsonNode node, string key) => node["properties"]!.AsArray().Single(p => p!["key"]!.GetValue<string>() == key)!["value"]!.GetValue<string>();

    private static RoomElementDto Item(string id, string family, string relation = ElementRoomRelation.In, string? discipline = "cvc") =>
        GenericFixtures.Equipment(id) with { Family = family, Type = null, Relation = relation, Discipline = discipline };

    private static ExtractionResult With(RoomDto room, IReadOnlyList<AnomalyDto>? anomalies = null)
    {
        var result = GenericFixtures.Result(anomalies: anomalies);
        return result with { Rooms = [room], Summary = result.Summary with { Counts = new RoomCounts(1, 1, 0) } };
    }

    private static RoomDto Room(IReadOnlyList<RoomElementDto>? doors, IReadOnlyList<RoomElementDto>? equipment, IReadOnlyList<RoomElementDto>? terminals) =>
        GenericFixtures.Room(100, "1", null, doors, equipment) with { Terminals = terminals };

    [Fact]
    public void RoomChildren_AreTheGroupsInOrder_ThenTheRoomAnomalies()
    {
        var result = With(
            Room([Item("1", "Porte", ElementRoomRelation.From)], [Item("2", "CTA")], [Item("3", "Diffuseur")]),
            [Rooms.RoomAnomaly("EXTRACTION_ERROR", "100")]);

        var room = Find(Envelope(result), "room-100");

        Assert.Equal(["room-100-portes", "room-100-equipements", "room-100-terminaux", "anomaly-1"], ChildIds(room));
    }

    [Fact]
    public void Group_IsAGroupingObject_WithNameChildrenAndCategoryGroup()
    {
        var envelope = Envelope(With(Room([Item("1", "Porte", ElementRoomRelation.From)], null, null)));

        var group = Find(envelope, "room-100-portes");

        Assert.Equal("Portes", group["name"]!.GetValue<string>());
        Assert.Equal("Group", Prop(group, "Category"));
        Assert.Equal("Portes", Prop(group, "categorie_libelle"));
        Assert.Equal(["element-equip-uid-1"], ChildIds(group));
        Assert.Equal(["Category", "categorie_libelle"], group["properties"]!.AsArray().Select(p => p!["key"]!.GetValue<string>()));
        GenericEnvelopeChecks.AssertValid(envelope);
    }

    [Fact]
    public void EmptyGroups_AreOmitted()
    {
        var envelope = Envelope(With(Room(null, [Item("2", "CTA")], null)));

        Assert.Equal(["room-100-equipements"], ChildIds(Find(envelope, "room-100")));
        Assert.DoesNotContain(GenericEnvelopeChecks.AllObjects(envelope), o => Id(o).EndsWith("-portes", StringComparison.Ordinal) || Id(o).EndsWith("-terminaux", StringComparison.Ordinal));
    }

    [Fact]
    public void RoomWithoutElements_HasNoGroup()
    {
        var envelope = Envelope(With(Room(null, null, null)));

        Assert.Empty(ChildIds(Find(envelope, "room-100")));
    }

    [Fact]
    public void GroupChildren_AreSortedByNameIgnoringCase_ThenByUniqueId()
    {
        var items = new[] { Item("9", "cta"), Item("5", "Zone"), Item("3", "CTA"), Item("1", "Batterie") };

        var envelope = Envelope(With(Room(null, items, null)));

        Assert.Equal(["element-equip-uid-1", "element-equip-uid-3", "element-equip-uid-9", "element-equip-uid-5"], ChildIds(Find(envelope, "room-100-equipements")));
    }

    [Fact]
    public void GroupChildren_OrderDoesNotDependOnTheInputOrder()
    {
        var items = new[] { Item("9", "cta"), Item("3", "CTA"), Item("1", "Batterie") };

        var a = GenericJson.Serialize(GenericMapper.Map(With(Room(null, items, null))));
        var b = GenericJson.Serialize(GenericMapper.Map(With(Room(null, Enumerable.Reverse(items).ToArray(), null))));

        Assert.Equal(a, b);
    }

    [Fact]
    public void ElementRefs_StillPointToRooms_AndIdsStayUnique()
    {
        var envelope = Envelope(GenericFixtures.Result());

        GenericEnvelopeChecks.AssertValid(envelope);
        Assert.Equal("Group", Prop(Find(envelope, "room-100-portes"), "Category"));
    }
}
