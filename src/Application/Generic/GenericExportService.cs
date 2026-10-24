using System.Text;
using EgRoomTracker.Contracts.Results;

namespace EgRoomTracker.Application.Generic;

/// <summary>Écriture de <c>result.generic.json</c> (enveloppe commune de la plateforme) dans le dossier de sortie.</summary>
public interface IGenericExportService
{
    /// <summary>
    /// Écrit <c>result.generic.json</c> dans <paramref name="directory"/> et retourne son nom. Les propriétés <c>image</c> ne
    /// référencent que les fichiers réellement présents dans <paramref name="imagesDirectory"/> (null : aucune image).
    /// </summary>
    string Write(ExtractionResult result, string directory, string? imagesDirectory);
}

/// <summary>Écrit le fichier de façon atomique (fichier temporaire puis déplacement), UTF-8 sans BOM : un échec ne laisse aucun fichier partiel.</summary>
public sealed class GenericExportService : IGenericExportService
{
    /// <summary>Nom du fichier dans <c>result/</c> (et dans le ZIP de sortie).</summary>
    public const string FileName = "result.generic.json";

    public string Write(ExtractionResult result, string directory, string? imagesDirectory)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        var context = new GenericMapContext
        {
            ImageFileNames = ListImages(imagesDirectory),
            ImagesDirectory = imagesDirectory is null ? "Images" : Path.GetFileName(Path.TrimEndingDirectorySeparator(imagesDirectory)),
        };
        var json = GenericJson.Serialize(GenericMapper.Map(result, context));

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

        return FileName;
    }

    private static HashSet<string>? ListImages(string? imagesDirectory) =>
        imagesDirectory is not null && Directory.Exists(imagesDirectory)
            ? Directory.EnumerateFiles(imagesDirectory).Select(Path.GetFileName).OfType<string>().ToHashSet(StringComparer.Ordinal)
            : null;
}
