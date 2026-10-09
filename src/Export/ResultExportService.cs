using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using EgRoomTracker.Contracts.Results;

namespace EgRoomTracker.Export;

/// <summary>
/// Export de <c>result.json</c> (sections 2 et 6, EGR-12) :
/// <list type="bullet">
/// <item>sérialisation déterministe : propriétés dans l'ordre des contrats, pièces et anomalies dans l'ordre reçu
/// (le tri relève de l'orchestration), null écrits, énumérations en camelCase, clés de dictionnaire conservées ;</item>
/// <item>UTF-8 sans BOM, accents écrits tels quels, nombres en notation invariante sans arrondi (D7) ;</item>
/// <item>dates UTC ISO 8601 suffixées <c>Z</c> ;</item>
/// <item>indentation de deux espaces, fins de ligne <c>\n</c> sur toutes les plateformes, saut de ligne final ;</item>
/// <item>publication locale atomique : sérialisation complète en mémoire, écriture dans un fichier temporaire
/// du même dossier, vidage sur disque, puis renommage sur le chemin final. En cas d'échec, le fichier temporaire
/// est supprimé et aucun <c>result.json</c> partiel n'est publié.</item>
/// </list>
/// Aucune référence Revit.
/// </summary>
public sealed class ResultExportService : IExportService
{
    private const string TemporarySuffix = ".tmp";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false),
            new UtcDateTimeOffsetConverter(),
        },
    };

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException"><paramref name="result"/> est null.</exception>
    /// <exception cref="ArgumentException"><paramref name="outputPath"/> est vide.</exception>
    /// <exception cref="NotSupportedException">Valeur non sérialisable (ex. nombre non fini) : rien n'est écrit.</exception>
    public void Write(ExtractionResult result, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        // Sérialisation avant toute écriture : une erreur de sérialisation ne laisse aucun fichier.
        var bytes = Serialize(result);

        var fullPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new IOException($"Dossier de sortie introuvable pour {outputPath}.");
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}{TemporarySuffix}");

        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        catch
        {
            TryDelete(temporaryPath);
            throw;
        }
    }

    /// <summary>
    /// Contenu exact de <c>result.json</c> : UTF-8 sans BOM, fins de ligne <c>\n</c>, saut de ligne final.
    /// Lève <see cref="NotSupportedException"/> si une valeur n'est pas représentable en JSON (ex. NaN, infini).
    /// </summary>
    public static byte[] Serialize(ExtractionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        byte[] raw;
        try
        {
            raw = JsonSerializer.SerializeToUtf8Bytes(result, Options);
        }
        catch (ArgumentException ex)
        {
            // System.Text.Json refuse NaN et ±Infinity par ArgumentException : ce n'est pas un argument invalide
            // de l'appelant mais une valeur non représentable dans le contrat.
            throw new NotSupportedException("result.json : valeur non représentable en JSON (nombre non fini ?).", ex);
        }

        return NormalizeLineEndings(raw);
    }

    /// <summary>
    /// L'indentation de System.Text.Json (.NET 8) utilise <see cref="Environment.NewLine"/> : on ramène les
    /// <c>\r\n</c> à <c>\n</c> pour un fichier identique sous Windows (moteur APS) et Linux (tests). Les retours
    /// chariot présents dans les chaînes sont échappés (<c>\r</c>) par l'encodeur et ne sont donc pas concernés.
    /// </summary>
    private static byte[] NormalizeLineEndings(byte[] raw)
    {
        var output = new List<byte>(raw.Length + 1);
        for (var i = 0; i < raw.Length; i++)
        {
            if (raw[i] == (byte)'\r' && i + 1 < raw.Length && raw[i + 1] == (byte)'\n')
            {
                continue;
            }

            output.Add(raw[i]);
        }

        output.Add((byte)'\n');
        return [.. output];
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Dates UTC ISO 8601 suffixées <c>Z</c> (ex. <c>2026-10-09T12:00:03Z</c>), fraction de seconde si non nulle.</summary>
    private sealed class UtcDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
    {
        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            DateTimeOffset.Parse(reader.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture));
    }
}
