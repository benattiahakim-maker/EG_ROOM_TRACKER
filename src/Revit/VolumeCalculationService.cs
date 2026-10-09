using Autodesk.Revit.DB;
using EgRoomTracker.Application;
using EgRoomTracker.Application.Revit;
using EgRoomTracker.Contracts.Codes;
using EgRoomTracker.Contracts.Configuration;

namespace EgRoomTracker.Revit;

/// <summary>
/// État du calcul des volumes (EGR-09) : mode <c>preserve</c>, lecture du réglage du document
/// <c>AreaVolumeSettings.GetAreaVolumeSettings(document).ComputeVolumes</c>, sans transaction ni modification.
/// Mode <c>compute</c> (P2, EGR-16) non disponible : échec explicite plutôt qu'un repli silencieux.
/// </summary>
public sealed class VolumeCalculationService(RevitExecutionContext context) : IVolumeCalculationService
{
    public VolumeComputationState Prepare(VolumeMode mode)
    {
        if (mode == VolumeMode.Compute)
        {
            throw new ExtractionFailedException(ErrorCodes.ConfigInvalid, Stages.Configuration,
                "volumeMode « compute » non disponible dans cette version (EGR-16).");
        }

        var settings = AreaVolumeSettings.GetAreaVolumeSettings(context.RequireDocument());
        return new VolumeComputationState(mode, settings.ComputeVolumes);
    }
}
