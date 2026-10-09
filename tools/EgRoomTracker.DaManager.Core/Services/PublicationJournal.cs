using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using EgRoomTracker.Aps.DesignAutomation;
using EgRoomTracker.Aps.Packaging;

namespace EgRoomTracker.DaManager.Services;

/// <summary>Versions pointées par les alias <c>test</c> et <c>prod</c> d'une ressource (null : alias absent).</summary>
public sealed record AliasVersions(int? Test, int? Prod);

/// <summary>Versions de production remplacées par la publication, cible du retour arrière (null : aucune).</summary>
public sealed record PreviousProd(int? AppBundle, int? Activity);

/// <summary>
/// Ligne du journal de publication (docs/PUBLISHING.md section 8) :
/// Date | Commit | Revit | AppBundle test / prod | Activity test / prod | Prod précédente (AppBundle / Activity).
/// </summary>
public sealed record JournalEntry(string Date, string? Commit, string Revit, AliasVersions AppBundle, AliasVersions Activity, PreviousProd? PreviousProd)
{
    /// <summary>Toutes les versions citées par la ligne pour un type de ressource (à conserver pour le retour arrière).</summary>
    public IEnumerable<int> Versions(DaResourceKind kind)
    {
        var pair = kind == DaResourceKind.AppBundle ? AppBundle : Activity;
        var previous = kind == DaResourceKind.AppBundle ? PreviousProd?.AppBundle : PreviousProd?.Activity;
        return new[] { pair.Test, pair.Prod, previous }.OfType<int>();
    }

    public string Text =>
        $"{Date} · {Commit ?? "—"} · Revit {Revit} · AppBundle {PublicationJournal.Pair(AppBundle.Test, AppBundle.Prod)} · " +
        $"Activity {PublicationJournal.Pair(Activity.Test, Activity.Prod)} · prod précédente {(PreviousProd is null ? "—" : PublicationJournal.Pair(PreviousProd.AppBundle, PreviousProd.Activity))}";
}

/// <summary>
/// Journal de publication lisible : JSON indenté (une entrée par version Revit publiée) et table Markdown de
/// docs/PUBLISHING.md section 8, en lecture comme en écriture. Le nettoyage des versions protège automatiquement toute
/// version citée par le journal.
/// </summary>
public sealed class PublicationJournal
{
    public const string Format = "egr-publication-journal";
    public const int FormatVersion = 1;

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static readonly string[] Header = ["Date", "Commit", "Revit", "AppBundle test / prod", "Activity test / prod", "Prod précédente (AppBundle / Activity)"];

    public PublicationJournal(IEnumerable<JournalEntry>? entries = null)
    {
        Entries = [.. (entries ?? []).Distinct().OrderBy(e => e.Date, StringComparer.Ordinal).ThenBy(e => e.Revit, StringComparer.Ordinal)];
    }

    public IReadOnlyList<JournalEntry> Entries { get; }

    /// <summary>Versions à protéger pour une ressource : toutes celles que le journal cite pour cette version Revit.</summary>
    public IReadOnlyList<int> ProtectedVersions(string revitVersion, DaResourceKind kind) =>
        [.. Entries.Where(e => e.Revit == revitVersion).SelectMany(e => e.Versions(kind)).Distinct().Order()];

    /// <summary>Fusion sans doublon (import) : les lignes identiques ne sont gardées qu'une fois.</summary>
    public PublicationJournal Merge(IEnumerable<JournalEntry> entries) => new(Entries.Concat(entries));

    public PublicationJournal Add(JournalEntry entry) => Merge([entry]);

    // ---- Fichiers ------------------------------------------------------------------------------------------------

    /// <summary>Journal du fichier, ou journal vide si le fichier n'existe pas. Un fichier illisible lève une erreur.</summary>
    public static PublicationJournal Load(string path) =>
        File.Exists(path) ? Import(path, File.ReadAllText(path)) : new PublicationJournal();

    /// <summary>Lecture selon l'extension : <c>.md</c> (table de PUBLISHING section 8), sinon JSON.</summary>
    public static PublicationJournal Import(string path, string text) =>
        IsMarkdown(path) ? ParseMarkdown(text) : ParseJson(text);

