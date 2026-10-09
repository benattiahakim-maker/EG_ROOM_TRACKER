using EgRoomTracker.Contracts.Codes;
using EgRoomTracker.Contracts.Results;

namespace EgRoomTracker.Application.Revit;

/// <summary>Anomalies de portée document produites à l'ouverture (section 9.1).</summary>
public static class DocumentAnomalies
{
    /// <summary>Nombre maximal de descriptions distinctes détaillées par anomalie agrégée.</summary>
    public const int MaxDetailedMessages = 20;

    /// <summary>Un lien Revit référencé mais absent du ZIP : une anomalie par lien.</summary>
    public static AnomalyDto LinkMissing(string linkName, string? linkPath) => new()
    {
        Code = DocumentAnomalyCodes.LinkMissing,
        Severity = AnomalySeverity.Warning,
        Scope = AnomalyScope.Document,
        Message = $"Lien Revit absent : {linkName}.",
        Details = new Dictionary<string, object?> { ["linkName"] = linkName, ["linkPath"] = linkPath },
    };

    /// <summary>
    /// Un lien Revit présent mais déchargé (statut <c>Unloaded</c> ou <c>LocallyUnloaded</c>) : information,
    /// sans effet sur le statut. Les pièces des liens ne sont de toute façon pas collectées (MVP).
    /// </summary>
    public static AnomalyDto LinkNotLoaded(string linkName, string? linkPath, string linkStatus) => new()
    {
        Code = DocumentAnomalyCodes.LinkNotLoaded,
        Severity = AnomalySeverity.Info,
        Scope = AnomalyScope.Document,
        Message = $"Lien Revit présent mais non chargé : {linkName}.",
        Details = new Dictionary<string, object?> { ["linkName"] = linkName, ["linkPath"] = linkPath, ["linkStatus"] = linkStatus },
    };

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
