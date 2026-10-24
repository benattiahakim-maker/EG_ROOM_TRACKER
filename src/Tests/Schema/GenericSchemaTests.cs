using System.Text.Json.Nodes;
using EgRoomTracker.Application.Generic;
using EgRoomTracker.Contracts.Codes;
using EgRoomTracker.Contracts.Generic;
using EgRoomTracker.Contracts.Results;
using EgRoomTracker.Tests.Support;

namespace EgRoomTracker.Tests.Schema;

/// <summary>
/// Schéma de <c>result.generic.json</c> (<c>docs/schema/generic.schema.json</c>) et exemple de référence : l'exemple est conforme et
/// identique à la sortie du mapper ; les écarts à l'enveloppe de la plateforme sont rejetés.
/// </summary>
public class GenericSchemaTests
{
    private const string ExampleFile = "generic.example.json";

    /// <summary>Résultat de référence de l'exemple : images comprises, avec un avertissement et une anomalie de chaque portée.</summary>
    private static string ExampleJson()
    {
        var result = GenericFixtures.Result(anomalies: [Rooms.RoomAnomaly(QualityFlagCodes.ExtractionError, "102", AnomalySeverity.Warning), Rooms.VolumeNotComputed(3)]);
        var images = new HashSet<string> { EgRoomTracker.Application.TrcNaming.RoomImageFileName("room-uid-100", GenericFixtures.CodeFile, 100) };
        var context = new GenericMapContext
        {
            ImageFileNames = images,
            ResultFiles = [new GenericFile("TRCValidationDict.json", "application/json", "/TRCValidationDict.json")],
        };
        return GenericJson.Serialize(GenericMapper.Map(result, context));
    }

    private static JsonNode Example() => JsonNode.Parse(File.ReadAllText(ResultSchema.DataPath(ExampleFile)))!;

    [Fact]
    public void Example_IsValid() => GenericEnvelopeChecks.AssertValid(Example());

