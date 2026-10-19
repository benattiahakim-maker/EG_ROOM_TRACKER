using System.Globalization;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using EgRoomTracker.Application.Revit;
using EgRoomTracker.Application.Units;
using EgRoomTracker.Contracts.Configuration;
using EgRoomTracker.Contracts.Results;

namespace EgRoomTracker.Revit;

/// <summary>
/// Collecte d'une famille d'éléments (instances de famille d'une ou plusieurs catégories) et rattachement aux pièces du
/// modèle principal (schéma 1.4). Les sous-classes fixent les catégories, la prise en charge des liens et la règle
/// d'association ; une instance illisible est comptée, sans interrompre la collecte. Utilisé uniquement sur le thread
/// Design Automation.
/// </summary>
public abstract class RoomElementServiceBase(RevitExecutionContext context, IUnitConversionService units) : IRoomElementService
{
    /// <summary>Profondeur maximale des liens imbriqués parcourus.</summary>
    private const int MaxLinkDepth = 3;

    /// <summary>Modèle parcouru : le modèle principal (transformation identité) ou un lien avec sa transformation vers le modèle principal.</summary>
    public sealed record ElementSource(Document Document, Transform Transform, string? LinkName);

    /// <summary>Instance à rattacher, avec son modèle d'origine et le localisateur de pièces du modèle principal.</summary>
    public sealed record ElementContext(Document Host, ElementSource Source, FamilyInstance Instance, RoomLocator Rooms);

    protected IUnitConversionService Units => units;

    public abstract ElementKind Kind { get; }

    /// <summary>Catégories parcourues par défaut ; remplaçables par <c>elementCategories</c> dans <c>settings.json</c>.</summary>
    protected abstract IReadOnlyList<BuiltInCategory> Categories { get; }

    /// <summary>Vrai si les modèles liés sont parcourus (équipements, terminaux) ; faux pour les portes, rattachées par leur propre modèle.</summary>
    protected virtual bool SupportsLinks => false;

    /// <summary>Pièces de l'instance et relation de chacune ; vide si aucune pièce n'est trouvée.</summary>
    protected abstract IReadOnlyList<RoomLink> Associate(ElementContext element);

    /// <summary>Champs propres à la famille (ex. passage libre d'une porte).</summary>
    protected virtual RoomElementDto Enrich(RoomElementDto element, FamilyInstance instance) => element;

    public ElementCollection Collect(bool includeAllParameters, IReadOnlyList<string>? categories = null, bool includeLinks = true)
    {
        var host = context.RequireDocument();
        var (resolved, unknown) = ResolveCategories(categories);
        var sources = new List<ElementSource> { new(host, Transform.Identity, null) };
        if (includeLinks && SupportsLinks)
        {
            AddLinks(host, Transform.Identity, sources, depth: 0);
        }

        var rooms = new RoomLocator(host);
        var placements = new List<(int Source, ElementPlacement Placement)>();
        var unreadable = 0;
        for (var index = 0; index < sources.Count; index++)
        {
            var source = sources[index];
            foreach (var category in resolved)
            {
                using var collector = new FilteredElementCollector(source.Document).OfCategory(category).WhereElementIsNotElementType();
                foreach (var element in collector)
                {
                    if (element is not FamilyInstance instance)
                    {
                        continue;
                    }

                    try
                    {
                        placements.Add((index, Build(host, source, rooms, instance, category, includeAllParameters)));
                    }
                    catch (Exception)
                    {
                        unreadable++;
                    }
                }
            }
        }

        var ordered = placements.OrderBy(p => p.Source).ThenBy(p => p.Placement.ElementId).Select(p => p.Placement).ToList();
        return new ElementCollection(
            ordered,
            unreadable,
            resolved.Select(c => c.ToString()).ToList(),
            unknown,
            sources.Select(s => s.Document.Title).ToList());
    }

