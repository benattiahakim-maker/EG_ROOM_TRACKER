using System.IO.Compression;
using System.Text;
using EgRoomTracker.Application;
using EgRoomTracker.Application.Configuration;

namespace EgRoomTracker.WorkItemRunner.Services;

/// <summary>Résultat d'un contrôle local : erreurs bloquantes et avertissements (affichés dans le plan).</summary>
public sealed record CheckResult(IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// Contrôles locaux avant soumission (docs/INPUTS.md) : ils évitent de payer un WorkItem voué à l'échec. Un
/// <c>settings.json</c> invalide n'est qu'un avertissement, car certains tests de recette le provoquent volontairement
/// (D6 : <c>schemaVersion: "2.0"</c>).
/// </summary>
public static class InputChecks
{
    /// <summary>Longueur relative au-delà de laquelle un chemin du ZIP risque la limite Windows de 260 caractères.</summary>
    public const int LongPathThreshold = 150;

    /// <summary>Taille maximale d'un fichier envoyé (au-delà, préférer un transfert dédié).</summary>
    public const long MaxZipBytes = 5L * 1024 * 1024 * 1024;

    public static CheckResult CheckZip(string zipPath)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        if (!File.Exists(zipPath))
        {
            return new CheckResult([$"ZIP introuvable : {Path.GetFileName(zipPath)}"], []);
        }

        if (new FileInfo(zipPath).Length > MaxZipBytes)
        {
            errors.Add("ZIP trop volumineux (5 Gio au plus).");
        }

        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            var names = archive.Entries.Select(e => e.FullName).ToList();
            var models = names.Where(n => n.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase) && !n.Contains("__MACOSX/", StringComparison.Ordinal)).ToList();
            if (models.Count == 0)
            {
                errors.Add("aucun fichier .rvt dans le ZIP (RVT_NOT_FOUND).");
            }
            else if (models.Count > 1)
            {
                warnings.Add($"{models.Count} fichiers .rvt dans le ZIP : renseigner mainModelPath, sinon RVT_AMBIGUOUS (sauvegardes Nom.NNNN.rvt exclues si l'original est présent).");
            }

            var longest = names.DefaultIfEmpty(string.Empty).MaxBy(n => n.Length)!;
            if (longest.Length > LongPathThreshold)
            {
                warnings.Add($"chemin de {longest.Length} caractères dans le ZIP : risque de dépasser 260 caractères sur la machine APS (docs/INPUTS.md).");
            }

            var hasCodepage = names.Any(n => n.EndsWith(".codepage", StringComparison.OrdinalIgnoreCase));
            var nonAsciiWithoutUtf8 = archive.Entries.Any(e => e.FullName.Any(c => c > 127)) && !hasCodepage;
            if (nonAsciiWithoutUtf8)
            {
                warnings.Add("noms non ASCII dans le ZIP : vérifier que l'archive est en UTF-8 (7-Zip cu) ou ajouter un fichier XYZ.codepage (docs/INPUTS.md).");
            }
        }
        catch (InvalidDataException)
        {
            errors.Add("ZIP illisible.");
        }

        return new CheckResult(errors, warnings);
    }

    /// <summary>Validation par <see cref="SettingsReader"/> (mêmes règles que l'add-in) ; erreur rapportée en avertissement.</summary>
    public static CheckResult CheckSettings(string settingsJson)
    {
        if (string.IsNullOrWhiteSpace(settingsJson))
        {
            return new CheckResult(["settings.json vide."], []);
        }

        if (Encoding.UTF8.GetByteCount(settingsJson) > SettingsReader.MaxSizeBytes)
        {
            return new CheckResult([$"settings.json dépasse {SettingsReader.MaxSizeBytes} octets."], []);
        }

        try
        {
            SettingsReader.Parse(settingsJson);
            return new CheckResult([], []);
        }
        catch (ExtractionFailedException exception)
        {
            return new CheckResult([], [$"settings.json refusé par l'extracteur ({exception.Error.Code}) : {exception.Message} Le WorkItem échouera (utile seulement pour un test volontaire, par exemple D6)."]);
        }
    }
}
