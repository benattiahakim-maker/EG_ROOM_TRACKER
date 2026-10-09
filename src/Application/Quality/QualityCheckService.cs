using System.Globalization;
using EgRoomTracker.Application.Revit;
using EgRoomTracker.Contracts.Codes;
using EgRoomTracker.Contracts.Results;

namespace EgRoomTracker.Application.Quality;

/// <summary>
/// Indicateurs qualité et statut final (EGR-11, sections 3, 9.1 et 9.3), sans appel Revit.
/// Répartition : UNPLACED, MISSING_PARAMETER, LEVEL_NOT_FOUND et EXTRACTION_ERROR arrivent de la lecture et sont
/// conservés ; ce service ajoute DUPLICATE_NUMBER, ZERO_AREA et VOLUME_NOT_COMPUTED, puis calcule le statut.
/// Aucune pièce n'est supprimée, fusionnée ni réordonnée.
/// </summary>
public sealed class QualityCheckService : IQualityCheckService
{
    public QualityReport Evaluate(
        IReadOnlyList<RoomDto> rooms,
        IReadOnlyList<AnomalyDto> anomalies,
        VolumeComputationState volumeState)
    {
        var flags = rooms.Select(r => r.QualityFlags.Distinct(StringComparer.Ordinal).ToList()).ToArray();
        var added = rooms.Select(_ => new List<AnomalyDto>()).ToArray();

        // ZERO_AREA : surface nulle (0, pas null) sur une pièce dont le non-placement n'est pas vérifié.
        for (var i = 0; i < rooms.Count; i++)
        {
            if (rooms[i].AreaM2 == 0 && !flags[i].Contains(QualityFlagCodes.Unplaced))
            {
                AddFlag(flags[i], added[i], rooms[i], QualityFlagCodes.ZeroArea,
                    "Surface nulle sur une pièce placée (non fermée ou redondante).",
                    new Dictionary<string, object?> { ["areaM2"] = 0.0 });
            }
        }

        // DUPLICATE_NUMBER : même numéro (comparaison exacte, zéros initiaux significatifs) et même phase ;
        // numéros vides ou absents ignorés ; deux pièces de variantes différentes d'un même jeu ne coexistent
        // jamais et ne sont pas en doublon (CanCoexist). Posé sur chaque pièce en conflit avec au moins une autre ;
        // elementIds = la pièce et celles avec lesquelles elle est en conflit.
        var groups = Enumerable.Range(0, rooms.Count)
            .Where(i => !string.IsNullOrEmpty(rooms[i].Number))
            .GroupBy(i => (Phase: rooms[i].Phase?.ElementId, Number: rooms[i].Number!))
            .Where(g => g.Count() > 1);
        foreach (var group in groups)
        {
            var members = group.ToList();
            foreach (var i in members)
            {
                var conflicts = members.Where(j => j == i || CanCoexist(rooms[i], rooms[j])).ToList();
                if (conflicts.Count < 2)
                {
                    continue;
                }

                var elementIds = conflicts.Select(j => rooms[j].ElementId).OrderBy(NumericKey).ThenBy(id => id, StringComparer.Ordinal).ToArray();
                var details = new Dictionary<string, object?> { ["number"] = group.Key.Number, ["elementIds"] = elementIds };
                if (conflicts.Any(j => j != i && ConflictRestsOnUnknownSet(rooms[i], rooms[j])))
                {
                    // Jeu d'options illisible : doublon signalé par prudence, marqué pour revue.
                    details[OptionSetUnknownDetail] = true;
                }

                AddFlag(flags[i], added[i], rooms[i], QualityFlagCodes.DuplicateNumber,
                    $"Numéro {group.Key.Number} partagé par {elementIds.Length} pièces.",
                    details);
            }
        }

        var documentAnomalies = anomalies.Where(a => a.Scope == AnomalyScope.Document).ToList();
        if (volumeState.ComputeFailure is not null)
        {
            // volumeMode compute (EGR-16) : activation temporaire impossible, volumes exportés en notAvailable.
            documentAnomalies.Add(new AnomalyDto
            {
                Code = DocumentAnomalyCodes.VolumeComputeFailed,
                Severity = AnomalySeverity.Warning,
                Scope = AnomalyScope.Document,
                ElementId = null,
                Message = "Activation temporaire du calcul des volumes impossible ; volumeM3 exporté à null (mode compute).",
                Details = new Dictionary<string, object?>
                {
                    ["volumeMode"] = volumeState.Mode.ToString().ToLowerInvariant(),
                    ["reason"] = volumeState.ComputeFailure,
                    ["roomsAffected"] = rooms.Count(r => r.VolumeStatus == VolumeStatus.NotAvailable),
                },
            });
        }
        else if (!volumeState.VolumesComputed)
        {
            documentAnomalies.Add(new AnomalyDto
            {
                Code = DocumentAnomalyCodes.VolumeNotComputed,
                Severity = AnomalySeverity.Info,
                Scope = AnomalyScope.Document,
                ElementId = null,
                Message = "Calcul des volumes désactivé dans le document ; volumeM3 exporté à null (mode preserve).",
                Details = new Dictionary<string, object?>
                {
                    ["volumeMode"] = volumeState.Mode.ToString().ToLowerInvariant(),
                    ["roomsAffected"] = rooms.Count(r => r.VolumeStatus == VolumeStatus.NotComputed),
                },
            });
        }

        // Ordre : anomalies document, puis anomalies pièce dans l'ordre des pièces (lecture puis qualité).
        var roomAnomalies = anomalies.Where(a => a.Scope == AnomalyScope.Room).ToList();
        var ordered = new List<AnomalyDto>(documentAnomalies);
        var placed = new HashSet<AnomalyDto>(ReferenceEqualityComparer.Instance);
        for (var i = 0; i < rooms.Count; i++)
        {
            foreach (var anomaly in roomAnomalies.Where(a => a.ElementId == rooms[i].ElementId && placed.Add(a)))
            {
                ordered.Add(anomaly);
            }

            ordered.AddRange(added[i]);
        }

        ordered.AddRange(roomAnomalies.Where(a => !placed.Contains(a)));

        var resultRooms = rooms.Select((r, i) => r with { QualityFlags = flags[i] }).ToList();
        return new QualityReport(resultRooms, ordered, StatusOf(ordered));
    }

