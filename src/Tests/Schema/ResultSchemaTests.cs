using System.Text.Json.Nodes;
using EgRoomTracker.Tests.Support;

namespace EgRoomTracker.Tests.Schema;

/// <summary>
/// Le schéma de <c>result.json</c> et l'exemple de référence (sections 2, 3, 9 et 10.2) : l'exemple est conforme,
/// et les écarts au contrat (null/zéro/vide, statuts, portées) sont rejetés.
/// </summary>
public class ResultSchemaTests
{
    [Fact]
    public void Example_IsValid() => ResultSchema.AssertValid(ResultSchema.LoadExample());

    [Fact]
    public void Example_RoomsAreSortedByNumericElementId()
    {
        var ids = AllRooms(ResultSchema.LoadExample()).Select(r => long.Parse(r!["id_revit"]!.GetValue<string>())).ToList();
        Assert.Equal(ids.Order(), ids);
    }

    public static TheoryData<string, Action<JsonNode>> InvalidMutations => new()
    {
        // Racine et bilan.
        { "schemaVersion majeure 2", r => r["schemaVersion"] = "2.0" },
        { "propriété racine inconnue", r => r["rooms.csv"] = "x" },
        { "summary.apsEngine absent (null attendu)", r => r["summary"]!.AsObject().Remove("apsEngine") },
        { "revit.version hors cibles", r => r["summary"]!["revit"]!["version"] = "2024" },
        { "status inconnu", r => r["summary"]!["status"] = "warning" },
        { "succeeded avec erreur globale", r => { r["summary"]!["status"] = "succeeded"; r["summary"]!["errors"] = Error("EXPORT_FAILED"); } },
        { "failed sans erreur globale", r => r["summary"]!["status"] = "failed" },
        { "code d'erreur inconnu", r => { r["summary"]!["status"] = "failed"; r["summary"]!["errors"] = Error("CONFIG_BROKEN"); } },
        { "date non ISO 8601", r => r["summary"]!["startedAtUtc"] = "09/10/2026 12:00" },
        { "compteur négatif", r => r["summary"]!["counts"]!["roomsExported"] = -1 },
        { "compteur décimal", r => r["summary"]!["counts"]!["roomsExported"] = 2.5 },
        { "volumeMode inconnu", r => r["summary"]!["options"]!["volumeMode"] = "recompute" },
        { "phaseIds non numérique", r => r["summary"]!["options"]!["phaseIds"] = new JsonArray("Nouvelle construction") },

        // Pièces : identifiants, null / zéro / vide, nombres.
        { "elementId numérique (chaîne attendue)", r => Room(r, 0)!["id_revit"] = 412345 },
        { "elementId non entier", r => Room(r, 0)!["id_revit"] = "412345.0" },
        { "uniqueId vide", r => Room(r, 0)!["unique_id"] = "" },
        { "number numérique (zéros initiaux perdus)", r => Room(r, 0)!["numero"] = 1 },
        { "areaM2 en chaîne (nombre invariant attendu)", r => Room(r, 0)!["surface_m2"] = "18,425" },
        { "areaM2 négative", r => Room(r, 0)!["surface_m2"] = -1 },
        { "champ nullable absent (null attendu)", r => Room(r, 1)!.AsObject().Remove("occupation") },
        { "finition absente (null attendu)", r => Room(r, 1)!["finitions"]!.AsObject().Remove("plinthe") },
        { "volumeM3 à 0 avec notComputed", r => Room(r, 0)!["volume_m3"] = 0 },
        { "volumeM3 null avec computed", r => Room(r, 0)!["statut_volume"] = "computed" },
        { "volumeStatus inconnu", r => Room(r, 0)!["statut_volume"] = "disabled" },
        { "niveau incomplet", r => Room(r, 0)!["niveau"]!.AsObject().Remove("unique_id") },
        { "indicateur qualité inconnu", r => Room(r, 0)!["indicateurs_qualite"] = new JsonArray("NOT_A_FLAG") },
        { "code document en indicateur de pièce", r => Room(r, 0)!["indicateurs_qualite"] = new JsonArray("VOLUME_NOT_COMPUTED") },
        { "indicateurs en double", r => Room(r, 0)!["indicateurs_qualite"] = new JsonArray("DUPLICATE_NUMBER", "DUPLICATE_NUMBER") },
        { "guid de paramètre partagé invalide", r => Room(r, 0)!["parametres_partages"]!["codeLocal"]!["guid"] = "codeLocal" },
        { "valeur de paramètre partagé booléenne", r => Room(r, 0)!["parametres_partages"]!["codeLocal"]!["valeur"] = true },

        // Anomalies : portée, code, elementId.
        { "anomalie de pièce sans elementId", r => Anomaly(r, 1)!["elementId"] = null },
        { "anomalie de document avec elementId", r => Anomaly(r, 0)!["elementId"] = "412345" },
        { "code document en portée pièce", r => Anomaly(r, 1)!["code"] = "LINK_MISSING" },
        { "indicateur de pièce en portée document", r => { Anomaly(r, 0)!["code"] = "UNPLACED"; } },
        { "code d'erreur globale en anomalie", r => Anomaly(r, 1)!["code"] = "EXPORT_FAILED" },
        { "sévérité inconnue", r => Anomaly(r, 1)!["severity"] = "fatal" },
        { "details absent", r => Anomaly(r, 1)!.AsObject().Remove("details") },
    };

