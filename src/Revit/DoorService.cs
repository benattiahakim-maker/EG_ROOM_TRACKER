using Autodesk.Revit.DB;
using EgRoomTracker.Application.Revit;
using EgRoomTracker.Application.Units;
using EgRoomTracker.Contracts.Configuration;
using EgRoomTracker.Contracts.Results;

namespace EgRoomTracker.Revit;

/// <summary>
/// Portes du document principal (<c>OST_Doors</c>) rattachées à leurs pièces : <c>FromRoom</c> (relation <c>from</c>) et
/// <c>ToRoom</c> (relation <c>to</c>) pour la phase de création de la porte. Une porte entre deux pièces figure dans
/// chacune des deux ; une porte extérieure n'a qu'un côté.
/// <para>
/// Largeur et hauteur sont le <b>passage libre</b>, mesuré sur la géométrie (<see cref="DoorGeometry"/>) sans dépendre des
/// paramètres de la famille. Les paramètres <c>DOOR_WIDTH</c> et <c>DOOR_HEIGHT</c> ne servent qu'en repli quand la
/// géométrie est inexploitable, et <c>dimensionSource</c> le signale. Numéro de porte : <c>DOOR_NUMBER</c>.
/// </para>
/// </summary>
public sealed class DoorService(RevitExecutionContext context, IUnitConversionService units)
    : RoomElementServiceBase(context, units)
{
    public override ElementKind Kind => ElementKind.Doors;

    protected override IReadOnlyList<BuiltInCategory> Categories { get; } = [BuiltInCategory.OST_Doors];

    protected override IReadOnlyList<RoomLink> Associate(ElementContext element)
    {
        var document = element.Host;
        var instance = element.Instance;
        var phase = CreatedPhase(document, instance);
        var from = ReadRoom(() => phase is null ? instance.FromRoom : instance.get_FromRoom(phase));
        var to = ReadRoom(() => phase is null ? instance.ToRoom : instance.get_ToRoom(phase));

        var links = new List<RoomLink>(2);
        if (from is not null)
        {
            links.Add(new RoomLink(from.Id.Value, ElementRoomRelation.From));
        }

        if (to is not null)
        {
            links.Add(new RoomLink(to.Id.Value, ElementRoomRelation.To));
        }

        return links;
    }

    protected override RoomElementDto Enrich(RoomElementDto element, FamilyInstance instance)
    {
        var opening = DoorGeometry.Measure(instance, Units);
        var width = opening?.WidthM;
        var height = opening?.HeightM;
        var fromGeometry = (width is not null ? 1 : 0) + (height is not null ? 1 : 0);

        // Repli sur les paramètres, seulement pour une dimension que la géométrie n'a pas donnée.
        var fallback = 0;
        if (width is null && ReadLength(instance, BuiltInParameter.DOOR_WIDTH) is { } parameterWidth)
        {
            width = parameterWidth;
            fallback++;
        }

        if (height is null && ReadLength(instance, BuiltInParameter.DOOR_HEIGHT) is { } parameterHeight)
        {
            height = parameterHeight;
            fallback++;
        }

        return element with
        {
            Number = instance.get_Parameter(BuiltInParameter.DOOR_NUMBER)?.AsString(),
            WidthM = width,
            HeightM = height,
            DimensionSource = (fromGeometry, fallback) switch
            {
                (0, 0) => null,
                (_, 0) => "geometry",
                (0, _) => "parameter",
                _ => "mixed",
            },
        };
    }

    /// <summary>Longueur en mètres d'un paramètre de l'instance ou de son type ; null si absent, non finie ou nulle (jamais 0).</summary>
    private double? ReadLength(FamilyInstance instance, BuiltInParameter parameter)
    {
        var value = instance.get_Parameter(parameter) ?? instance.Symbol?.get_Parameter(parameter);
        if (value is not { HasValue: true, StorageType: StorageType.Double })
        {
            return null;
        }

        var raw = value.AsDouble();
        return double.IsFinite(raw) && raw > 0 ? Units.ToMeters(raw) : null;
    }
}
