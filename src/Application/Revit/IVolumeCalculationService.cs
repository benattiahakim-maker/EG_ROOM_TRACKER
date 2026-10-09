using EgRoomTracker.Contracts.Configuration;

namespace EgRoomTracker.Application.Revit;

/// <summary>
/// État du calcul des volumes : <c>preserve</c> lit le réglage du document sans le modifier ; <c>compute</c> (EGR-16)
/// active le calcul dans une transaction sur la copie de travail, régénère, lit les volumes puis annule la
/// transaction (le RVT n'est jamais sauvegardé).
/// </summary>
public interface IVolumeCalculationService
{
    VolumeComputationState Prepare(VolumeMode mode);
}

/// <summary>
/// État du calcul des volumes pour l'exécution.
/// <see cref="VolumesComputed"/> : les volumes exportés sont issus d'un calcul Revit actif (réglage du document ou
/// calcul temporaire EGR-16).
/// </summary>
public sealed record VolumeComputationState(VolumeMode Mode, bool VolumesComputed)
{
    /// <summary>Réglage <c>ComputeVolumes</c> du document tel que trouvé à l'ouverture ; null si non lu.</summary>
    public bool? DocumentComputeVolumes { get; init; }

    /// <summary>Vrai si le calcul a été activé temporairement (mode compute, réglage du document désactivé).</summary>
    public bool ComputeApplied { get; init; }

    /// <summary>Résultat de l'annulation de la transaction temporaire ; null si aucune transaction n'a été ouverte.</summary>
    public bool? TransactionRolledBack { get; init; }

    /// <summary>Motif d'échec de l'activation du calcul en mode compute ; null si pas d'échec.</summary>
    public string? ComputeFailure { get; init; }

    /// <summary>
    /// Volumes bruts (unités internes) lus pendant la transaction temporaire, par ElementId ; null hors calcul temporaire.
    /// Les volumes doivent être lus avant l'annulation, qui restaure l'état du modèle.
    /// </summary>
    public IReadOnlyDictionary<long, double>? ComputedVolumes { get; init; }

    /// <summary>
    /// Source du volume d'une pièce (EGR-16) :
    /// échec du calcul temporaire → volume non disponible ; calcul temporaire → volume lu avant annulation
    /// (absent → non disponible) ; sinon lecture directe selon le réglage du document.
    /// </summary>
    public (bool VolumesComputed, Func<double?> ReadVolume) VolumeSource(long elementId, Func<double?> readLive)
    {
        if (ComputeFailure is not null)
        {
            return (true, static () => null);
        }

        if (ComputedVolumes is { } cache)
        {
            return (true, () => cache.TryGetValue(elementId, out var volume) ? volume : null);
        }

        return (VolumesComputed, readLive);
    }
}