    /// <summary>
    /// Deux pièces de même phase peuvent-elles coexister dans un même état du modèle ?
    /// Oui si l'une est dans le modèle principal (sans variante), si elles sont dans la même variante, ou dans des
    /// variantes de jeux différents (une vue affiche une variante par jeu). Non si elles sont dans deux variantes
    /// différentes d'un même jeu (mutuellement exclusives). Jeu inconnu pour l'une des deux : coexistence supposée
    /// (doublon signalé plutôt que manqué).
    /// </summary>
    internal static bool CanCoexist(RoomDto a, RoomDto b)
    {
        if (a.DesignOption is null || b.DesignOption is null)
        {
            return true;
        }

        if (string.Equals(a.DesignOption.ElementId, b.DesignOption.ElementId, StringComparison.Ordinal))
        {
            return true;
        }

        return a.DesignOptionSetId is null
            || b.DesignOptionSetId is null
            || !string.Equals(a.DesignOptionSetId, b.DesignOptionSetId, StringComparison.Ordinal);
    }

    /// <summary>
    /// Détail <c>optionSetUnknown: true</c> d'une anomalie DUPLICATE_NUMBER : le conflit oppose deux variantes
    /// différentes dont le jeu d'options n'a pas pu être lu pour au moins l'une ; présent seulement dans ce cas.
    /// </summary>
    public const string OptionSetUnknownDetail = "optionSetUnknown";

    /// <summary>Le conflit entre deux pièces ne tient qu'à un jeu d'options inconnu (variantes différentes, jeu illisible).</summary>
    internal static bool ConflictRestsOnUnknownSet(RoomDto a, RoomDto b) =>
        a.DesignOption is not null
        && b.DesignOption is not null
        && !string.Equals(a.DesignOption.ElementId, b.DesignOption.ElementId, StringComparison.Ordinal)
        && (a.DesignOptionSetId is null || b.DesignOptionSetId is null);

    /// <summary>Statut (9.3) : une anomalie warning ou error donne succeededWithWarnings ; info seule, succeeded. Jamais failed ici.</summary>
    internal static ExtractionStatus StatusOf(IEnumerable<AnomalyDto> anomalies) =>
        anomalies.Any(a => a.Severity is AnomalySeverity.Warning or AnomalySeverity.Error)
            ? ExtractionStatus.SucceededWithWarnings
            : ExtractionStatus.Succeeded;

    private static void AddFlag(List<string> flags, List<AnomalyDto> added, RoomDto room, string code, string message, IReadOnlyDictionary<string, object?> details)
    {
        if (flags.Contains(code))
        {
            return;
        }

        flags.Add(code);
        added.Add(new AnomalyDto
        {
            Code = code,
            Severity = AnomalySeverity.Warning,
            Scope = AnomalyScope.Room,
            ElementId = room.ElementId,
            Message = message,
            Details = details,
        });
    }

    private static long NumericKey(string elementId) =>
        long.TryParse(elementId, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value) ? value : long.MaxValue;
}
