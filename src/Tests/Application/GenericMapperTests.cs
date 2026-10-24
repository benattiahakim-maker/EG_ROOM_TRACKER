using System.Text.Json;
using System.Text.Json.Nodes;
using EgRoomTracker.Application;
using EgRoomTracker.Application.Generic;
using EgRoomTracker.Contracts.Codes;
using EgRoomTracker.Contracts.Generic;
using EgRoomTracker.Contracts.Results;
using EgRoomTracker.Tests.Support;

namespace EgRoomTracker.Tests.Application;

/// <summary>Sortie generic : conversion pure de l'extraction en enveloppe commune de la plateforme.</summary>
public sealed class GenericMapperTests
{
    private static JsonNode Envelope(ExtractionResult result, GenericMapContext? context = null) =>
        JsonNode.Parse(GenericJson.Serialize(GenericMapper.Map(result, context)))!;

    private static JsonNode? Find(JsonNode envelope, string id) => GenericEnvelopeChecks.AllObjects(envelope).FirstOrDefault(o => o["id"]!.GetValue<string>() == id);

    private static JsonNode? Property(JsonNode obj, string key) => ((JsonArray)obj["properties"]!).FirstOrDefault(p => p!["key"]!.GetValue<string>() == key);

    private static IEnumerable<string> RefsOf(JsonNode obj, string key) =>
        ((JsonArray)obj["properties"]!).Where(p => p!["key"]!.GetValue<string>() == key && p["type"]!.GetValue<string>() == "ref").Select(p => p!["value"]!.GetValue<string>());

    [Fact]
    public void Envelope_RootAndSchema()
    {
        var envelope = Envelope(GenericFixtures.Result());

        Assert.Equal("1.0", envelope["contractVersion"]!.GetValue<string>());
        Assert.Equal("pieces-roomtracker", envelope["serviceId"]!.GetValue<string>());
        GenericEnvelopeChecks.AssertValid(envelope);
    }

    [Fact]
    public void DoorBetweenTwoRooms_IsOneObjectUnderTheFirstRoom_WithRefsToBothRooms()
    {
        var envelope = Envelope(GenericFixtures.Result());

        var doors = GenericEnvelopeChecks.AllObjects(envelope).Where(o => o["id"]!.GetValue<string>().StartsWith("element-door-uid-", StringComparison.Ordinal)).ToList();
        var door = Assert.Single(doors);
        var first = Find(envelope, "room-100")!;
        var second = Find(envelope, "room-101")!;

        Assert.Contains(first["children"]!.AsArray(), c => c!["id"]!.GetValue<string>() == "element-door-uid-5001");
        Assert.DoesNotContain(second["children"]!.AsArray(), c => c!["id"]!.GetValue<string>() == "element-door-uid-5001");
        Assert.Equal(["room-100", "room-101"], RefsOf(door!, "room"));
        Assert.Equal(["room-100"], RefsOf(door!, "room_from"));
        Assert.Equal(["room-101"], RefsOf(door!, "room_to"));
        Assert.Equal(["element-door-uid-5001"], RefsOf(second, "contains"));
        Assert.Empty(RefsOf(first, "contains"));
    }

    [Fact]
    public void Ids_AreUnique_AndRefsResolve()
    {
        var envelope = Envelope(GenericFixtures.Result(anomalies: [Rooms.RoomAnomaly(QualityFlagCodes.ExtractionError, "102"), Rooms.VolumeNotComputed(3)]));

        var ids = GenericEnvelopeChecks.AllObjects(envelope).Select(o => o["id"]!.GetValue<string>()).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.Empty(GenericEnvelopeChecks.StructureErrors(envelope));
    }

    [Fact]
    public void Objects_HaveNoKindNorCategoryField_CategoryIsAnOrdinaryProperty()
    {
        var envelope = Envelope(GenericFixtures.Result(anomalies: [Rooms.VolumeNotComputed(3)]));

        foreach (var obj in GenericEnvelopeChecks.AllObjects(envelope))
        {
            Assert.Equal(["id", "name", "children", "properties"], obj.AsObject().Select(p => p.Key).ToList());
        }

        Assert.Equal("Space", Property(Find(envelope, "room-100")!, "Category")!["value"]!.GetValue<string>());
        Assert.Equal("Door", Property(Find(envelope, "element-door-uid-5001")!, "Category")!["value"]!.GetValue<string>());
        Assert.Equal("cvc equipement", Property(Find(envelope, "element-equip-uid-6001")!, "Category")!["value"]!.GetValue<string>());
        Assert.Equal("Anomaly", Property(Find(envelope, "anomaly-1")!, "Category")!["value"]!.GetValue<string>());
    }

