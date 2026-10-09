using EgRoomTracker.Application.Revit;
using EgRoomTracker.Contracts.Configuration;

namespace EgRoomTracker.Revit;

/// <summary>État du calcul des volumes ; mode compute en EGR-16 (#16). Squelette.</summary>
public sealed class VolumeCalculationService(RevitExecutionContext context) : IVolumeCalculationService
{
    public VolumeComputationState Prepare(VolumeMode mode)
    {
        _ = context;
        throw new NotImplementedException("EGR-09/EGR-16 : état et calcul des volumes.");
    }
}
