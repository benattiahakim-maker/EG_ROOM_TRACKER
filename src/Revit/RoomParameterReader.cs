using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using EgRoomTracker.Application.Revit;
using EgRoomTracker.Application.Units;
using EgRoomTracker.Contracts.Configuration;
using EgRoomTracker.Contracts.Results;

namespace EgRoomTracker.Revit;

/// <summary>
/// Lecture des champs d'une pièce et production du DTO (section 3).
/// EGR-08 : identité (<c>ElementId.Value</c>, <c>UniqueId</c>), nom (<c>ROOM_NAME</c>, sans concaténation du numéro),
/// numéro (<c>ROOM_NUMBER</c>, chaîne avec zéros initiaux), niveau, occupation textuelle (<c>ROOM_OCCUPANCY</c>),
/// phase, variante, placement. Paramètres natifs par <see cref="BuiltInParameter"/>, jamais par libellé.
/// Quantités (EGR-09), finitions et paramètres partagés (EGR-10) : à compléter ; null en attendant.
/// </summary>
public sealed class RoomParameterReader(RevitExecutionContext context, IUnitConversionService units) : IRoomParameterReader
{
    public RoomReadResult Read(CollectedRoom room, IReadOnlyList<SharedParameterMapping> sharedParameters, VolumeComputationState volumeState)
    {
        _ = units;
        var document = context.RequireDocument();
        if (document.GetElement(new ElementId(room.ElementId)) is not Room element)
        {
            throw new InvalidOperationException($"Pièce {room.ElementId} introuvable dans le document.");
        }

        // Volume : statut provisoire en attendant EGR-09 (jamais 0 présenté comme calculé).
        var volumeStatus = volumeState.VolumesComputed ? VolumeStatus.NotAvailable : VolumeStatus.NotComputed;
        var builder = new RoomReadBuilder(room.ElementId, element.UniqueId, volumeStatus);

        builder.Identity(
            name: builder.Text(ReadText(element, BuiltInParameter.ROOM_NAME), nameof(BuiltInParameter.ROOM_NAME)),
            number: builder.Text(ReadText(element, BuiltInParameter.ROOM_NUMBER), nameof(BuiltInParameter.ROOM_NUMBER)),
            occupancy: builder.Text(ReadText(element, BuiltInParameter.ROOM_OCCUPANCY), nameof(BuiltInParameter.ROOM_OCCUPANCY)));

        var levelId = element.LevelId;
        var level = levelId is not null && levelId != ElementId.InvalidElementId ? document.GetElement(levelId) as Level : null;
        builder.Level(
            level is null ? null : new LevelRef(Id(level.Id), level.UniqueId, level.Name),
            levelId is null || levelId == ElementId.InvalidElementId ? null : levelId.Value);

        builder.Placement(element.Location is not null);
        builder.Phase(ReadPhase(document, element));
        builder.DesignOption(element.DesignOption is { } option ? new ElementRef(Id(option.Id), option.Name) : null);

        return builder.Build();
    }

    /// <summary>Paramètre texte natif : absent, sans valeur, ou valeur (chaîne vide conservée).</summary>
    internal static ParameterReading ReadText(Element element, BuiltInParameter parameter)
    {
        var value = element.get_Parameter(parameter);
        if (value is null)
        {
            return ParameterReading.Absent;
        }

        if (!value.HasValue || value.StorageType != StorageType.String)
        {
            return ParameterReading.NoValue;
        }

        var text = value.AsString();
        return text is null ? ParameterReading.NoValue : ParameterReading.Of(text);
    }

    private static ElementRef? ReadPhase(Document document, Room room)
    {
        var phaseId = room.get_Parameter(BuiltInParameter.ROOM_PHASE_ID)?.AsElementId();
        if (phaseId is null || phaseId == ElementId.InvalidElementId)
        {
            return null;
        }

        return document.GetElement(phaseId) is Phase phase ? new ElementRef(Id(phase.Id), phase.Name) : null;
    }

    private static string Id(ElementId id) => id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