    [Fact]
    public void RoomProperties_AreTyped_WithUnits_AndNeverInvented()
    {
        var room = Property(Find(Envelope(GenericFixtures.Result()), "room-100")!, "surface")!;

        Assert.Equal("number", room["type"]!.GetValue<string>());
        Assert.Equal(18.425, room["value"]!.GetValue<double>());
        Assert.Equal("m²", room["unit"]!.GetValue<string>());

        var unknown = Find(Envelope(GenericFixtures.Result()), "room-102")!;
        Assert.Null(Property(unknown, "volume")); // volume non calculé : omis, jamais 0
        Assert.Null(Property(unknown, "box"));
    }

    [Fact]
    public void Volume_IsWrittenInCubicMetres_OnlyWhenComputed()
    {
        var result = GenericFixtures.Result();
        var computed = result with { Rooms = [result.Rooms[0] with { VolumeStatus = VolumeStatus.Computed, VolumeM3 = 49.7625 }, .. result.Rooms.Skip(1)] };

        var volume = Property(Find(Envelope(computed), "room-100")!, "volume")!;

        Assert.Equal(49.7625, volume["value"]!.GetValue<double>());
        Assert.Equal("m³", volume["unit"]!.GetValue<string>());
    }

    [Fact]
    public void Heights_AreNumbersInMetres()
    {
        var result = GenericFixtures.Result();
        var withHeights = result with
        {
            Rooms = [result.Rooms[0] with { Heights = new RoomHeightsDto(2.7, 2.55, 2.55, HeightSources.Measured, 12) { LimitingCategory = "Plafonds", LimitingDocument = "CVC lien" } }, .. result.Rooms.Skip(1)],
        };

        var room = Find(Envelope(withHeights), "room-100")!;

        Assert.Equal(2.55, Property(room, "hauteur")!["value"]!.GetValue<double>());
        Assert.Equal("m", Property(room, "hauteur")!["unit"]!.GetValue<string>());
        Assert.Equal(2.7, Property(room, "hauteur_renseignee")!["value"]!.GetValue<double>());
        Assert.Equal("Plafonds (CVC lien)", Property(room, "hauteur_element_limitant")!["value"]!.GetValue<string>());
    }

    [Fact]
    public void Groups_AreSetOnPropertiesAndWithinLimits()
    {
        var envelope = Envelope(GenericFixtures.Result());
        var door = Find(envelope, "element-door-uid-5001")!;

        Assert.Equal("Identification", Property(door, "viewerId")!["group"]!.GetValue<string>());
        Assert.Equal("Dimensions", Property(door, "largeur")!["group"]!.GetValue<string>());
        Assert.Equal("Paramètres d'occurrence", Property(door, "occurrence:Commentaires")!["group"]!.GetValue<string>());
        Assert.Equal("Paramètres de type", Property(door, "type:Largeur")!["group"]!.GetValue<string>());
        Assert.Equal("Relations", ((JsonArray)door["properties"]!).First(p => p!["key"]!.GetValue<string>() == "room")!["group"]!.GetValue<string>());
        Assert.All(GenericEnvelopeChecks.AllObjects(envelope).SelectMany(o => (JsonArray)o["properties"]!), p =>
        {
            var group = p!["group"]!.GetValue<string>();
            Assert.InRange(group.Length, 1, 120);
        });
    }

    [Fact]
    public void Parameters_AreTyped_NullsAreOmitted_AndLongValuesAreKeptWhole()
    {
        var longText = new string('x', 600);
        var equipment = GenericFixtures.Equipment("6001") with { Parameters = new Dictionary<string, object?> { ["Texte"] = longText, ["Actif"] = true, ["Puissance"] = 12.5, ["Nombre"] = 3, ["Gros"] = 9_000_000_000_000_000_000L, ["Vide"] = null } };
        var result = GenericFixtures.Result();
        var changed = result with { Rooms = [result.Rooms[0] with { Equipment = [equipment] }, .. result.Rooms.Skip(1)] };

        var obj = Find(Envelope(changed), "element-equip-uid-6001")!;

        Assert.Equal(longText, Property(obj, "occurrence:Texte")!["value"]!.GetValue<string>());
        Assert.Equal("boolean", Property(obj, "occurrence:Actif")!["type"]!.GetValue<string>());
        Assert.Equal(12.5, Property(obj, "occurrence:Puissance")!["value"]!.GetValue<double>());
        Assert.Equal("number", Property(obj, "occurrence:Nombre")!["type"]!.GetValue<string>());
        Assert.Null(Property(obj, "occurrence:Nombre")!["unit"]); // l'unité d'un paramètre n'est pas connue : omise
        Assert.Equal("text", Property(obj, "occurrence:Gros")!["type"]!.GetValue<string>()); // au-delà de 2^53 : texte, sans perte
        Assert.Null(Property(obj, "occurrence:Vide"));
        GenericEnvelopeChecks.AssertValid(Envelope(changed));
    }

