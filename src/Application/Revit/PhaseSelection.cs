using System.Globalization;
using EgRoomTracker.Contracts.Codes;

namespace EgRoomTracker.Application.Revit;

/// <summary>
/// Filtre explicite de phases (<c>settings.phaseIds</c>, section 2) : null ou absent = toutes les phases ;
/// liste vide ou phase absente du document = configuration invalide (CONFIG_INVALID).
/// Les identifiants arrivent déjà validés et typés par EGR-05 (<c>ExtractionSettings.PhaseElementIds</c>).
/// </summary>
public static class PhaseSelection
{
    /// <summary>
    /// Ensemble des ElementId de phase demandés ; null = aucune restriction.
    /// Contrôle défensif uniquement : <c>[]</c> est déjà refusé par la validation de configuration (EGR-05) ;
    /// s'il arrivait quand même, CONFIG_INVALID (étape <c>configuration</c>) plutôt qu'un résultat vide.
    /// </summary>
    public static IReadOnlySet<long>? From(IReadOnlyList<long>? phaseElementIds)
    {
        if (phaseElementIds is null)
        {
            return null;
        }

        if (phaseElementIds.Count == 0)
        {
            throw new ExtractionFailedException(ErrorCodes.ConfigInvalid, Stages.Configuration,
                "phaseIds : liste vide ; utiliser null ou omettre la clé pour toutes les phases.");
        }

        return new HashSet<long>(phaseElementIds);
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
