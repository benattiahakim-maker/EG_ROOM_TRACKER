using System.Globalization;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using EgRoomTracker.Application.Revit;
using EgRoomTracker.Application.Units;
using EgRoomTracker.Contracts.Codes;
using EgRoomTracker.Contracts.Configuration;
using EgRoomTracker.Contracts.Results;

namespace EgRoomTracker.Revit;

/// <summary>
/// Lecture des champs d'une pièce et production du DTO (section 3).
/// EGR-08 : identité (<c>ElementId.Value</c>, <c>UniqueId</c>), nom (<c>ROOM_NAME</c>, sans concaténation du numéro),
/// numéro (<c>ROOM_NUMBER</c>, chaîne avec zéros initiaux), niveau, occupation textuelle (<c>ROOM_OCCUPANCY</c>),
/// phase, variante, placement. Paramètres natifs par <see cref="BuiltInParameter"/>, jamais par libellé.
/// EGR-09 : <c>SpatialElement.Area</c>, <c>Room.UnboundedHeight</c>, <c>Room.Volume</c> convertis par UnitUtils
/// (<see cref="RoomQuantities"/>).
/// EGR-10 : finitions textuelles (<c>ROOM_FINISH_FLOOR/WALL/CEILING/BASE</c>, aucune déduction géométrique) et
/// paramètres partagés par GUID (<c>Element.get_Parameter(Guid)</c>, <c>SharedParameterElement.Lookup</c>),
/// sans libellé localisé. Une exception de lecture non capturée donne EXTRACTION_ERROR (orchestration).
/// </summary>
public sealed class RoomParameterReader(RevitExecutionContext context, IUnitConversionService units) : IRoomParameterReader
{
    /// <summary>Présence des GUID dans le document (<c>SharedParameterElement.Lookup</c>), mémorisée pour le document courant.</summary>
    private readonly Dictionary<Guid, bool> _knownGuids = [];
    private Document? _knownGuidsDocument;

    public IReadOnlyList<AnomalyDto> CheckSharedParameters(IReadOnlyList<SharedParameterMapping> sharedParameters)
    {
        var document = context.RequireDocument();
        var anomalies = new List<AnomalyDto>();
        foreach (var mapping in sharedParameters)
        {
            if (!IsKnown(document, mapping.Guid))
            {
                anomalies.Add(SharedParameterAnomalies.NotFound(mapping));
                continue;
            }

            // Type numérique non exportable : connu dès la définition du paramètre dans le document.
            var dataType = SharedParameterElement.Lookup(document, mapping.Guid)?.GetDefinition()?.GetDataType();
            if (dataType is not null && IsUnsupportedNumericSpec(dataType))
            {
                anomalies.Add(SharedParameterAnomalies.UnsupportedType(mapping, dataType.TypeId ?? string.Empty));
            }
        }

        return anomalies;
    }

    public IReadOnlyDictionary<string, object?>? ReadAllParameters(long roomElementId)
    {
        var document = context.RequireDocument();
        return document.GetElement(new ElementId(roomElementId)) is Room element ? ParameterDumper.Dump(element, units) : null;
    }

    public IReadOnlyDictionary<string, string?>? ReadNamedParameters(long roomElementId, IReadOnlyList<string> names)
    {
        var document = context.RequireDocument();
        if (document.GetElement(new ElementId(roomElementId)) is not Room element)
        {
            return null;
        }

        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            var parameter = element.LookupParameter(name);
            values[name] = parameter is null || !parameter.HasValue
                ? null
                : parameter.StorageType == StorageType.String ? parameter.AsString() : parameter.AsValueString();
        }

