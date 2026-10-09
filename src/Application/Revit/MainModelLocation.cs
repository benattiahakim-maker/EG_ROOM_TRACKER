using EgRoomTracker.Contracts.Codes;

namespace EgRoomTracker.Application.Revit;

/// <summary>
/// Chemin du RVT principal à ouvrir, à partir du chemin relatif résolu par la configuration (EGR-05).
/// Contrôle défensif : le chemin reste confiné au répertoire d'entrée et le fichier existe.
/// </summary>
public static class MainModelLocation
{
    /// <summary>Retourne le chemin absolu et le chemin relatif normalisé (séparateur <c>/</c>).</summary>
    public static (string FullPath, string RelativePath) Resolve(string inputDirectory, string? mainModelPath)
    {
        if (string.IsNullOrWhiteSpace(mainModelPath))
        {
            throw new ExtractionFailedException(ErrorCodes.RvtNotFound, Stages.Input, "RVT principal non résolu par la configuration.");
        }

        var root = Path.GetFullPath(inputDirectory);
        var fullPath = Path.GetFullPath(Path.Combine(root, mainModelPath));
        var relative = Path.GetRelativePath(root, fullPath);
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || relative.StartsWith("../", StringComparison.Ordinal))
        {
            throw new ExtractionFailedException(ErrorCodes.PathOutsideInput, Stages.Configuration, "Chemin du RVT principal hors du répertoire d'entrée.");
        }

        if (!string.Equals(Path.GetExtension(fullPath), ".rvt", StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
        {
            throw new ExtractionFailedException(ErrorCodes.RvtNotFound, Stages.Input, $"RVT principal introuvable : {relative.Replace('\\', '/')}.");
        }

        return (fullPath, relative.Replace('\\', '/'));
    }
}
