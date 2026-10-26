using EgRoomTracker.Contracts.Codes;
using EgRoomTracker.Contracts.Results;

namespace EgRoomTracker.Application.Revit;

/// <summary>Anomalies de portée document produites à l'ouverture (section 9.1).</summary>
public static class DocumentAnomalies
{
    /// <summary>Nombre maximal de descriptions distinctes détaillées par anomalie agrégée.</summary>
    public const int MaxDetailedMessages = 20;

    /// <summary>Texte de l'information <c>VOLUME_NOT_COMPUTED</c> (sortie generic et journal) ; reste très en dessous de 400 caractères.</summary>
    public static string VolumeNotComputedMessage(int rooms) =>
        $"{rooms.ToString(System.Globalization.CultureInfo.InvariantCulture)} pièce(s) sans volume calculé : calcul des volumes désactivé dans le document (volumeM3 exporté à null ; mode preserve).";

    /// <summary>
    /// Nombre de pièces de l'anomalie <c>VOLUME_NOT_COMPUTED</c> (<c>Details["roomsAffected"]</c>), seul calcul du nombre : le journal et
    /// l'enveloppe generic le lisent ici et écrivent donc le même message. Null si l'anomalie est absente.
    /// </summary>
    public static int? VolumeNotComputedRooms(IEnumerable<AnomalyDto> anomalies)
    {
        var anomaly = anomalies.FirstOrDefault(a => a.Code == DocumentAnomalyCodes.VolumeNotComputed);
        if (anomaly is null)
        {
            return null;
        }

        return anomaly.Details.TryGetValue("roomsAffected", out var count) && count is not null
            ? Convert.ToInt32(count, System.Globalization.CultureInfo.InvariantCulture)
            : 0;
    }

    /// <summary>
    /// Nom de fichier seul (après le dernier <c>/</c> ou <c>\</c>) : un chemin de lien n'est jamais écrit dans un résultat, il vient
    /// de la machine d'origine. Null ou vide : null.
    /// </summary>
    public static string? FileNameOnly(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var name = path.Trim()[(path.Trim().LastIndexOfAny(['/', '\\']) + 1)..];
        return name.Length == 0 ? null : name;
    }

    /// <summary>Un lien Revit référencé mais absent du ZIP : une anomalie par lien. <c>linkName</c> et <c>linkPath</c> sont des noms de fichier.</summary>
    public static AnomalyDto LinkMissing(string linkName, string? linkPath)
    {
        var name = FileNameOnly(linkName) ?? linkName;
        return new()
        {
            Code = DocumentAnomalyCodes.LinkMissing,
            Severity = AnomalySeverity.Warning,
            Scope = AnomalyScope.Document,
            Message = $"Lien Revit absent : {name}.",
            Details = new Dictionary<string, object?> { ["linkName"] = name, ["linkPath"] = FileNameOnly(linkPath) },
        };
    }

    /// <summary>
    /// Un lien Revit présent mais déchargé (statut <c>Unloaded</c> ou <c>LocallyUnloaded</c>) : information,
    /// sans effet sur le statut. Les pièces des liens ne sont de toute façon pas collectées (MVP).
    /// </summary>
    public static AnomalyDto LinkNotLoaded(string linkName, string? linkPath, string linkStatus)
    {
        var name = FileNameOnly(linkName) ?? linkName;
        return new()
        {
            Code = DocumentAnomalyCodes.LinkNotLoaded,
            Severity = AnomalySeverity.Info,
            Scope = AnomalyScope.Document,
            Message = $"Lien Revit présent mais non chargé : {name}.",
            Details = new Dictionary<string, object?> { ["linkName"] = name, ["linkPath"] = FileNameOnly(linkPath), ["linkStatus"] = linkStatus },
        };
    }

    /// <summary>
    /// Avertissements Revit tolérés à l'ouverture (D2), agrégés en une anomalie : nombre total et descriptions
    /// distinctes (limitées) avec leur nombre d'occurrences. Null si aucun avertissement.
    /// </summary>
    public static AnomalyDto? RevitWarningsOnOpen(IReadOnlyCollection<string> descriptions)
    {
        if (descriptions.Count == 0)
        {
            return null;
        }

        var distinct = descriptions
            .GroupBy(d => d, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .ToList();
        var detailed = distinct
            .Take(MaxDetailedMessages)
            .Select(g => (object?)new Dictionary<string, object?> { ["description"] = g.Key, ["count"] = g.Count() })
            .ToList();

        return new AnomalyDto
        {
            Code = DocumentAnomalyCodes.RevitWarningOnOpen,
            Severity = AnomalySeverity.Warning,
            Scope = AnomalyScope.Document,
            Message = $"{descriptions.Count} avertissement(s) Revit toléré(s) à l'ouverture.",
            Details = new Dictionary<string, object?>
            {
                ["warningCount"] = descriptions.Count,
                ["distinctCount"] = distinct.Count,
                ["warnings"] = detailed,
            },
        };
    }
}