    [Fact]
    public void SharedParameters_AreWrittenWhenTheyHaveAValue()
    {
        var result = GenericFixtures.Result();
        var room = result.Rooms[0] with
        {
            SharedParameters = new Dictionary<string, SharedParameterValue> { ["codeLocal"] = new(Rooms.CodeLocalGuid, "A-001"), ["vide"] = new(Rooms.CodeLocalGuid, null), ["nombre"] = new(Rooms.CodeLocalGuid, 4.5) },
        };

        var obj = Find(Envelope(result with { Rooms = [room, .. result.Rooms.Skip(1)] }), "room-100")!;

        Assert.Equal("A-001", Property(obj, "partage:codeLocal")!["value"]!.GetValue<string>());
        Assert.Equal(4.5, Property(obj, "partage:nombre")!["value"]!.GetValue<double>());
        Assert.Null(Property(obj, "partage:vide"));
    }

    [Fact]
    public void NamedParameters_AreIdentificationProperties()
    {
        var result = GenericFixtures.Result();
        var room = result.Rooms[0] with
        {
            NamedParameters = new Dictionary<string, string?> { ["SGP-DI-Ouvrage"] = "Bât A", ["SGP-DI-Typologie_Espace"] = null, ["SGP-DI-Local_Code"] = "L-12" },
        };

        var obj = Find(Envelope(result with { Rooms = [room, .. result.Rooms.Skip(1)] }), "room-100")!;

        Assert.Equal("Bât A", Property(obj, "ouvrage")!["value"]!.GetValue<string>());
        Assert.Null(Property(obj, "typologie_espace"));
        Assert.Equal("L-12", Property(obj, "local_code")!["value"]!.GetValue<string>());
    }

    [Fact]
    public void Box_ComesFromTheRoomBoundingBox_InMillimetres()
    {
        var box = Property(Find(Envelope(GenericFixtures.Result()), "room-100")!, "box")!;

        Assert.Equal("box", box["type"]!.GetValue<string>());
        Assert.Equal([0d, 0d, 0d], box["value"]!["min"]!.AsArray().Select(v => v!.GetValue<double>()));
        Assert.Equal([4300d, 4285d, 2700d], box["value"]!["max"]!.AsArray().Select(v => v!.GetValue<double>()));
    }

    [Fact]
    public void MissingOrInvalidBox_IsOmitted_WithAWarning_AndTheStatusIsPartial()
    {
        var result = GenericFixtures.Result(boxes: false);
        var invalid = result with { Rooms = [result.Rooms[0] with { BoundingBox = new BoundingBoxMm(10, 0, 0, 0, 0, 0) }, .. result.Rooms.Skip(1)] };

        foreach (var (input, expected) in new[] { (result, 3), (invalid, 3) })
        {
            var envelope = GenericMapper.Map(input);

            Assert.Equal("partial", envelope.Status);
            Assert.Contains(envelope.Warnings, w => w.StartsWith($"{expected} pièce(s) sans boîte englobante", StringComparison.Ordinal));
            Assert.All(envelope.Objects, o => Assert.DoesNotContain(o.Properties, p => p.Key == "box"));
        }
    }

    [Fact]
    public void RoomImage_IsAFileProperty_OnlyWhenTheFileExists_WithAnEncodedUrl()
    {
        var name = TrcNaming.RoomImageFileName("room-uid-100", GenericFixtures.CodeFile, 100);
        var context = new GenericMapContext { ImageFileNames = new HashSet<string> { name } };

        var envelope = Envelope(GenericFixtures.Result(), context);
        var image = Property(Find(envelope, "room-100")!, "image")!;

        Assert.Equal("file", image["type"]!.GetValue<string>());
        Assert.Equal(name, image["value"]!["name"]!.GetValue<string>());
        Assert.Equal("image/jpeg", image["value"]!["mimeType"]!.GetValue<string>());
        Assert.Equal($"/Images/{Uri.EscapeDataString(name)}", image["value"]!["url"]!.GetValue<string>());
        Assert.Null(Property(Find(envelope, "room-101")!, "image")); // pas d'image écrite : pas de propriété
        Assert.Null(Property(Find(Envelope(GenericFixtures.Result()), "room-100")!, "image"));
        GenericEnvelopeChecks.AssertValid(envelope);
    }

