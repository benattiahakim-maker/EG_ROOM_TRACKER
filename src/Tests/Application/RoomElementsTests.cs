using System.Text.Json.Nodes;
using EgRoomTracker.Application;
using EgRoomTracker.Application.Revit;
using EgRoomTracker.Contracts.Codes;
using EgRoomTracker.Contracts.Configuration;
using EgRoomTracker.Contracts.Results;
using EgRoomTracker.Logging;
using EgRoomTracker.Tests.Support;

namespace EgRoomTracker.Tests.Application;

/// <summary>Paramètres complets des pièces et rattachement des portes, équipements et terminaux (schéma 1.4).</summary>
public sealed class RoomElementsTests
{
    // Pièces de la doublure : 415002 et 412345 (résultat trié : 412345 d'abord).
    private const long RoomA = 412345;
    private const long RoomB = 415002;

    private static RoomElementDto Door(string id) => new()
    {
        ElementId = id,
        UniqueId = $"u-{id}",
        Category = "Portes",
        Family = "Simple",
        Type = "0915 x 2134",
        Mark = $"P{id}",
        Level = "Niveau 1",
        WidthM = 0.915,
        HeightM = 2.134,
    };

    private static ElementPlacement Placement(long id, params RoomLink[] links) => new(id, Door(id.ToString()), links);

    private sealed class FakeElements(ElementKind kind, Func<bool, ElementCollection> collect) : IRoomElementService
    {
        public ElementKind Kind => kind;

        public bool? IncludeAllParameters { get; private set; }

        public IReadOnlyList<string>? RequestedCategories { get; private set; }

        public bool? IncludeLinks { get; private set; }

        public ElementCollection Collect(bool includeAllParameters, IReadOnlyList<string>? categories = null, bool includeLinks = true)
        {
            IncludeAllParameters = includeAllParameters;
            RequestedCategories = categories;
            IncludeLinks = includeLinks;
            return collect(includeAllParameters);
        }
    }

    private static Pipeline Create(ExtractionSettings settings, params IRoomElementService[] services) => new()
    {
        LoadSettings = () => settings,
        ElementServices = services,
    };

    private static ExtractionSettings Settings(ElementKind[] elements, RoomParametersMode parameters = RoomParametersMode.None) =>
        new() { SchemaVersion = "1.0", Elements = elements, RoomParameters = parameters };

    [Fact]
    public void Doors_are_attached_to_both_sides_and_those_without_room_are_reported()
    {
        var doors = new FakeElements(ElementKind.Doors, _ => new ElementCollection(
        [
            Placement(1, new RoomLink(RoomA, ElementRoomRelation.From), new RoomLink(RoomB, ElementRoomRelation.To)),
            Placement(2, new RoomLink(RoomB, ElementRoomRelation.From)),
            Placement(3),
            Placement(4, new RoomLink(999, ElementRoomRelation.To)),
        ], Unreadable: 1));
        var pipeline = Create(Settings([ElementKind.Doors]), doors);

        var result = pipeline.CreateService().Run(Pipeline.Request);

        var roomA = result.Rooms.Single(r => r.ElementId == RoomA.ToString());
        var roomB = result.Rooms.Single(r => r.ElementId == RoomB.ToString());
        Assert.Equal(["1"], roomA.Doors!.Select(d => d.ElementId));
        Assert.Equal(ElementRoomRelation.From, roomA.Doors![0].Relation);
        Assert.Equal(["1", "2"], roomB.Doors!.Select(d => d.ElementId));
        Assert.Equal([ElementRoomRelation.To, ElementRoomRelation.From], roomB.Doors!.Select(d => d.Relation));
        Assert.Equal(0.915, roomA.Doors![0].WidthM);

        var anomaly = Assert.Single(result.Anomalies, a => a.Code == DocumentAnomalyCodes.ElementWithoutRoom);
        Assert.Equal(AnomalySeverity.Info, anomaly.Severity);
        Assert.Equal(AnomalyScope.Document, anomaly.Scope);
        Assert.Equal(2, anomaly.Details["count"]);
        Assert.Equal(["3", "4"], (string[])anomaly.Details["elementIds"]!);
        Assert.Equal(1, anomaly.Details["unreadable"]);
        Assert.Contains(pipeline.Logs, l => l.EventCode == LogEventCodes.ElementsCollected);
    }

