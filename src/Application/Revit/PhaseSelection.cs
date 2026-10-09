using System.Globalization;
using EgRoomTracker.Contracts.Codes;

namespace EgRoomTracker.Application.Revit;

/// <summary>
/// Filtre explicite de phases (<c>settings.phaseIds</c>, section 2) : null ou absent = toutes les phases ;
/// liste vide ou phase absente du document = configuration invalide (CONFIG_INVALID).
/// </summary>
public static class PhaseSelection
{
    /// <summary>
    /// Convertit les ElementId de phase (chaînes) en valeurs entières ; null = aucune restriction.
    /// Contrôle défensif uniquement : la validation de <c>settings.json</c>, y compris le format entier des
    /// <c>phaseIds</c>, relève d'EGR-05 (<c>IInputConfigurationService</c>).
    /// </summary>
    public static IReadOnlySet<long>? Parse(IReadOnlyList<string>? phaseIds)
    {
        if (phaseIds is null)
        {
            return null;
        }

        if (phaseIds.Count == 0)
        {
            throw new ExtractionFailedException(ErrorCodes.ConfigInvalid, Stages.Configuration,
                "phaseIds : liste vide ; utiliser null ou omettre la clé pour toutes les phases.");
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

    /// <summary>
    /// Vérifie que toutes les phases demandées existent dans le document ; sinon échec global CONFIG_INVALID
    /// (étape <c>collection</c>) listant les identifiants inconnus. Null = toutes les phases, toujours valide.
    /// </summary>
    public static void EnsureKnown(IReadOnlySet<long>? requested, IReadOnlySet<long> documentPhaseIds)
    {
        if (requested is null)
        {
            return;
        }

        var unknown = requested.Where(id => !documentPhaseIds.Contains(id)).Order().ToArray();
        if (unknown.Length > 0)
        {
            var list = string.Join(", ", unknown.Select(id => id.ToString(CultureInfo.InvariantCulture)));
            throw new ExtractionFailedException(ErrorCodes.ConfigInvalid, Stages.Collection,
                $"phaseIds : phase(s) absente(s) du document : {list}.");
        }
    }
}
