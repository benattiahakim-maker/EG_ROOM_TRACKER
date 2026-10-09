using System.Text.RegularExpressions;
using EgRoomTracker.Contracts.Codes;

namespace EgRoomTracker.Application.Configuration;

/// <summary>
/// Résolution du RVT principal dans le répertoire d'entrée (ZIP décompressé par l'Activity), section 2 et décision D4.
/// <list type="bullet">
/// <item><c>mainModelPath</c> renseigné : chemin relatif confiné au répertoire d'entrée, y compris après résolution
/// des liens symboliques ; fichier <c>.rvt</c> existant.</item>
/// <item><c>mainModelPath</c> null : détection automatique uniquement si le répertoire contient exactement un RVT
/// candidat ; jamais de choix arbitraire entre plusieurs RVT.</item>
/// </list>
/// Retourne le chemin relatif normalisé (séparateur <c>/</c>), sans composant <c>.</c> ni <c>..</c>.
/// </summary>
public static partial class MainModelResolver
{
    /// <summary>Nombre maximal de candidats cités dans le message RVT_AMBIGUOUS.</summary>
    public const int MaxListedCandidates = 10;

    public static string Resolve(string inputDirectory, string? mainModelPath)
    {
        var root = Path.GetFullPath(inputDirectory);
        if (!Directory.Exists(root))
        {
            throw new ExtractionFailedException(ErrorCodes.RvtNotFound, Stages.Input, "Répertoire d'entrée absent : ZIP non décompressé ou vide.");
        }

        return mainModelPath is null ? Detect(root) : ResolveExplicit(root, mainModelPath);
    }

    /// <summary>Vrai si le chemin est un RVT candidat à la détection automatique (ni sauvegarde Revit, ni métadonnée macOS).</summary>
    public static bool IsCandidate(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var fileName = segments.Length > 0 ? segments[^1] : string.Empty;
        return fileName.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase)
            && !BackupPattern().IsMatch(fileName)
            && !fileName.StartsWith("._", StringComparison.Ordinal)
            && !segments.Contains("__MACOSX", StringComparer.OrdinalIgnoreCase);
    }

    private static string ResolveExplicit(string root, string mainModelPath)
    {
        var relative = mainModelPath.Replace('\\', '/');
        if (relative.IndexOfAny(['\0', '*', '?', '"', '<', '>', '|']) >= 0)
        {
            throw new ExtractionFailedException(ErrorCodes.ConfigInvalid, Stages.Configuration, "settings.json : mainModelPath contient des caractères interdits.");
        }

        if (relative.StartsWith('/') || DrivePattern().IsMatch(relative) || Path.IsPathRooted(relative))
        {
            throw OutsideInput("mainModelPath doit être relatif au répertoire d'entrée (chemin absolu refusé).");
        }

        var fullPath = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsWithin(root, fullPath))
        {
            throw OutsideInput("mainModelPath sort du répertoire d'entrée.");
        }

        var normalized = ToRelative(root, fullPath);
        if (!normalized.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase))
        {
            throw new ExtractionFailedException(ErrorCodes.RvtNotFound, Stages.Input, $"mainModelPath « {normalized} » ne désigne pas un fichier .rvt.");
        }

        if (!File.Exists(fullPath))
        {
            throw new ExtractionFailedException(ErrorCodes.RvtNotFound, Stages.Input, $"RVT principal introuvable dans le ZIP : {normalized}.");
        }

        if (!IsWithin(RealPath(root), RealPath(fullPath)))
        {
            throw OutsideInput($"mainModelPath « {normalized} » pointe, via un lien symbolique, hors du répertoire d'entrée.");
        }

        return normalized;
    }

    private static string Detect(string root)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            MatchCasing = MatchCasing.CaseInsensitive,
            MatchType = MatchType.Simple,

            // Les liens symboliques (fichiers ou dossiers) ne sont pas suivis : rien hors du répertoire d'entrée.
            AttributesToSkip = FileAttributes.ReparsePoint,
        };

        var candidates = Directory.EnumerateFiles(root, "*.rvt", options)
            .Select(path => ToRelative(root, path))
            .Where(IsCandidate)
            .Order(StringComparer.Ordinal)
            .ToList();

        return candidates.Count switch
        {
            0 => throw new ExtractionFailedException(ErrorCodes.RvtNotFound, Stages.Input, "Aucun fichier .rvt dans le ZIP."),
            1 => candidates[0],
            _ => throw new ExtractionFailedException(
                ErrorCodes.RvtAmbiguous,
                Stages.Input,
                $"{candidates.Count} fichiers .rvt dans le ZIP ; préciser mainModelPath dans settings.json. Candidats : "
                + string.Join(", ", candidates.Take(MaxListedCandidates))
                + (candidates.Count > MaxListedCandidates ? ", …" : "") + "."),
        };
    }

    private static bool IsWithin(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative != ".."
            && !Path.IsPathRooted(relative)
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !relative.StartsWith("../", StringComparison.Ordinal);
    }

    private static string ToRelative(string root, string fullPath) =>
        Path.GetRelativePath(root, fullPath).Replace(Path.DirectorySeparatorChar, '/');

    /// <summary>Chemin réel : chaque composant qui est un lien symbolique est remplacé par sa cible finale.</summary>
    private static string RealPath(string fullPath)
    {
        var pathRoot = Path.GetPathRoot(fullPath)!;
        var current = pathRoot;
        var parts = fullPath[pathRoot.Length..].Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var next = Path.Combine(current, part);
            FileSystemInfo info = Directory.Exists(next) ? new DirectoryInfo(next) : new FileInfo(next);
            if (info.LinkTarget is not null)
            {
                next = info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? next;
            }

            current = Path.GetFullPath(next);
        }

        return current;
    }

    private static ExtractionFailedException OutsideInput(string message) =>
        new(ErrorCodes.PathOutsideInput, Stages.Configuration, $"settings.json : {message}");

    /// <summary>Copies de sauvegarde Revit : <c>Nom.0001.rvt</c>.</summary>
    [GeneratedRegex(@"\.[0-9]{4}\.rvt$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BackupPattern();

    [GeneratedRegex(@"^[A-Za-z]:", RegexOptions.CultureInvariant)]
    private static partial Regex DrivePattern();
}