    [Fact]
    public void RoomImage_UrlIsEncoded_ForPlatformNamesWithSpacesAndPoundSign()
    {
        var result = GenericFixtures.Result();
        var source = result.Summary.Source! with { FileName = "8bfa£Projet test_V1.rvt" };
        var renamed = result with { Summary = result.Summary with { Source = source } };
        var name = TrcNaming.RoomImageFileName("room-uid-100", TrcNaming.CodeFile(source.FileName), 100);

        var envelope = Envelope(renamed, new GenericMapContext { ImageFileNames = new HashSet<string> { name } });
        var url = Property(Find(envelope, "room-100")!, "image")!["value"]!["url"]!.GetValue<string>();

        Assert.DoesNotContain(' ', url);
        Assert.DoesNotContain('£', url);
        Assert.StartsWith("/Images/", url);
        GenericEnvelopeChecks.AssertValid(envelope);
    }

    [Fact]
    public void ResultFiles_AreListedInAFilesObject()
    {
        var context = new GenericMapContext { ResultFiles = [new GenericFile("TRCValidationDict.json", "application/json", "/TRCValidationDict.json")] };

        var envelope = Envelope(GenericFixtures.Result(), context);
        var files = Find(envelope, "result-files")!;

        Assert.Equal("file", Property(files, "file:TRCValidationDict.json")!["type"]!.GetValue<string>());
        GenericEnvelopeChecks.AssertValid(envelope);
    }

    [Fact]
    public void Anomalies_RoomScopeAreChildrenOfTheRoom_DocumentScopeAreRootObjects()
    {
        var roomAnomaly = Rooms.RoomAnomaly(QualityFlagCodes.ExtractionError, "102", AnomalySeverity.Error);
        var envelope = Envelope(GenericFixtures.Result(anomalies: [roomAnomaly, Rooms.VolumeNotComputed(3)]));

        var inRoom = Assert.Single(Find(envelope, "room-102")!["children"]!.AsArray());
        Assert.Equal(QualityFlagCodes.ExtractionError, Property(inRoom!, "code")!["value"]!.GetValue<string>());
        Assert.Equal("error", Property(inRoom!, "severity")!["value"]!.GetValue<string>());
        Assert.Equal("room", Property(inRoom!, "scope")!["value"]!.GetValue<string>());
        Assert.Equal(roomAnomaly.Message, Property(inRoom!, "message")!["value"]!.GetValue<string>());

        var root = envelope["objects"]!.AsArray().Single(o => o!["id"]!.GetValue<string>() == "anomaly-2")!;
        Assert.Equal("document", Property(root, "scope")!["value"]!.GetValue<string>());
        Assert.Equal("info", Property(root, "severity")!["value"]!.GetValue<string>());
        Assert.Contains("roomsAffected", Property(root, "details")!["value"]!.GetValue<string>());
        GenericEnvelopeChecks.AssertValid(envelope);
    }

    [Fact]
    public void Status_DoneWithoutWarnings_PartialWithWarnings()
    {
        var withoutBox = GenericMapper.Map(GenericFixtures.Result(anomalies: [Rooms.VolumeNotComputed(3)])); // R102 sans boîte : avertissement
        Assert.Equal("partial", withoutBox.Status);

        var allBoxes = GenericFixtures.Result();
        var complete = allBoxes with { Rooms = [allBoxes.Rooms[0], allBoxes.Rooms[1], allBoxes.Rooms[2] with { BoundingBox = GenericFixtures.Box(9000) }], Anomalies = [Rooms.VolumeNotComputed(3)] };

        var info = GenericMapper.Map(complete);
        Assert.Equal("done", info.Status); // une anomalie info n'est pas un avertissement
        Assert.Empty(info.Warnings);

        var warned = GenericMapper.Map(complete with { Anomalies = [Rooms.RoomAnomaly(QualityFlagCodes.ExtractionError, "102", AnomalySeverity.Error), Rooms.RoomAnomaly(QualityFlagCodes.ExtractionError, "101", AnomalySeverity.Error)] });
        Assert.Equal("partial", warned.Status);
        Assert.Contains(warned.Warnings, w => w.StartsWith($"{QualityFlagCodes.ExtractionError} : 2 anomalies", StringComparison.Ordinal));
    }

