using EgRoomTracker.Application.Revit;
using EgRoomTracker.Application.Units;
using EgRoomTracker.Contracts.Configuration;

namespace EgRoomTracker.Revit;

/// <summary>Lecture des champs natifs et des paramètres partagés ; production des DTO. Squelette ; EGR-08 à EGR-10.</summary>
public sealed class RoomParameterReader(RevitExecutionContext context, IUnitConversionService units) : IRoomParameterReader
{
    public RoomReadResult Read(CollectedRoom room, IReadOnlyList<SharedParameterMapping> sharedParameters, VolumeComputationState volumeState)
    {
        _ = (context, units);
        throw new NotImplementedException("EGR-08/09/10 : lecture des paramètres de pièce.");
    }
}