    [Fact]
    public void Only_requested_kinds_have_an_array_and_are_reported_in_options()
    {
        var doors = new FakeElements(ElementKind.Doors, _ => new ElementCollection([], 0));
        var terminals = new FakeElements(ElementKind.Terminals, _ => new ElementCollection([], 0));
        var pipeline = Create(Settings([ElementKind.Doors]), doors, terminals);

        var result = pipeline.CreateService().Run(Pipeline.Request);

        Assert.All(result.Rooms, r =>
        {
            Assert.Empty(r.Doors!);
            Assert.Null(r.Equipment);
            Assert.Null(r.Terminals);
            Assert.Null(r.Parameters);
        });
        Assert.Equal(["doors"], result.Summary.Options.Elements);
        Assert.Null(result.Summary.Options.RoomParameters);
        Assert.Null(terminals.IncludeAllParameters);
    }

    [Fact]
    public void Linked_models_are_included_by_default_and_the_switch_is_forwarded()
    {
        var equipment = new FakeElements(ElementKind.Equipment, _ => new ElementCollection([], 0));
        Create(Settings([ElementKind.Equipment]), equipment).CreateService().Run(Pipeline.Request);
        Assert.True(equipment.IncludeLinks);

        var off = new FakeElements(ElementKind.Equipment, _ => new ElementCollection([], 0));
        var pipeline = Create(Settings([ElementKind.Equipment]) with { LinkedElements = false }, off);
        var result = pipeline.CreateService().Run(Pipeline.Request);

        Assert.False(off.IncludeLinks);
        Assert.Equal(false, result.Summary.Options.LinkedElements);
    }

    [Fact]
    public void Elements_of_links_keep_their_discipline_and_link_name_and_are_attached_to_one_room()
    {
        var terminal = new RoomElementDto
        {
            ElementId = "88",
            UniqueId = "u-88",
            Category = "Air Terminals",
            Family = "Diffuseur",
            Type = "600x600",
            Level = "R+0",
            Discipline = ElementDisciplines.Cvc,
            LinkName = "CVC_Reseau",
        };
        var terminals = new FakeElements(ElementKind.Terminals, _ => new ElementCollection(
            [new ElementPlacement(88, terminal, [new RoomLink(RoomA, ElementRoomRelation.In)])], 0, ["OST_DuctTerminal"], null, ["Projet", "CVC_Reseau"]));
        var pipeline = Create(Settings([ElementKind.Terminals]), terminals);

        var result = pipeline.CreateService().Run(Pipeline.Request);
        var json = JsonNode.Parse(ContractJson.Serialize(result))!;

        var a = result.Rooms.Single(r => r.ElementId == RoomA.ToString());
        var placed = Assert.Single(a.Terminals!);
        Assert.Equal(("cvc", "CVC_Reseau"), (placed.Discipline, placed.LinkName));
        Assert.Empty(result.Rooms.Single(r => r.ElementId == RoomB.ToString()).Terminals!);
        var log = Assert.Single(pipeline.Logs, l => l.EventCode == LogEventCodes.ElementsCollected);
        Assert.Equal(["Projet", "CVC_Reseau"], (string[])log.Data!["sources"]!);
        ResultSchema.AssertValid(json);
    }

