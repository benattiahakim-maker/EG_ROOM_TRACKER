using EgRoomTracker.Contracts.Configuration;
using EgRoomTracker.Contracts.Results;

namespace EgRoomTracker.Application.Revit;

/// <summary>
/// Lecture des champs natifs et des mappings partagés d'une pièce ; production du DTO.
/// Une erreur limitée à la pièce est capturée ici : identité exportée, champs illisibles à null,
/// anomalie EXTRACTION_ERROR.
/// </summary>
public interface IRoomParameterReader
{
    RoomReadResult Read(long roomElementId, IReadOnlyList<SharedParameterMapping> sharedParameters, VolumeComputationState volumeState);
}

public sealed record RoomReadResult(RoomDto Room, IReadOnlyList<AnomalyDto> Anomalies);
