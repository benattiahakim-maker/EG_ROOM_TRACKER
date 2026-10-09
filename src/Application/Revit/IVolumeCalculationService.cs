using EgRoomTracker.Contracts.Configuration;

namespace EgRoomTracker.Application.Revit;

/// <summary>
/// État du calcul des volumes ; en mode compute, transaction sur la copie de travail sans sauvegarde (EGR-16).
/// </summary>
public interface IVolumeCalculationService
{
    VolumeComputationState Prepare(VolumeMode mode);
}

/// <summary>État du calcul des volumes pour l'exécution.</summary>
public sealed record VolumeComputationState(VolumeMode Mode, bool VolumesComputed);
