using System.Globalization;
using EgRoomTracker.Contracts.Codes;

namespace EgRoomTracker.Application.Revit;

/// <summary>
/// Filtre explicite de phases (<c>settings.phaseIds</c>) : null ou absent = toutes les phases (section 2).
/// </summary>
public static class PhaseSelection
{
    /// <summary>
    /// Convertit les ElementId de phase (chaînes) en valeurs entières ; null = aucune restriction.
    /// Une valeur non entière est une configuration invalide (contrôle défensif, la validation relève d'EGR-05).
    /// </summary>
    public static IReadOnlySet<long>? Parse(IReadOnlyList<string>? phaseIds)
    {
        if (phaseIds is null)
        {
            return null;
        }

        var result = new HashSet<long>();
        foreach (var id in phaseIds)
        {
            if (!long.TryParse(id, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
            {
                throw new ExtractionFailedException(ErrorCodes.ConfigInvalid, Stages.Configuration, $"phaseIds : ElementId invalide « {id} ».");
            }

            result.Add(value);
        }

        return result;
    }
}