    /// <summary>Écriture atomique ; <c>.md</c> produit la table Markdown, toute autre extension le JSON.</summary>
    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, IsMarkdown(path) ? ToMarkdown() : ToJson(), new UTF8Encoding(false));
        File.Move(temporary, path, overwrite: true);
    }

    // ---- JSON ----------------------------------------------------------------------------------------------------

    public string ToJson()
    {
        var entries = new JsonArray();
        foreach (var entry in Entries)
        {
            entries.Add(new JsonObject
            {
                ["date"] = entry.Date,
                ["commit"] = entry.Commit,
                ["revit"] = entry.Revit,
                ["appBundle"] = new JsonObject { ["test"] = entry.AppBundle.Test, ["prod"] = entry.AppBundle.Prod },
                ["activity"] = new JsonObject { ["test"] = entry.Activity.Test, ["prod"] = entry.Activity.Prod },
                ["previousProd"] = entry.PreviousProd is null ? null : new JsonObject { ["appBundle"] = entry.PreviousProd.AppBundle, ["activity"] = entry.PreviousProd.Activity },
            });
        }

        var root = new JsonObject { ["format"] = Format, ["version"] = FormatVersion, ["entries"] = entries };
        return root.ToJsonString(WriteOptions) + "\n";
    }

    public static PublicationJournal ParseJson(string json)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"Journal JSON illisible : {exception.Message}");
        }

        if (root is not JsonObject obj || obj["format"]?.ToString() != Format)
        {
            throw new InvalidOperationException($"Journal JSON : « format » doit valoir « {Format} ».");
        }

        if (Number(obj["version"], "version") != FormatVersion)
        {
            throw new InvalidOperationException($"Journal JSON : version {obj["version"]} non prise en charge (attendue : {FormatVersion}).");
        }

        var entries = new List<JournalEntry>();
        var index = 0;
        foreach (var node in obj["entries"] as JsonArray ?? throw new InvalidOperationException("Journal JSON : tableau « entries » manquant."))
        {
            index++;
            try
            {
                var e = node as JsonObject ?? throw new InvalidOperationException("objet attendu");
                var previous = e["previousProd"] is JsonObject p ? new PreviousProd(Version(p["appBundle"]), Version(p["activity"])) : null;
                entries.Add(Validate(new JournalEntry(
                    Text(e["date"]) ?? throw new InvalidOperationException("« date » manquante"),
                    Text(e["commit"]),
                    Text(e["revit"]) ?? throw new InvalidOperationException("« revit » manquant"),
                    ReadPair(e["appBundle"], "appBundle"),
                    ReadPair(e["activity"], "activity"),
                    previous)));
            }
            catch (Exception exception) when (exception is InvalidOperationException or FormatException)
            {
                throw new InvalidOperationException($"Journal JSON, entrée {index} : {exception.Message}");
            }
        }

        return new PublicationJournal(entries);
    }

    // ---- Markdown ------------------------------------------------------------------------------------------------

    /// <summary>Table de docs/PUBLISHING.md section 8 (à coller dans une description de PR, une release ou un ticket).</summary>
    public string ToMarkdown()
    {
        var builder = new StringBuilder();
        builder.Append("| ").AppendJoin(" | ", Header).Append(" |\n");
        builder.Append(string.Concat(Enumerable.Repeat("| --- ", Header.Length))).Append("|\n");
        foreach (var e in Entries)
        {
            builder.Append($"| {e.Date} | {e.Commit ?? "—"} | {e.Revit} | {Pair(e.AppBundle.Test, e.AppBundle.Prod)} | {Pair(e.Activity.Test, e.Activity.Prod)} | ")
                .Append(e.PreviousProd is null ? "—" : Pair(e.PreviousProd.AppBundle, e.PreviousProd.Activity))
                .Append(" |\n");
        }

        return builder.ToString();
    }

    /// <summary>Lit les lignes de la table (en-tête et séparateur ignorés ; le texte autour de la table aussi).</summary>
    public static PublicationJournal ParseMarkdown(string markdown)
    {
        var entries = new List<JournalEntry>();
        var lineNumber = 0;
        foreach (var raw in markdown.Split('\n'))
        {
            lineNumber++;
            var line = raw.Trim();
            if (!line.StartsWith('|'))
            {
                continue;
            }

            var cells = line.Trim('|').Split('|').Select(c => c.Trim().Trim('`')).ToArray();
            if (cells[0] == Header[0] || cells.All(c => c.Length == 0 || c.All(ch => ch is '-' or ':')))
            {
                continue;
            }

            try
            {
                if (cells.Length != Header.Length)
                {
                    throw new InvalidOperationException($"{Header.Length} colonnes attendues, {cells.Length} trouvée(s)");
                }

                var (bundleTest, bundleProd) = ParsePair(cells[3]);
                var (activityTest, activityProd) = ParsePair(cells[4]);
                PreviousProd? previous = null;
                if (!IsEmpty(cells[5]))
                {
                    var (previousBundle, previousActivity) = ParsePair(cells[5]);
                    previous = new PreviousProd(previousBundle, previousActivity);
                }

                entries.Add(Validate(new JournalEntry(
                    cells[0],
                    IsEmpty(cells[1]) ? null : cells[1],
                    cells[2],
                    new AliasVersions(bundleTest, bundleProd),
                    new AliasVersions(activityTest, activityProd),
                    previous)));
            }
            catch (Exception exception) when (exception is InvalidOperationException or FormatException)
            {
                throw new InvalidOperationException($"Journal Markdown, ligne {lineNumber} : {exception.Message}");
            }
        }

        return new PublicationJournal(entries);
    }

    internal static string Pair(int? first, int? second) => $"{Show(first)} / {Show(second)}";

    // ---- Interne -------------------------------------------------------------------------------------------------

    private static JournalEntry Validate(JournalEntry entry)
    {
        if (!DateOnly.TryParseExact(entry.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            throw new InvalidOperationException($"date « {entry.Date} » invalide (AAAA-MM-JJ attendu)");
        }

        if (!DeploymentService.SupportedVersions.Contains(entry.Revit))
        {
            throw new InvalidOperationException($"version Revit « {entry.Revit} » inconnue ({string.Join(", ", DeploymentService.SupportedVersions)})");
        }

        return entry;
    }

    private static (int?, int?) ParsePair(string cell)
    {
        var parts = cell.Split('/');
        return parts.Length == 2
            ? (ParseVersion(parts[0]), ParseVersion(parts[1]))
            : throw new InvalidOperationException($"« {cell} » : deux versions « test / prod » attendues");
    }

    private static int? ParseVersion(string text)
    {
        var value = text.Trim();
        if (IsEmpty(value))
        {
            return null;
        }

        return int.TryParse(value.TrimStart('v', 'V'), NumberStyles.None, CultureInfo.InvariantCulture, out var version) && version >= 1
            ? version
            : throw new InvalidOperationException($"version « {value} » invalide");
    }

    private static bool IsEmpty(string text) => text is "" or "-" or "—" or "–" || text.StartsWith("aucun", StringComparison.OrdinalIgnoreCase);

    private static AliasVersions ReadPair(JsonNode? node, string name) =>
        node is JsonObject o ? new AliasVersions(Version(o["test"]), Version(o["prod"])) : throw new InvalidOperationException($"objet « {name} » manquant");

    private static int? Version(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        var version = Number(node, "version");
        return version >= 1 ? version : throw new InvalidOperationException($"version {version} invalide");
    }

    private static int Number(JsonNode? node, string name) =>
        node is JsonValue value && value.TryGetValue<int>(out var number) ? number : throw new InvalidOperationException($"« {name} » : entier attendu");

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text.Trim() : null;

    private static string Show(int? version) => version is null ? "—" : $"v{version}";

    private static bool IsMarkdown(string path) => Path.GetExtension(path).Equals(".md", StringComparison.OrdinalIgnoreCase);
}