    [Fact]
    public void Main_model_elements_have_no_link_name()
    {
        var dto = Door("5") with { Discipline = ElementDisciplines.Electrical };
        var json = JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(dto, new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase }))!;

        Assert.Equal("electricite", json["discipline"]!.GetValue<string>());
        Assert.False(json.AsObject().ContainsKey("linkName"));
        Assert.Equal("in", json["relation"]!.GetValue<string>());
    }

    [Fact]
    public void Configured_categories_are_forwarded_only_to_their_family()
    {
        var doors = new FakeElements(ElementKind.Doors, _ => new ElementCollection([], 0));
        var terminals = new FakeElements(ElementKind.Terminals, _ => new ElementCollection([], 0));
        var settings = Settings([ElementKind.Doors, ElementKind.Terminals]) with
        {
            ElementCategories = new Dictionary<ElementKind, IReadOnlyList<string>>
            {
                [ElementKind.Terminals] = ["OST_PlumbingFixtures", "OST_Sprinklers"],
            },
        };

        Create(settings, doors, terminals).CreateService().Run(Pipeline.Request);

        Assert.Null(doors.RequestedCategories);
        Assert.Equal(["OST_PlumbingFixtures", "OST_Sprinklers"], terminals.RequestedCategories);
    }

    [Fact]
    public void Unknown_categories_are_reported_as_a_warning_and_categories_are_logged()
    {
        var terminals = new FakeElements(ElementKind.Terminals, _ => new ElementCollection([], 0, ["OST_Sprinklers"], ["OST_Inconnue"]));
        var pipeline = Create(Settings([ElementKind.Terminals]), terminals);

        var result = pipeline.CreateService().Run(Pipeline.Request);

        var anomaly = Assert.Single(result.Anomalies, a => a.Code == DocumentAnomalyCodes.ElementsCollectionFailed);
        Assert.Equal(AnomalySeverity.Warning, anomaly.Severity);
        Assert.Equal(["OST_Inconnue"], (string[])anomaly.Details["unknownCategories"]!);
        Assert.Empty(result.Summary.Errors);
        var log = Assert.Single(pipeline.Logs, l => l.EventCode == LogEventCodes.ElementsCollected);
        Assert.Equal(["OST_Sprinklers"], (string[])log.Data!["categories"]!);
    }

    [Fact]
    public void Failing_collection_is_a_warning_and_rooms_are_still_exported()
    {
        var equipment = new FakeElements(ElementKind.Equipment, _ => throw new InvalidOperationException("API Revit"));
        var pipeline = Create(Settings([ElementKind.Equipment]), equipment);

        var result = pipeline.CreateService().Run(Pipeline.Request);

        Assert.Empty(result.Summary.Errors);
        Assert.Equal(2, result.Rooms.Count);
        Assert.All(result.Rooms, r => Assert.Null(r.Equipment));
        var anomaly = Assert.Single(result.Anomalies, a => a.Code == DocumentAnomalyCodes.ElementsCollectionFailed);
        Assert.Equal(AnomalySeverity.Warning, anomaly.Severity);
        Assert.Equal("equipment", anomaly.Details["kind"]);
        Assert.Contains(pipeline.Logs, l => l.EventCode == LogEventCodes.ElementsCollectionFailed && l.Level == LogLevel.Warning);
    }

    [Fact]
    public void Requested_kind_without_service_is_reported_not_fatal()
    {
        var pipeline = Create(Settings([ElementKind.Terminals]));

        var result = pipeline.CreateService().Run(Pipeline.Request);

        Assert.Empty(result.Summary.Errors);
        Assert.Contains(result.Anomalies, a => a.Code == DocumentAnomalyCodes.ElementsCollectionFailed && (string?)a.Details["kind"] == "terminals");
    }

    [Fact]
    public void All_parameters_are_attached_to_rooms_and_forwarded_to_element_services()
    {
        var doors = new FakeElements(ElementKind.Doors, _ => new ElementCollection([], 0));
        var pipeline = Create(Settings([ElementKind.Doors], RoomParametersMode.All), doors);
        pipeline.AllParameters = id => new Dictionary<string, object?> { ["Commentaires"] = $"pièce {id}" };

        var result = pipeline.CreateService().Run(Pipeline.Request);

        Assert.All(result.Rooms, r =>
        {
            var parameter = Assert.Single(r.Parameters!);
            Assert.Equal("Commentaires", parameter.Key);
            Assert.Equal($"pièce {r.ElementId}", parameter.Value);
        });
        Assert.Equal("all", result.Summary.Options.RoomParameters);
        Assert.True(doors.IncludeAllParameters);
    }

    [Fact]
    public void Parameters_are_flat_properties_like_area_and_volume_in_the_json()
    {
        var pipeline = Create(Settings([], RoomParametersMode.All));
        pipeline.AllParameters = _ => new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["Commentaires"] = "x",
            ["Hauteur"] = 2.44,
            ["Occupant"] = null,
        };

        var result = pipeline.CreateService().Run(Pipeline.Request);
        var room = JsonNode.Parse(ContractJson.Serialize(result))!["rooms"]![0]!.AsObject();

        var parameters = room["parametres"]!.AsObject();
        Assert.Equal(["Commentaires", "Hauteur", "Occupant"], parameters.Select(p => p.Key));
        Assert.Equal("x", parameters["Commentaires"]!.GetValue<string>());
        Assert.Equal(2.44, parameters["Hauteur"]!.GetValue<double>());
        Assert.Null(parameters["Occupant"]);
        Assert.IsType<JsonObject>(room["parametres"]);
        Assert.True(room.ContainsKey("surface_m2") && room.ContainsKey("volume_m3"));
    }

    [Fact]
    public void Failing_all_parameters_keeps_the_room_contract_fields()
    {
        var pipeline = Create(Settings([], RoomParametersMode.All));
        pipeline.AllParameters = _ => throw new InvalidOperationException("paramètre illisible");

        var result = pipeline.CreateService().Run(Pipeline.Request);

        Assert.Equal(2, result.Rooms.Count);
        Assert.All(result.Rooms, r =>
        {
            Assert.Null(r.Parameters);
            Assert.DoesNotContain(QualityFlagCodes.ExtractionError, r.QualityFlags);
        });
        Assert.Equal(2, pipeline.Logs.Count(l => l.EventCode == LogEventCodes.RoomParametersFailed));
    }

    [Fact]
    public void Default_output_has_none_of_the_new_properties()
    {
        var pipeline = Create(Settings([]));

        var result = pipeline.CreateService().Run(Pipeline.Request);
        var json = JsonNode.Parse(ContractJson.Serialize(result))!;

        var room = json["rooms"]![0]!.AsObject();
        foreach (var key in new[] { "parameters", "doors", "equipment", "terminals" })
        {
            Assert.False(room.ContainsKey(key), key);
        }

        var options = json["summary"]!["options"]!.AsObject();
        Assert.False(options.ContainsKey("roomParameters"));
        Assert.False(options.ContainsKey("elements"));
        ResultSchema.AssertValid(json);
    }

    [Fact]
    public void Contract_WithParametersAndElements_IsSchemaValid()
    {
        var doors = new FakeElements(ElementKind.Doors, _ => new ElementCollection(
        [
            Placement(1, new RoomLink(RoomA, ElementRoomRelation.From), new RoomLink(RoomB, ElementRoomRelation.To)),
            Placement(3),
        ], 0));
        var equipment = new FakeElements(ElementKind.Equipment, _ => new ElementCollection(
        [
            new ElementPlacement(7, Door("7") with { Category = "Équipements mécaniques", WidthM = null, HeightM = null, Mark = null, Parameters = new Dictionary<string, object?>() },
                [new RoomLink(RoomA, ElementRoomRelation.In)]),
        ], 0));
        var pipeline = Create(Settings([ElementKind.Doors, ElementKind.Equipment, ElementKind.Terminals], RoomParametersMode.All), doors, equipment);
        pipeline.AllParameters = _ => new Dictionary<string, object?>
        {
            ["Surface"] = 12.87,
            ["Hauteur limite"] = 2.4384,
            ["Angle"] = "90°",
            ["Niveau"] = "311",
            ["Numéro"] = 12L,
            ["Vide"] = null,
        };
        pipeline.ElementServices = [doors, equipment, new FakeElements(ElementKind.Terminals, _ => new ElementCollection([], 0))];

        var result = pipeline.CreateService().Run(Pipeline.Request);
        var json = JsonNode.Parse(ContractJson.Serialize(result))!;

        Assert.Equal("1.4", json["schemaVersion"]!.GetValue<string>());
        Assert.Equal(["doors", "equipment", "terminals"], json["summary"]!["options"]!["elements"]!.AsArray().Select(n => n!.GetValue<string>()));
        ResultSchema.AssertValid(json);
    }
}