        return values;
    }

    public RoomReadResult Read(CollectedRoom room, IReadOnlyList<SharedParameterMapping> sharedParameters, VolumeComputationState volumeState)
    {
        var document = context.RequireDocument();
        if (document.GetElement(new ElementId(room.ElementId)) is not Room element)
        {
            throw new InvalidOperationException($"Pièce {room.ElementId} introuvable dans le document.");
        }

        // volumeStatus définitif fixé par Quantities (EGR-09).
        var builder = new RoomReadBuilder(room.ElementId, element.UniqueId, VolumeStatus.NotAvailable);

        builder.Identity(
            name: builder.Text(ReadText(element, BuiltInParameter.ROOM_NAME), nameof(BuiltInParameter.ROOM_NAME)),
            number: builder.Text(ReadText(element, BuiltInParameter.ROOM_NUMBER), nameof(BuiltInParameter.ROOM_NUMBER)),
            occupancy: builder.Text(ReadText(element, BuiltInParameter.ROOM_OCCUPANCY), nameof(BuiltInParameter.ROOM_OCCUPANCY)));

        var levelId = element.LevelId;
        var level = levelId is not null && levelId != ElementId.InvalidElementId ? document.GetElement(levelId) as Level : null;
        builder.Level(
            level is null ? null : new LevelRef(Id(level.Id), level.UniqueId, level.Name),
            levelId is null || levelId == ElementId.InvalidElementId ? null : levelId.Value);

        var isPlaced = element.Location is not null;
        builder.Placement(isPlaced);
        // Mode compute (EGR-16) : volume lu pendant la transaction temporaire, avant son annulation.
        var volume = volumeState.VolumeSource(room.ElementId, () => element.Volume);
        builder.Quantities(RoomQuantities.Convert(
            rawArea: element.Area,
            rawUnboundedHeight: element.UnboundedHeight,
            readVolume: volume.ReadVolume,
            volumesComputed: volume.VolumesComputed,
            isPlaced: isPlaced,
            units: units));
        builder.Phase(ReadPhase(document, element));
        var option = element.DesignOption;
        builder.DesignOption(option is null ? null : new ElementRef(Id(option.Id), option.Name), ReadOptionSetId(option));

        builder.Finishes(
            floor: builder.Text(ReadText(element, BuiltInParameter.ROOM_FINISH_FLOOR), nameof(BuiltInParameter.ROOM_FINISH_FLOOR)),
            wall: builder.Text(ReadText(element, BuiltInParameter.ROOM_FINISH_WALL), nameof(BuiltInParameter.ROOM_FINISH_WALL)),
            ceiling: builder.Text(ReadText(element, BuiltInParameter.ROOM_FINISH_CEILING), nameof(BuiltInParameter.ROOM_FINISH_CEILING)),
            @base: builder.Text(ReadText(element, BuiltInParameter.ROOM_FINISH_BASE), nameof(BuiltInParameter.ROOM_FINISH_BASE)));
        builder.SharedParameters(sharedParameters.Select(m => (m, ReadShared(document, element, m.Guid))));

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

    /// <summary>Paramètre partagé d'une pièce, selon son <see cref="StorageType"/> (jamais <c>AsValueString</c>).</summary>
    private SharedParameterRead ReadShared(Document document, Element element, Guid guid)
    {
        if (!IsKnown(document, guid))
        {
            return SharedParameterRead.NotInDocument;
        }

        var parameter = element.get_Parameter(guid);
        if (parameter is null)
        {
            return SharedParameterRead.Absent;
        }

        if (!parameter.HasValue)
        {
            return SharedParameterRead.NoValue;
        }

        return parameter.StorageType switch
        {
            StorageType.String => SharedParameterValues.FromText(parameter.AsString()),
            StorageType.Integer => SharedParameterValues.FromInteger(parameter.AsInteger()),
            StorageType.Double => SharedParameterValues.FromDouble(parameter.AsDouble(), MeasureOf(parameter.Definition), units),
            StorageType.ElementId => SharedParameterValues.FromElementId(parameter.AsElementId()?.Value),
            _ => SharedParameterRead.NoValue,
        };
    }

    /// <summary>Grandeur d'un double selon sa spec ; spec absente ou autre que longueur/surface/volume/nombre : non supportée.</summary>
    internal static SharedParameterMeasure MeasureOf(Definition? definition) => MeasureOf(definition?.GetDataType());

    private static SharedParameterMeasure MeasureOf(ForgeTypeId? dataType)
    {
        if (dataType is null || dataType.Empty())
        {
            return SharedParameterMeasure.Unsupported;
        }

        if (dataType == SpecTypeId.Length)
        {
            return SharedParameterMeasure.Length;
        }

        if (dataType == SpecTypeId.Area)
        {
            return SharedParameterMeasure.Area;
        }

        if (dataType == SpecTypeId.Volume)
        {
            return SharedParameterMeasure.Volume;
        }

        return dataType == SpecTypeId.Number ? SharedParameterMeasure.Number : SharedParameterMeasure.Unsupported;
    }

    /// <summary>
    /// Spec mesurable (valeur double avec unité, <c>UnitUtils.IsMeasurableSpec</c>) autre que longueur, surface, volume
    /// ou nombre sans unité. Les specs non mesurables (texte, entier, Oui/Non, références) ne sont pas concernées.
    /// </summary>
    private static bool IsUnsupportedNumericSpec(ForgeTypeId dataType) =>
        !dataType.Empty()
        && dataType != SpecTypeId.Int.Integer
        && UnitUtils.IsMeasurableSpec(dataType)
        && MeasureOf(dataType) == SharedParameterMeasure.Unsupported;

    private bool IsKnown(Document document, Guid guid)
    {
        if (!ReferenceEquals(_knownGuidsDocument, document))
        {
            _knownGuids.Clear();
            _knownGuidsDocument = document;
        }

        if (!_knownGuids.TryGetValue(guid, out var known))
        {
            known = SharedParameterElement.Lookup(document, guid) is not null;
            _knownGuids[guid] = known;
        }

        return known;
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

    /// <summary>
    /// Jeu de variantes d'une variante : <c>BuiltInParameter.OPTION_SET_ID</c> (« Design Option Set Id ») lu sur la
    /// <c>DesignOption</c>. Type de stockage non documenté : ElementId ou entier acceptés ; sinon null (inconnu).
    /// </summary>
    private static string? ReadOptionSetId(DesignOption? option)
    {
        if (option is null)
        {
            return null;
        }

        try
        {
            var parameter = option.get_Parameter(BuiltInParameter.OPTION_SET_ID);
            if (parameter is null || !parameter.HasValue)
            {
                return null;
            }

            return parameter.StorageType switch
            {
                StorageType.ElementId when parameter.AsElementId() is { } id && id != ElementId.InvalidElementId => Id(id),
                StorageType.Integer => parameter.AsInteger().ToString(CultureInfo.InvariantCulture),
                _ => null,
            };
        }
        catch (Exception)
        {
            return null;
        }
    }
}
