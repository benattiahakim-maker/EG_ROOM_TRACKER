using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace EgRoomTracker.Tests.Support;

/// <summary>
/// Validation contre <c>docs/schema/result.schema.json</c> (JSON Schema draft 2020-12, formats vérifiés),
/// copié dans le dossier de sortie des tests sous <c>Data/</c>.
/// </summary>
internal static class ResultSchema
{
    private static readonly Lazy<JsonSchema> Schema = new(() =>
        JsonSchema.FromText(File.ReadAllText(DataPath("result.schema.json"))));

    private static readonly EvaluationOptions Options = new()
    {
        OutputFormat = OutputFormat.List,
        RequireFormatValidation = true,
    };

    public static string DataPath(string fileName) => Path.Combine(AppContext.BaseDirectory, "Data", fileName);

    /// <summary>Erreurs de validation (« emplacement : message ») ; vide si l'instance est conforme.</summary>
    public static IReadOnlyList<string> Validate(JsonElement instance)
    {
        var results = Schema.Value.Evaluate(instance, Options);
        if (results.IsValid)
        {
            return [];
        }

        var errors = (results.Details ?? [])
            .Where(d => d.Errors is { Count: > 0 })
            .SelectMany(d => d.Errors!.Select(e => $"{d.InstanceLocation} : {e.Key} : {e.Value}"))
            .ToList();
        return errors.Count > 0 ? errors : ["<racine> : instance non conforme"];
    }

    public static IReadOnlyList<string> Validate(string json)
    {
        using var document = JsonDocument.Parse(json);
        return Validate(document.RootElement);
    }

    public static IReadOnlyList<string> Validate(JsonNode node) => Validate(node.ToJsonString());

    /// <summary>Échoue avec le détail des erreurs si l'instance n'est pas conforme.</summary>
    public static void AssertValid(string json)
    {
        var errors = Validate(json);
        Assert.True(errors.Count == 0, "result.json non conforme au schéma :\n" + string.Join("\n", errors));
    }

    public static void AssertValid(JsonNode node) => AssertValid(node.ToJsonString());

    public static void AssertInvalid(JsonNode node) =>
        Assert.True(Validate(node).Count > 0, "Instance acceptée à tort par le schéma :\n" + node.ToJsonString());

    public static JsonNode LoadExample() =>
        JsonNode.Parse(File.ReadAllText(DataPath("result.example.json")))!;
}
