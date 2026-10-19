using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace EgRoomTracker.Application.Trc;

/// <summary>Écriture du fichier TRC et de ses annexes dans le dossier de sortie.</summary>
public interface ITrcExportService
{
    /// <summary>Écrit <c>TRCValidationDict.json</c> et <c>Details_Annexes/</c> dans <paramref name="directory"/> ; retourne le nom du fichier.</summary>
    string Write(Contracts.Results.ExtractionResult result, string directory);
}

/// <summary>
/// Écrit <c>TRCValidationDict.json</c> (liste de fiches, indentation de deux espaces, UTF-8 sans BOM, accents écrits tels quels,
/// comme TRC) de façon atomique, puis les annexes. Un échec ne laisse aucun fichier partiel.
/// </summary>
public sealed class TrcExportService : ITrcExportService
{
    public const string FileName = "TRCValidationDict.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string Write(Contracts.Results.ExtractionResult result, string directory)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        var document = TrcMapper.Map(result);
        var json = document.Fiches.ToJsonString(Options);

        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, FileName);
        var temporary = Path.Combine(directory, $".{FileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, json, new UTF8Encoding(false));
            File.Move(temporary, target, overwrite: true);
        }
        catch
        {
            try
            {
                File.Delete(temporary);
            }
            catch (IOException)
            {
            }

            throw;
        }

        if (document.Annexes.Count > 0)
        {
            var annexes = Directory.CreateDirectory(Path.Combine(directory, TrcMapper.AnnexDirectory)).FullName;
            foreach (var annex in document.Annexes)
            {
                File.WriteAllText(Path.Combine(annexes, annex.FileName), annex.Text, new UTF8Encoding(false));
            }
        }

        return FileName;
    }
}
