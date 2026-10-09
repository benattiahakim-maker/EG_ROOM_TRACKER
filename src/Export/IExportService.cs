using EgRoomTracker.Contracts.Results;

namespace EgRoomTracker.Export;

/// <summary>
/// Sérialisation déterministe de <c>result.json</c> (UTF-8 sans BOM) et publication locale atomique
/// (fichier temporaire puis renommage). Implémentation : EGR-12.
/// </summary>
public interface IExportService
{
    /// <summary>Écrit le résultat au chemin indiqué ; lève <see cref="IOException"/> en cas d'échec d'écriture.</summary>
    void Write(ExtractionResult result, string outputPath);
}
