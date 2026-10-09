using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace EgRoomTracker.WorkItemRunner.Services;

/// <summary>Ligne de pièce affichée (sous-ensemble de <c>rooms[]</c>).</summary>
public sealed record RoomRow(string? Number, string? Name, string? Level, double? AreaM2, double? VolumeM3, string? Phase, string QualityFlags);

/// <summary>Ligne d'anomalie affichée.</summary>
public sealed record AnomalyRow(string Code, string Severity, string Scope, string? ElementId, string Message);

/// <summary>Lecture de <c>result.json</c> : bilan, pièces, anomalies et écarts au schéma.</summary>
public sealed record ResultView(
    string? Status,
    IReadOnlyList<KeyValuePair<string, string>> Summary,
    IReadOnlyList<RoomRow> Rooms,
    IReadOnlyList<AnomalyRow> Anomalies,
    IReadOnlyList<string> SchemaErrors)
{
    public bool IsSchemaValid => SchemaErrors.Count == 0;
}

/// <summary>
/// Lecture et validation de <c>result.json</c> contre <c>docs/schema/result.schema.json</c> (draft 2020-12, formats
/// vérifiés, comme <c>src/Tests/Support/ResultSchema.cs</c>).
/// </summary>
public sealed class ResultReader(string schemaPath)
{
    private static readonly EvaluationOptions Options = new() { OutputFormat = OutputFormat.List, RequireFormatValidation = true };
    private readonly Lazy<JsonSchema> _schema = new(() => JsonSchema.FromText(
        File.ReadAllText(schemaPath),
        new BuildOptions { SchemaRegistry = new SchemaRegistry() })); // registre propre : le $id peut être chargé plusieurs fois

    public static ResultReader ForRepository(string repositoryRoot) =>
        new(Path.Combine(repositoryRoot, "docs", "schema", "result.schema.json"));

    public ResultView Read(string json)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException exception)
        {
            return new ResultView(null, [], [], [], [$"JSON illisible : {exception.Message}"]);
        }

        var summary = root?["summary"];
        var counts = summary?["counts"];
        var rows = new List<KeyValuePair<string, string>>
        {
            new("Statut", Text(summary?["status"])),
            new("Schéma du résultat", Text(root?["schemaVersion"])),
            new("Extracteur", Text(summary?["extractorVersion"])),
            new("Revit", $"{Text(summary?["revit"]?["version"])} ({Text(summary?["revit"]?["build"])})"),
            new("Engine", Text(summary?["apsEngine"])),
            new("Maquette", Text(summary?["source"]?["relativePath"])),
            new("Début / fin (UTC)", $"{Text(summary?["startedAtUtc"])} / {Text(summary?["completedAtUtc"])}"),
            new("Pièces collectées / exportées / avec anomalies", $"{Text(counts?["roomsCollected"])} / {Text(counts?["roomsExported"])} / {Text(counts?["roomsWithAnomalies"])}"),
            new("Corrélation", Text(summary?["correlationId"])),
        };
        foreach (var error in summary?["errors"]?.AsArray() ?? [])
        {
            rows.Add(new("Erreur", $"{Text(error?["code"])} ({Text(error?["stage"])}) : {Text(error?["message"])}"));
        }

        var rooms = (root?["rooms"]?.AsArray() ?? []).Select(r => new RoomRow(
            r?["number"]?.ToString(),
            r?["name"]?.ToString(),
            r?["level"] is JsonObject level ? level["name"]?.ToString() : r?["level"]?.ToString(),
            Number(r?["areaM2"]),
            Number(r?["volumeM3"]),
            r?["phase"] is JsonObject phase ? phase["name"]?.ToString() : r?["phase"]?.ToString(),
            string.Join(", ", (r?["qualityFlags"]?.AsArray() ?? []).Select(f => f?.ToString())))).ToList();
        var anomalies = (root?["anomalies"]?.AsArray() ?? []).Select(a => new AnomalyRow(
            Text(a?["code"]),
            Text(a?["severity"]),
            Text(a?["scope"]),
            a?["elementId"]?.ToString(),
            Text(a?["message"]))).ToList();
        return new ResultView(summary?["status"]?.ToString(), rows, rooms, anomalies, Validate(json));
    }

    public IReadOnlyList<string> Validate(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var results = _schema.Value.Evaluate(document.RootElement, Options);
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
        catch (JsonException exception)
        {
            return [$"JSON illisible : {exception.Message}"];
        }
    }

    private static string Text(JsonNode? node) => node?.ToString() ?? "—";

    private static double? Number(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<double>(out var number) ? number
        : node is not null && double.TryParse(node.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed
        : null;
}
