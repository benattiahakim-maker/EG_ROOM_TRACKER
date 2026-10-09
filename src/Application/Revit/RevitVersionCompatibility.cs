using System.Globalization;
using EgRoomTracker.Contracts.Codes;

namespace EgRoomTracker.Application.Revit;

/// <summary>
/// Contrôle de version avant ouverture, à partir des informations de fichier (<c>BasicFileInfo</c>) :
/// un RVT enregistré dans une version postérieure au moteur est refusé (REVIT_VERSION_INCOMPATIBLE).
/// Un RVT antérieur est mis à niveau en mémoire uniquement (jamais sauvegardé).
/// </summary>
public static class RevitVersionCompatibility
{
    public static FileVersionCheck Check(string? fileFormat, bool savedInLaterVersion, string engineVersion)
    {
        var fileYear = ParseYear(fileFormat);
        var engineYear = ParseYear(engineVersion);
        if (savedInLaterVersion || (fileYear is not null && engineYear is not null && fileYear > engineYear))
        {
            throw new ExtractionFailedException(
                ErrorCodes.RevitVersionIncompatible,
                Stages.Document,
                $"RVT enregistré avec Revit {fileFormat ?? "?"}, postérieur au moteur Revit {engineVersion}.");
        }

        var upgraded = fileYear is not null && engineYear is not null && fileYear < engineYear;
        return new FileVersionCheck(fileYear?.ToString(CultureInfo.InvariantCulture), upgraded);
    }

    /// <summary>Extrait l'année (4 chiffres) d'un format Revit, par exemple <c>2026</c> ou <c>Autodesk Revit 2026</c>.</summary>
    internal static int? ParseYear(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        for (var i = 0; i + 4 <= value.Length; i++)
        {
            if (int.TryParse(value.AsSpan(i, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var year)
                && year is >= 2000 and <= 2099
                && (i == 0 || !char.IsDigit(value[i - 1]))
                && (i + 4 == value.Length || !char.IsDigit(value[i + 4])))
            {
                return year;
            }
        }

        return null;
    }
}

/// <summary>Résultat du contrôle : année du fichier si connue, mise à niveau en mémoire nécessaire.</summary>
public sealed record FileVersionCheck(string? FileYear, bool UpgradedInMemory);