    [Theory]
    [MemberData(nameof(InvalidMutations))]
    public void Example_WithContractViolation_IsRejected(string description, Action<JsonNode> mutate)
    {
        _ = description;
        var result = ResultSchema.LoadExample();
        mutate(result);
        ResultSchema.AssertInvalid(result);
    }

    public static TheoryData<string, Action<JsonNode>> ValidVariants => new()
    {
        { "chaîne vide renseignée", r => Room(r, 0)!["nom"] = "" },
        { "valeur numérique nulle", r => Room(r, 0)!["hauteur_non_limitee_m"] = 0 },
        { "champ sans valeur", r => Room(r, 0)!["surface_m2"] = null },
        { "volume calculé", r => { Room(r, 0)!["statut_volume"] = "computed"; Room(r, 0)!["volume_m3"] = 49.7475; } },
        { "volume calculé nul", r => { Room(r, 0)!["statut_volume"] = "computed"; Room(r, 0)!["volume_m3"] = 0; } },
        { "volume illisible", r => Room(r, 0)!["statut_volume"] = "notAvailable" },
        { "niveau introuvable", r => { Room(r, 0)!["niveau"] = null; Room(r, 0)!["indicateurs_qualite"] = new JsonArray("LEVEL_NOT_FOUND"); } },
        { "elementId 64 bits", r => Room(r, 2)!["id_revit"] = "9223372036854775807" },
        { "paramètre partagé numérique", r => Room(r, 0)!["parametres_partages"]!["codeLocal"]!["valeur"] = 12.5 },
        { "sans paramètre partagé", r => Room(r, 0)!["parametres_partages"] = new JsonObject() },
        { "schemaVersion mineure supérieure", r => r["schemaVersion"] = "1.4" },
        { "date avec décalage +00:00", r => r["summary"]!["startedAtUtc"] = "2026-10-09T12:00:03+00:00" },
        { "source inconnue", r => r["summary"]!["source"] = null },
        { "phases filtrées", r => r["summary"]!["options"]!["phaseIds"] = new JsonArray("86961") },
    };

    [Theory]
    [MemberData(nameof(ValidVariants))]
    public void Example_WithAllowedVariant_IsValid(string description, Action<JsonNode> mutate)
    {
        _ = description;
        var result = ResultSchema.LoadExample();
        mutate(result);
        ResultSchema.AssertValid(result);
    }

    [Fact]
    public void FailedResult_WithEmptyRoomsAndAnomalies_IsValid()
    {
        var result = ResultSchema.LoadExample();
        result["summary"]!["status"] = "failed";
        result["summary"]!["errors"] = Error("RVT_AMBIGUOUS");
        result["summary"]!["source"] = null;
        result["summary"]!["counts"] = new JsonObject { ["roomsCollected"] = 0, ["roomsExported"] = 0, ["roomsWithAnomalies"] = 0 };
        result["rooms"] = new JsonArray();
        result["anomalies"] = new JsonArray();

        ResultSchema.AssertValid(result);
    }

    private static JsonArray Error(string code) =>
        new(new JsonObject { ["code"] = code, ["stage"] = "export", ["message"] = "Erreur." });

    private static JsonNode? Room(JsonNode result, int index) => result["rooms"]![index];

    private static JsonArray AllRooms(JsonNode result) => result["rooms"]!.AsArray();

    private static JsonNode? Anomaly(JsonNode result, int index) => result["anomalies"]![index];
}
