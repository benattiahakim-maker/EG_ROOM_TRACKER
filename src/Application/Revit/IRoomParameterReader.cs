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

    /// <summary>
    /// Tous les paramètres d'une pièce (natifs, partagés, de projet) à plat <c>{ nom : valeur }</c>, pour <c>roomParameters: all</c> (schéma 1.4) ;
    /// null si la pièce est introuvable ou si la lecture complète n'est pas prise en charge.
    /// </summary>
    IReadOnlyDictionary<string, object?>? ReadAllParameters(long roomElementId) => null;

    /// <summary>
    /// Valeur texte de paramètres de la pièce lus par leur nom (identification TRC : <c>SGP-DI-Ouvrage</c>…) ; null si la pièce
    /// est introuvable ou si la lecture n'est pas prise en charge ; une entrée à null pour un paramètre absent ou sans valeur.
    /// </summary>
    IReadOnlyDictionary<string, string?>? ReadNamedParameters(long roomElementId, IReadOnlyList<string> names) => null;
}

public sealed record RoomReadResult(RoomDto Room, IReadOnlyList<AnomalyDto> Anomalies);
