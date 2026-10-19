using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace EgRoomTracker.WorkItemRunner.Services;

/// <summary>Ligne de pièce affichée : tous les champs de <c>rooms[]</c>, les paramètres partagés réunis en un texte.</summary>
public sealed record RoomRow(
    string? ElementId,
    string? Number,
    string? Name,
    string? Level,
    string? Occupancy,
    double? AreaM2,
    double? UnboundedHeightM,
    double? VolumeM3,
    string? VolumeStatus,
    string? FinishFloor,
    string? FinishWall,
    string? FinishCeiling,
    string? FinishBase,
    string? Phase,
    string? DesignOption,
    string? SharedParameters,
    string QualityFlags);

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

    /// <summary>
    /// Colonnes du tableau des pièces, déduites des clés de <c>rooms[]</c> (union dans l'ordre d'apparition) : les objets
    /// imbriqués sont aplatis (<c>finitions.sol</c>), une référence d'élément (niveau, phase, variante) montre son nom,
    /// un paramètre partagé sa valeur. Un nouveau champ du contrat apparaît sans modifier l'outil.
    /// </summary>
    public IReadOnlyList<string> RoomColumns { get; init; } = [];

    /// <summary>Valeurs du tableau des pièces : une ligne par pièce, une cellule par colonne de <see cref="RoomColumns"/>.</summary>
    public IReadOnlyList<IReadOnlyList<string?>> RoomValues { get; init; } = [];

    /// <summary>
    /// Même tableau en arbre : une ligne par pièce (cellules de <see cref="RoomValues"/>), et sous elle une ligne par porte,
    /// équipement ou terminal qu'elle contient. Une porte entre deux pièces figure sous chacune.
    /// </summary>
    public IReadOnlyList<TableRow> RoomTree { get; init; } = [];
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
        if (summary?["images"] is JsonObject images)
        {
            rows.Add(new("Images (demandées / exportées / en échec)", $"{Text(images["requested"])} / {Text(images["exported"])} / {Text(images["failed"])}"));
        }

        foreach (var error in summary?["errors"]?.AsArray() ?? [])
        {
            rows.Add(new("Erreur", $"{Text(error?["code"])} ({Text(error?["stage"])}) : {Text(error?["message"])}"));
        }

        var rooms = (root?["rooms"]?.AsArray() ?? []).Select(r => new RoomRow(
            r?["id_revit"]?.ToString(),
            r?["numero"]?.ToString(),
            r?["nom"]?.ToString(),
            NamedRef(r?["niveau"]),
            r?["occupation"]?.ToString(),
            Number(r?["surface_m2"]),
            Number(r?["hauteur_non_limitee_m"]),
            Number(r?["volume_m3"]),
            r?["statut_volume"]?.ToString(),
            r?["finitions"]?["sol"]?.ToString(),
            r?["finitions"]?["murs"]?.ToString(),
            r?["finitions"]?["plafond"]?.ToString(),
            r?["finitions"]?["plinthe"]?.ToString(),
            NamedRef(r?["phase"]),
            NamedRef(r?["variante"]),
            SharedParameters(r?["parametres_partages"]),
            string.Join(", ", (r?["indicateurs_qualite"]?.AsArray() ?? []).Select(f => f?.ToString())))).ToList();
        var anomalies = (root?["anomalies"]?.AsArray() ?? []).Select(a => new AnomalyRow(
            Text(a?["code"]),
            Text(a?["severity"]),
            Text(a?["scope"]),
            a?["elementId"]?.ToString(),
            Text(a?["message"]))).ToList();
        var (columns, tree) = BuildRoomTable(root?["rooms"]?.AsArray());
        return new ResultView(summary?["status"]?.ToString(), rows, rooms, anomalies, Validate(json))
        {
            RoomColumns = columns,
            RoomValues = tree.Select(row => row.Cells).ToList(),
            RoomTree = tree,
        };
    }

    /// <summary>Première colonne du tableau de <c>result.json</c> : nature de la ligne (pièce, porte, équipement ou terminal et sa discipline).</summary>
    public const string ObjectColumn = "objet";

    /// <summary>Tableaux d'éléments d'une pièce présentés comme lignes enfants, avec le libellé de leur colonne <see cref="ObjectColumn"/>.</summary>
    private static readonly (string Key, string Label)[] ElementArrays = [("portes", "porte"), ("equipements", "équipement"), ("terminaux", "terminal")];

    private static (IReadOnlyList<string> Columns, IReadOnlyList<TableRow> Rows) BuildRoomTable(JsonArray? rooms)
    {
        var columns = new List<string> { ObjectColumn };
        var known = new HashSet<string>(StringComparer.Ordinal) { ObjectColumn };
        Dictionary<string, string?> Cells(JsonNode? node, string label)
        {
            var cells = new Dictionary<string, string?>(StringComparer.Ordinal) { [ObjectColumn] = label };
            Flatten(node, string.Empty, cells);
            foreach (var key in cells.Keys.Where(known.Add))
            {
                columns.Add(key);
            }

            return cells;
        }

        // Les colonnes des pièces d'abord (dans l'ordre d'apparition), puis celles propres aux éléments.
        var flattened = (rooms ?? []).Select(room => (Room: Cells(room, "pièce"), Node: room)).ToList();
        var withChildren = flattened.Select(entry => (entry.Room, Children: ElementArrays
                .SelectMany(array => (entry.Node?[array.Key] as JsonArray ?? [])
                    .OfType<JsonObject>()
                    .Select(item => Cells(item, item["discipline"] is JsonValue discipline ? $"{array.Label} {discipline}" : array.Label)))
                .ToList()))
            .ToList();

        IReadOnlyList<string?> Row(Dictionary<string, string?> cells) => columns.Select(column => cells.GetValueOrDefault(column)).ToArray();
        return (columns, withChildren
            .Select(entry => new TableRow(Row(entry.Room), entry.Children.Select(child => new TableRow(Row(child), depth: 1)).ToList()))
            .ToList());
    }

    private static void Flatten(JsonNode? node, string path, Dictionary<string, string?> cells)
    {
        switch (node)
        {
            case JsonObject reference when path.Length > 0 && reference["nom"] is JsonValue name:
                cells[path] = name.ToString();
                break;
            case JsonObject parameter when path.Length > 0 && parameter.ContainsKey("guid") && parameter.ContainsKey("valeur"):
                cells[path] = parameter["valeur"] is JsonValue value ? Scalar(value) : null;
                break;
            case JsonObject obj:
                foreach (var property in obj)
                {
                    Flatten(property.Value, path.Length == 0 ? property.Key : $"{path}.{property.Key}", cells);
                }

                break;
            case JsonArray array when array.Any(item => item is JsonObject { } o && o.ContainsKey("discipline")):
                // Équipements et terminaux : une colonne par discipline (équipements.cvc, …), même vide, pour garder le détail.
                foreach (var discipline in Disciplines)
                {
                    cells[$"{path}.{discipline}"] = Summarize(new JsonArray(array
                        .OfType<JsonObject>()
                        .Where(item => item["discipline"]?.ToString() == discipline)
                        .Select(item => (JsonNode)item.DeepClone())
                        .ToArray()));
                }

                break;
            case JsonArray array when array.Any(item => item is JsonObject):
                cells[path] = Summarize(array);
                break;
            case JsonArray array:
                cells[path] = string.Join(", ", array.Select(item => item is JsonValue v ? Scalar(v) : item?.ToJsonString()));
                break;
            case JsonValue scalar:
                cells[path] = Scalar(scalar);
                break;
            default:
                cells[path] = null;
                break;
        }
    }

    private static readonly string[] Disciplines = ["cvc", "plomberie", "electricite", "autres"];

    /// <summary>Nombre maximal de repères cités dans la cellule d'un tableau d'éléments.</summary>
    private const int MaxLabels = 5;

    /// <summary>
    /// Cellule d'un tableau d'objets : le nombre d'éléments, et pour des éléments rattachés à la pièce (portes, équipements,
    /// terminaux) leurs repères (<c>repere</c>, sinon <c>type</c>, sinon <c>id_revit</c>). Le détail est dans le JSON exporté.
    /// </summary>
    private static string Summarize(JsonArray array)
    {
        var labels = array
            .OfType<JsonObject>()
            .Where(item => item.ContainsKey("id_revit"))
            .Select(item => new[] { item["repere"], item["type"], item["id_revit"] }
                .Select(node => node?.ToString())
                .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text)) ?? "?")
            .ToList();
        return labels.Count == 0
            ? array.Count == 0 ? "0" : $"{array.Count} élément(s)"
            : $"{array.Count} : {string.Join(", ", labels.Take(MaxLabels))}{(labels.Count > MaxLabels ? ", …" : string.Empty)}";
    }

    /// <summary>Texte d'une valeur JSON ; les nombres sont écrits en notation invariante, sans zéros inutiles.</summary>
    private static string Scalar(JsonValue value) =>
        value.GetValueKind() == JsonValueKind.Number && value.TryGetValue<double>(out var number)
            ? number.ToString("0.####", CultureInfo.InvariantCulture)
            : value.ToString();

    /// <summary>
    /// Lecture de <c>TRCValidationDict.json</c> (fiches de l'application TRC, données seules) : une ligne par objet BIM, une colonne par
    /// code de propriété (valeurs multiples réunies par « | »), en arbre : portes et équipements sous les pièces de leur
    /// <c>ListeSpaces</c> (pk des pièces). Pas de schéma, de statut ni d'anomalies : il n'y en a pas dans ce format.
    /// </summary>
    /// <summary>Ancien code de rattachement (<c>bim_id</c> de la pièce, une fiche par pièce), remplacé par <c>ListeSpaces</c>.</summary>
    private const string LegacySpaceCode = "Space";

    public ResultView ReadTrc(string json)
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

        var fiches = root?.AsArray().OfType<JsonObject>().ToList() ?? [];
        var objects = fiches.Where(f => f["model"]?.ToString() == "trc.bimobject").ToList();
        var bimIdByPk = objects.ToDictionary(o => o["pk"]!.ToString(), o => o["fields"]?["bim_id"]?.ToString() ?? string.Empty, StringComparer.Ordinal);

        var columns = new List<string> { "bim_id" };
        var known = new HashSet<string>(StringComparer.Ordinal) { "bim_id" };
        var cells = bimIdByPk.Keys.ToDictionary(pk => pk, pk => new Dictionary<string, List<string>>(StringComparer.Ordinal), StringComparer.Ordinal);
        var valueCount = 0;
        foreach (var fiche in fiches.Where(f => f["model"]?.ToString() == "trc.bimobjectpropertyvalue"))
        {
            var fields = fiche["fields"];
            var pk = fields?["bim_object_id"]?.ToString();
            var code = fields?["bim_property_code_property"]?.ToString();
            if (pk is null || code is null || !cells.TryGetValue(pk, out var row))
            {
                continue;
            }

            if (known.Add(code))
            {
                columns.Add(code);
            }

            if (!row.TryGetValue(code, out var list))
            {
                row[code] = list = [];
            }

            list.Add(fields?["latest_value"]?.ToString() ?? string.Empty);
            valueCount++;
        }

        IReadOnlyList<string?> Row(string pk) => columns
            .Select(column => column == "bim_id" ? bimIdByPk[pk] : cells[pk].TryGetValue(column, out var list) ? string.Join(" | ", list) : null)
            .ToArray();

        // Arbre : un objet rattaché à une pièce est rangé sous elle, sous chacune s'il en a plusieurs ; un objet sans rattachement, ou
        // dont aucune pièce n'est dans le fichier, reste à la racine. Parents désignés par leur pk.
        var pkByBimId = bimIdByPk.GroupBy(pair => pair.Value, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Key, StringComparer.Ordinal);
        var spaces = bimIdByPk.Keys.ToDictionary(pk => pk, pk => ParentPks(cells[pk]).Where(bimIdByPk.ContainsKey).Distinct().ToList(), StringComparer.Ordinal);

        IEnumerable<string> ParentPks(Dictionary<string, List<string>> row)
        {
            // ListeSpaces : une valeur, liste JSON des pk des pièces.
            foreach (var value in row.GetValueOrDefault(EgRoomTracker.Application.Trc.TrcCodes.SpaceList) ?? [])
            {
                JsonArray? list = null;
                try
                {
                    list = JsonNode.Parse(value) as JsonArray;
                }
                catch (JsonException)
                {
                }

                foreach (var pk in list ?? [])
                {
                    if (pk is JsonValue text && text.TryGetValue<string>(out var parent))
                    {
                        yield return parent;
                    }
                }
            }

            // Space : ancien format (bim_id de la pièce, une fiche par pièce), pour les lancements déjà dans l'historique.
            foreach (var bimId in row.GetValueOrDefault(LegacySpaceCode) ?? [])
            {
                if (pkByBimId.TryGetValue(bimId, out var parent))
                {
                    yield return parent;
                }
            }
        }
        var children = new Dictionary<string, List<TableRow>>(StringComparer.Ordinal);
        foreach (var (pk, parents) in spaces)
        {
            foreach (var parent in parents)
            {
                if (!children.TryGetValue(parent, out var list))
                {
                    children[parent] = list = [];
                }

                list.Add(new TableRow(Row(pk), depth: 1));
            }
        }

        var tree = bimIdByPk.Keys
            .Where(pk => spaces[pk].Count == 0)
            .Select(pk => new TableRow(Row(pk), children.GetValueOrDefault(pk)))
            .ToList();

        var codeFile = fiches.FirstOrDefault(f => f["model"]?.ToString() == "special.setcodefile")?["fields"]?["code_file"]?.ToString();
        var rows = new List<KeyValuePair<string, string>>
        {
            new("Format", "TRC (TRCValidationDict.json), données seules"),
            new("Fichier (code_file)", codeFile ?? "—"),
            new("Objets BIM / valeurs", $"{objects.Count} / {valueCount}"),
        };
        return new ResultView(null, rows, [], [], [])
        {
            RoomColumns = columns,
            RoomValues = tree.Select(row => row.Cells).ToList(),
            RoomTree = tree,
        };
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

    /// <summary>Référence d'élément (niveau, phase, variante) : son nom si c'est un objet, sinon sa valeur.</summary>
    private static string? NamedRef(JsonNode? node) =>
        node is JsonObject reference ? reference["nom"]?.ToString() : node?.ToString();

    /// <summary>Paramètres partagés d'une pièce : <c>clé=valeur</c>, séparés par « ; » (valeur absente : <c>—</c>).</summary>
    private static string? SharedParameters(JsonNode? node) =>
        node is JsonObject parameters && parameters.Count > 0
            ? string.Join("; ", parameters.Select(p => $"{p.Key}={p.Value?["valeur"]?.ToString() ?? "—"}"))
            : null;

    private static double? Number(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<double>(out var number) ? number
        : node is not null && double.TryParse(node.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed
        : null;
}