    private ElementPlacement Build(Document host, ElementSource source, RoomLocator rooms, FamilyInstance instance, BuiltInCategory category, bool includeAllParameters)
    {
        var symbol = instance.Symbol;
        var level = source.Document.GetElement(instance.LevelId) as Level;
        var element = new RoomElementDto
        {
            ElementId = instance.Id.Value.ToString(CultureInfo.InvariantCulture),
            UniqueId = instance.UniqueId,
            Category = instance.Category?.Name ?? string.Empty,
            Family = symbol?.FamilyName,
            Type = symbol?.Name,
            Mark = instance.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString(),
            Level = level?.Name,
            Discipline = SupportsLinks ? ElementDisciplines.Of(category.ToString()) : null,
            LinkName = source.LinkName,
            Parameters = includeAllParameters ? ParameterDumper.Dump(instance, units) : null,
        };
        var links = Associate(new ElementContext(host, source, instance, rooms));
        return new ElementPlacement(instance.Id.Value, Enrich(element, instance), links);
    }

    /// <summary>Modèles liés chargés, avec la transformation totale vers le modèle principal ; les liens non chargés sont ignorés.</summary>
    private static void AddLinks(Document document, Transform parent, List<ElementSource> sources, int depth)
    {
        if (depth >= MaxLinkDepth)
        {
            return;
        }

        foreach (var link in new FilteredElementCollector(document).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>())
        {
            if (link.GetLinkDocument() is not { } linked)
            {
                continue;
            }

            var total = parent.Multiply(link.GetTotalTransform());
            sources.Add(new ElementSource(linked, total, linked.Title));
            AddLinks(linked, total, sources, depth + 1);
        }
    }

    /// <summary>Catégories demandées (noms de <c>BuiltInCategory</c>) ou, à défaut, celles du service ; noms inconnus mis à part.</summary>
    private (IReadOnlyList<BuiltInCategory> Resolved, IReadOnlyList<string> Unknown) ResolveCategories(IReadOnlyList<string>? requested)
    {
        if (requested is null)
        {
            return (Categories, []);
        }

        var resolved = new List<BuiltInCategory>();
        var unknown = new List<string>();
        foreach (var name in requested)
        {
            if (Enum.TryParse<BuiltInCategory>(name, ignoreCase: false, out var category)
                && Enum.IsDefined(category)
                && name.StartsWith("OST_", StringComparison.Ordinal))
            {
                if (!resolved.Contains(category))
                {
                    resolved.Add(category);
                }
            }
            else
            {
                unknown.Add(name);
            }
        }

        return (resolved, unknown);
    }

    /// <summary>Phase de création de l'instance ; null si absente (Revit utilise alors la dernière phase).</summary>
    protected static Phase? CreatedPhase(Document document, FamilyInstance instance) =>
        document.GetElement(instance.CreatedPhaseId) as Phase;

    /// <summary>Lecture d'une pièce liée à une instance ; null si Revit ne la fournit pas (hors phase, non placée).</summary>
    protected static Room? ReadRoom(Func<Room?> read)
    {
        try
        {
            return read();
        }
        catch (Exception)
        {
            return null;
        }
    }
}

/// <summary>
/// Éléments situés <em>dans</em> une pièce (équipements, terminaux), du modèle principal <b>et des liens</b> : une seule pièce
/// par élément (<see cref="RoomLocator"/> : point d'insertion, sinon plus grand volume commun). Pour le modèle principal,
/// <c>FamilyInstance.Room</c> (phase de création) passe d'abord.
/// </summary>
public abstract class InRoomElementServiceBase(RevitExecutionContext context, IUnitConversionService units)
    : RoomElementServiceBase(context, units)
{
    protected override bool SupportsLinks => true;

    protected override IReadOnlyList<RoomLink> Associate(ElementContext element)
    {
        Room? room = null;
        if (element.Source.LinkName is null)
        {
            var phase = CreatedPhase(element.Host, element.Instance);
            room = ReadRoom(() => phase is null ? element.Instance.Room : element.Instance.get_Room(phase));
        }

        room ??= ReadRoom(() => element.Rooms.Locate(element.Instance, element.Source.Transform));
        return room is null ? [] : [new RoomLink(room.Id.Value, ElementRoomRelation.In)];
    }
}