    /// <summary>Régénérer l'exemple : <c>UPDATE_GENERIC_EXAMPLE=1 dotnet test --filter Example_MatchesMapperOutput</c>.</summary>
    [Fact]
    public void Example_MatchesMapperOutput()
    {
        var expected = ExampleJson();
        if (Environment.GetEnvironmentVariable("UPDATE_GENERIC_EXAMPLE") == "1")
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "EgRoomTracker.sln")))
            {
                directory = directory.Parent;
            }

            File.WriteAllText(Path.Combine(directory!.FullName, "docs", "examples", ExampleFile), expected + "\n", new System.Text.UTF8Encoding(false));
        }

        Assert.Equal(expected.ReplaceLineEndings("\n").TrimEnd(), File.ReadAllText(ResultSchema.DataPath(ExampleFile)).ReplaceLineEndings("\n").TrimEnd());
    }

    public static TheoryData<string, Action<JsonNode>> InvalidMutations => new()
    {
        { "contractVersion inconnue", e => e["contractVersion"] = "2.0" },
        { "serviceId autre", e => e["serviceId"] = "inventaire-cvc" },
        { "propriété racine inconnue", e => e["kind"] = "x" },
        { "status failed", e => e["status"] = "failed" },
        { "partial sans avertissement", e => { e["status"] = "partial"; e["warnings"] = new JsonArray(); } },
        { "partial avec seulement des informations", e => { e["status"] = "partial"; e["warnings"] = new JsonArray(new JsonObject { ["severity"] = "info", ["message"] = "x", ["code"] = "A" }); } },
        { "information sans message", e => e["warnings"] = new JsonArray(new JsonObject { ["severity"] = "info", ["code"] = "A" }) },
        { "gravité inconnue", e => e["warnings"] = new JsonArray(new JsonObject { ["severity"] = "error", ["message"] = "x" }) },
        { "code d'information invalide", e => e["warnings"] = new JsonArray(new JsonObject { ["severity"] = "info", ["message"] = "x", ["code"] = "a b" }) },
        { "urn absent", e => e["models"]![0]!.AsObject().Remove("urn") },
        { "champ kind sur un objet", e => e["objects"]![0]!["kind"] = "room" },
        { "champ category sur un objet", e => e["objects"]![0]!["category"] = "Space" },
        { "id vide", e => e["objects"]![0]!["id"] = "" },
        { "unité sur un texte", e => P(e, "numero")["unit"] = "m" },
        { "unité vide sur un nombre", e => P(e, "surface")["unit"] = "" },
        { "groupe vide", e => P(e, "numero")["group"] = "" },
        { "groupe de 121 caractères", e => P(e, "numero")["group"] = new string('g', 121) },
        { "type inconnu", e => P(e, "numero")["type"] = "string" },
        { "nombre en texte", e => P(e, "surface")["value"] = "18.4" },
        { "texte en nombre", e => P(e, "numero")["value"] = 1 },
        { "booléen en texte", e => P(e, "modele_lie", "element-door-uid-5001")["value"] = "true" },
        { "date non ISO 8601", e => { var p = P(e, "numero"); p["type"] = "date"; p["value"] = "09/10/2026"; } },
        { "box sans max", e => P(e, "box")["value"]!.AsObject().Remove("max") },
        { "box à deux coordonnées", e => P(e, "box")["value"]!["min"] = new JsonArray(0, 0) },
        { "ref numérique", e => P(e, "contains", "room-101")["value"] = 5 },
        { "fichier sans mimeType", e => P(e, "image")["value"]!.AsObject().Remove("mimeType") },
        { "fichier mimeType invalide", e => P(e, "image")["value"]!["mimeType"] = "jpeg" },
        { "fichier url http", e => P(e, "image")["value"]!["url"] = "http://exemple.fr/a.jpg" },
        { "fichier url javascript", e => P(e, "image")["value"]!["url"] = "javascript:alert(1)" },
        { "fichier url //hôte", e => P(e, "image")["value"]!["url"] = "//exemple.fr/a.jpg" },
        { "fichier url avec espace", e => P(e, "image")["value"]!["url"] = "/Images/a b.jpg" },
    };

    [Theory]
    [MemberData(nameof(InvalidMutations))]
    public void Schema_RejectsDeviationsFromTheContract(string name, Action<JsonNode> mutate)
    {
        var envelope = Example();
        mutate(envelope);

        Assert.True(GenericEnvelopeChecks.SchemaErrors(envelope).Count > 0, $"{name} : instance acceptée à tort.");
    }

    [Fact]
    public void Schema_AcceptsInformationObjects_AndPartialNeedsAWarning()
    {
        var info = new JsonObject { ["severity"] = "info", ["message"] = "2 lien(s) absent(s) : a, b", ["code"] = "LINK_MISSING" };

        var done = Example();
        done["status"] = "done";
        done["warnings"] = new JsonArray(info.DeepClone());
        Assert.Empty(GenericEnvelopeChecks.SchemaErrors(done));

        var partialWithText = Example();
        partialWithText["status"] = "partial";
        partialWithText["warnings"] = new JsonArray("Pièce sans boîte", info.DeepClone());
        Assert.Empty(GenericEnvelopeChecks.SchemaErrors(partialWithText));

        var partialWithObjectWarning = Example();
        partialWithObjectWarning["status"] = "partial";
        partialWithObjectWarning["warnings"] = new JsonArray(new JsonObject { ["severity"] = "warning", ["message"] = "x" });
        Assert.Empty(GenericEnvelopeChecks.SchemaErrors(partialWithObjectWarning));
    }

    [Fact]
    public void Schema_AcceptsAFileWithAnHttpsUrl()
    {
        var envelope = Example();
        P(envelope, "image")["value"]!["url"] = "https://exemple.fr/Images/a.jpg";

        Assert.Empty(GenericEnvelopeChecks.SchemaErrors(envelope));
    }

    [Fact]
    public void Structure_RejectsDuplicateIds_UnresolvedRefs_DuplicateKeys_AndUnknownModelKeys()
    {
        var duplicateId = Example();
        duplicateId["objects"]![1]!["id"] = duplicateId["objects"]![0]!["id"]!.GetValue<string>();
        Assert.NotEmpty(GenericEnvelopeChecks.StructureErrors(duplicateId));

        var unresolved = Example();
        P(unresolved, "contains", "room-101")["value"] = "element-inconnu";
        Assert.NotEmpty(GenericEnvelopeChecks.StructureErrors(unresolved));

        var duplicateKey = Example();
        ((JsonArray)duplicateKey["objects"]![0]!["properties"]!).Add(JsonNode.Parse("""{"key":"numero","label":"N","type":"text","value":"x"}"""));
        Assert.NotEmpty(GenericEnvelopeChecks.StructureErrors(duplicateKey));

        var unknownModel = Example();
        P(unknownModel, "modelKey")["value"] = "link-9";
        Assert.NotEmpty(GenericEnvelopeChecks.StructureErrors(unknownModel));

        var invertedBox = Example();
        P(invertedBox, "box")["value"]!["min"] = new JsonArray(9e9, 0, 0);
        Assert.NotEmpty(GenericEnvelopeChecks.StructureErrors(invertedBox));
    }

    /// <summary>Première propriété de clé <paramref name="key"/> (de l'objet <paramref name="objectId"/>, sinon de la première pièce).</summary>
    private static JsonObject P(JsonNode envelope, string key, string objectId = "room-100")
    {
        var obj = GenericEnvelopeChecks.AllObjects(envelope).First(o => o["id"]!.GetValue<string>() == objectId);
        return ((JsonArray)obj["properties"]!).First(p => p!["key"]!.GetValue<string>() == key)!.AsObject();
    }
}
