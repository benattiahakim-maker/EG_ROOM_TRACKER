using EgRoomTracker.Contracts.Configuration;
using EgRoomTracker.Contracts.Results;

namespace EgRoomTracker.Application.Revit;

/// <summary>
/// Lecture des champs natifs et des mappings partagés d'une pièce ; production du DTO.
/// Une erreur limitée à un champ est capturée ici (champ à null, anomalie). Toute exception non capturée
/// est convertie par <c>RoomExtractionService</c> en pièce EXTRACTION_ERROR (identité seule).
/// </summary>
public interface IRoomParameterReader
{
    RoomReadResult Read(CollectedRoom room, IReadOnlyList<SharedParameterMapping> sharedParameters, VolumeComputationState volumeState);

    /// <summary>
    /// Contrôle des mappings partagés au niveau document, appelé une fois avant la lecture des pièces :
    /// une anomalie document SHARED_PARAMETER_NOT_FOUND par GUID inconnu du document (EGR-10).
    /// </summary>
    IReadOnlyList<AnomalyDto> CheckSharedParameters(IReadOnlyList<SharedParameterMapping> sharedParameters) => [];
}

public sealed record RoomReadResult(RoomDto Room, IReadOnlyList<AnomalyDto> Anomalies);