    [Fact]
    public void SucceededWithWarnings_WithoutWarningAnomaly_StaysPartialWithAText()
    {
        var result = GenericFixtures.Result(ExtractionStatus.SucceededWithWarnings);
        var complete = result with { Rooms = [result.Rooms[0], result.Rooms[1], result.Rooms[2] with { BoundingBox = GenericFixtures.Box(9000) }] };

        var envelope = GenericMapper.Map(complete);

        Assert.Equal("partial", envelope.Status);
        Assert.NotEmpty(envelope.Warnings);
    }

    [Fact]
    public void FailedExtraction_HasNoEnvelope()
    {
        var failed = GenericFixtures.Result(ExtractionStatus.Failed);

        Assert.Throws<InvalidOperationException>(() => GenericMapper.Map(failed));
    }

    [Fact]
    public void LinkedElement_PointsToItsModelByKey_AndKeepsTheLinkIds()
    {
        var envelope = Envelope(GenericFixtures.Result());

        Assert.Equal(["main", "link-1"], envelope["models"]!.AsArray().Select(m => m!["key"]!.GetValue<string>()));
        Assert.Equal("CVC lien", envelope["models"]![1]!["title"]!.GetValue<string>());
        Assert.Equal("", envelope["models"]![1]!["urn"]!.GetValue<string>());
        var linked = Find(envelope, "element-equip-uid-6001")!;
        Assert.Equal("link-1", Property(linked, "modelKey")!["value"]!.GetValue<string>());
        Assert.Equal("equip-uid-6001", Property(linked, "viewerId")!["value"]!.GetValue<string>());
        Assert.Equal("text", Property(linked, "viewerId")!["type"]!.GetValue<string>());
        Assert.Equal("6001", Property(linked, "id_revit")!["value"]!.GetValue<string>());
        Assert.Equal("CVC lien", Property(linked, "lien")!["value"]!.GetValue<string>());
        Assert.True(Property(linked, "modele_lie")!["value"]!.GetValue<bool>());
        Assert.Equal("main", Property(Find(envelope, "room-100")!, "modelKey")!["value"]!.GetValue<string>());
        Assert.False(Property(Find(envelope, "element-door-uid-5001")!, "modele_lie")!["value"]!.GetValue<bool>());
    }

    [Fact]
    public void RoomIdentity_UniqueIdAndRevitIdAreOrdinaryProperties()
    {
        var room = Find(Envelope(GenericFixtures.Result()), "room-100")!;

        Assert.Equal("room-uid-100", Property(room, "viewerId")!["value"]!.GetValue<string>());
        Assert.Equal("room-uid-100", Property(room, "unique_id")!["value"]!.GetValue<string>());
        Assert.Equal("100", Property(room, "id_revit")!["value"]!.GetValue<string>());
        Assert.Equal("001 Bureau", room["name"]!.GetValue<string>());
    }

    [Fact]
    public void Output_IsDeterministic()
    {
        var context = new GenericMapContext();

        Assert.Equal(GenericJson.Serialize(GenericMapper.Map(GenericFixtures.Result(), context)), GenericJson.Serialize(GenericMapper.Map(GenericFixtures.Result(), context)));
    }

    [Fact]
    public void ElementWithTwoRoomsAndNoDoor_HasRoomRefsOnly()
    {
        var shared = GenericFixtures.Equipment("6002") with { Relation = ElementRoomRelation.In };
        var result = GenericFixtures.Result();
        var changed = result with { Rooms = [result.Rooms[0] with { Equipment = [shared] }, result.Rooms[1] with { Equipment = [shared] }, result.Rooms[2]] };

        var obj = Find(Envelope(changed), "element-equip-uid-6002")!;

        Assert.Equal(["room-100", "room-101"], RefsOf(obj, "room"));
        Assert.Empty(RefsOf(obj, "room_from"));
    }

    [Fact]
    public void Serialization_IsValidJson_WithAccentsKeptAsIs()
    {
        var json = GenericJson.Serialize(GenericMapper.Map(GenericFixtures.Result()));

        using var document = JsonDocument.Parse(json);
        Assert.Contains("Paramètres d'occurrence", json);
        Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
    }
}
