using System.Globalization;
using EgRoomTracker.Application.Trc;
using EgRoomTracker.WorkItemRunner.Mvvm;

namespace EgRoomTracker.WorkItemRunner.Services;

/// <summary>Groupe de colonnes d'un tableau de l'arbre, affiché en bande au-dessus des en-têtes.</summary>
public enum TrcSection
{
    Identification,
    Occurrence,
    Type,
}

/// <summary>Élément masquable par les filtres « Occurrence » / « Type » (cellule, en-tête, bande).</summary>
public abstract class TrcSectionItem(TrcSection section) : ObservableObject
{
    private bool _isVisible = true;

    public TrcSection Section { get; } = section;

    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }

    /// <summary>Fond de la cellule selon la section (bleuté pour l'occurrence, ambré pour le type).</summary>
    public string Background => Section switch
    {
        TrcSection.Occurrence => "#FBFDFF",
        TrcSection.Type => "#FFFDF7",
        _ => "#FFFFFF",
    };
}

/// <summary>Cellule (ou en-tête) d'un tableau : texte, largeur de sa colonne (pixels) et section.</summary>
public sealed class TrcCell(string? text, double width, TrcSection section) : TrcSectionItem(section)
{
    public string? Text { get; } = text;

    public double Width { get; } = width;
}

/// <summary>Bande d'en-tête couvrant les colonnes d'une section : « Identification », « Paramètres d'occurrence (n) »…</summary>
public sealed class TrcBand(string title, double width, TrcSection section) : TrcSectionItem(section)
{
    public string Title { get; } = title;

    public double Width { get; } = width;

    public string BandBackground => Section switch
    {
        TrcSection.Occurrence => "#E0F2FE",
        TrcSection.Type => "#FEF3C7",
        _ => "#F1F5F9",
    };

    public string Foreground => Section switch
    {
        TrcSection.Occurrence => "#0369A1",
        TrcSection.Type => "#92400E",
        _ => "#64748B",
    };
}

/// <summary>En-tête d'un tableau : bandes de sections et en-têtes de colonnes.</summary>
public sealed record TrcHeader(IReadOnlyList<TrcBand> Bands, IReadOnlyList<TrcCell> Columns)
{
    public IEnumerable<TrcSectionItem> Items => Bands.Cast<TrcSectionItem>().Concat(Columns);
}

/// <summary>Objet dépliable (pièce, groupe) avec une flèche ▶ / ▼.</summary>
public abstract class TrcExpandable : ObservableObject
{
    private bool _isExpanded;

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetProperty(ref _isExpanded, value))
            {
                OnPropertyChanged(nameof(Glyph));
            }
        }
    }

    public string Glyph => IsExpanded ? "▼" : "▶";
}

/// <summary>Ligne d'un sous-tableau : une porte, un équipement ou un terminal ; ses paramètres sont des colonnes.</summary>
public sealed class TrcElementNode(string bimId, IReadOnlyList<TrcCell> cells)
{
    public string BimId { get; } = bimId;

    public IReadOnlyList<TrcCell> Cells { get; } = cells;
}

/// <summary>Sous-tableau d'une catégorie dans une pièce (Portes, Équipements CVC…), avec ses propres colonnes.</summary>
public sealed class TrcGroup : TrcExpandable
{
    private bool _isVisible = true;

    public TrcGroup(string title, string filterKey, string color, TrcHeader header, IReadOnlyList<TrcElementNode> rows)
    {
        Title = title;
        FilterKey = filterKey;
        Color = color;
        Header = header;
        Rows = rows;
        IsExpanded = true;
    }

    public string Title { get; }

    /// <summary>Clé du filtre qui affiche ce groupe : <c>door</c> ou la discipline.</summary>
    public string FilterKey { get; }

    /// <summary>Couleur de la catégorie (<c>#RRGGBB</c>) ; <see cref="ChipBackground"/> en est la version transparente.</summary>
    public string Color { get; }

    public string ChipBackground => "#1F" + Color[1..];

    public TrcHeader Header { get; }

    public IReadOnlyList<TrcElementNode> Rows { get; }

    public string CountText => Rows.Count == 1 ? "1 élément" : $"{Rows.Count.ToString(CultureInfo.InvariantCulture)} éléments";

    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }
}

/// <summary>Pièce : ligne du tableau des pièces (colonnes de <see cref="TrcTree.RoomHeader"/>), dépliable sur un sous-tableau par catégorie.</summary>
public sealed class TrcRoomNode(string bimId, string name, IReadOnlyList<TrcCell> cells, IReadOnlyList<TrcGroup> groups) : TrcExpandable
{
    public string BimId { get; } = bimId;

    public string Name { get; } = name;

    public IReadOnlyList<TrcCell> Cells { get; } = cells;

    public IReadOnlyList<TrcGroup> Groups { get; } = groups;

    /// <summary>Résumé du contenu : « 1 porte · 2 équipements · 1 terminal ».</summary>
    public string Content => TrcTree.Summarize(Groups);
}

/// <summary>Case du filtre d'affichage : catégories (Portes, CVC…) et sections de colonnes (Occurrence, Type).</summary>
public sealed class TrcFilter : ObservableObject
{
    private bool _isChecked = true;

    public TrcFilter(string key, string label, string color)
    {
        Key = key;
        Label = label;
        Color = color;
    }

    public string Key { get; }

    public string Label { get; }

    public string Color { get; }

    public string ChipBackground => "#1F" + Color[1..];

    public bool IsChecked
    {
        get => _isChecked;
        set => SetProperty(ref _isChecked, value);
    }
}

/// <summary>Objet BIM lu dans <c>TRCValidationDict.json</c> : pk, bim_id, valeurs par code, pk des pièces qui le contiennent.</summary>
public sealed record TrcObject(string Pk, string BimId, IReadOnlyDictionary<string, List<string>> Values, IReadOnlyList<string> ParentPks);

/// <summary>
/// Arbre des résultats TRC (maquette Figma v2 « paramètres en colonnes ») : tableau des pièces ; une pièce dépliée montre un sous-tableau par
/// catégorie (Portes, Équipements CVC, Terminaux électricité…). Chaque tableau a ses propres colonnes, sous trois bandes : Identification,
/// Paramètres d'occurrence, Paramètres de type (une pièce n'a pas de type). Une porte entre deux pièces figure sous chacune.
/// </summary>
public sealed class TrcTree
{
    public const string DoorKey = "door";
    public const string OccurrenceKey = "occurrence";
    public const string TypeKey = "type";
    public const string NoRoom = "(sans pièce)";

    private const string OccurrencePrefix = "c:parametres revit_";
    private const string TypePrefix = "d:parametres type_";

    private static readonly string[] DisciplineOrder = ["cvc", "electricite", "plomberie", "autres"];

    private static readonly Dictionary<string, (string Label, string Color)> Disciplines = new(StringComparer.Ordinal)
    {
        ["cvc"] = ("CVC", "#0E7490"),
        ["electricite"] = ("électricité", "#7C3AED"),
        ["plomberie"] = ("plomberie", "#1D4ED8"),
        ["autres"] = ("autres", "#475569"),
    };

    private const string DoorColor = "#B45309";

    /// <summary>Colonnes d'identification d'un élément, dans l'ordre ; seules celles renseignées dans le groupe apparaissent.</summary>
    private static readonly (string Code, string Header)[] ElementIdentity =
    [
        (TrcCodes.Numero, "Numéro"),
        (TrcCodes.ElementMark, "Repère"),
        (TrcCodes.ElementFamily, "Famille"),
        (TrcCodes.ElementType, "Type"),
        (TrcCodes.DoorWidth, "Largeur"),
        (TrcCodes.DoorHeight, "Hauteur"),
        (TrcCodes.ElementRevitCategory, "Catégorie Revit"),
        (TrcCodes.ElementLink, "Modèle lié"),
        (TrcCodes.Storey, "Niveau"),
        (TrcCodes.ElementRevitId, "Id Revit"),
    ];

    /// <summary>Colonnes d'identification d'une pièce, dans l'ordre (le contenu est calculé).</summary>
    private static readonly (string Code, string Header)[] RoomIdentity =
    [
        (TrcCodes.Username, "Pièce"),
        (TrcCodes.Storey, "Niveau"),
        (TrcCodes.Surface, "Surface (m²)"),
        (TrcCodes.Volume, "Volume (m³)"),
        (TrcCodes.Height, "Hauteur (m)"),
    ];

    /// <summary>Codes jamais affichés en colonne (portés par le groupe, la colonne « Pièces » ou l'info-bulle).</summary>
    private static readonly HashSet<string> Hidden = new(StringComparer.Ordinal)
    {
        TrcCodes.Category, TrcCodes.SpaceList, "Space", TrcCodes.ElementDiscipline,
    };

    private TrcTree(TrcHeader roomHeader, IReadOnlyList<TrcRoomNode> rooms, IReadOnlyList<TrcFilter> filters)
    {
        RoomHeader = roomHeader;
        Rooms = rooms;
        Filters = filters;
        foreach (var filter in filters)
        {
            filter.PropertyChanged += (_, _) => ApplyFilters();
        }
    }

    /// <summary>En-tête du tableau des pièces (bandes Identification et Paramètres d'occurrence).</summary>
    public TrcHeader RoomHeader { get; }

    public IReadOnlyList<TrcRoomNode> Rooms { get; }

    /// <summary>Filtres d'affichage : un par catégorie présente (Portes, CVC…), puis « Occurrence » et « Type ».</summary>
    public IReadOnlyList<TrcFilter> Filters { get; }

    public void ExpandAll()
    {
        foreach (var room in Rooms)
        {
            room.IsExpanded = true;
            foreach (var group in room.Groups)
            {
                group.IsExpanded = true;
            }
        }
    }

    public void CollapseAll()
    {
        foreach (var room in Rooms)
        {
            room.IsExpanded = false;
        }
    }

    private void ApplyFilters()
    {
        var shown = Filters.Where(f => f.IsChecked).Select(f => f.Key).ToHashSet(StringComparer.Ordinal);
        bool Visible(TrcSection section) => section switch
        {
            TrcSection.Occurrence => shown.Contains(OccurrenceKey),
            TrcSection.Type => shown.Contains(TypeKey),
            _ => true,
        };

        void Apply(IEnumerable<TrcSectionItem> items)
        {
            foreach (var item in items)
            {
                item.IsVisible = Visible(item.Section);
            }
        }

        Apply(RoomHeader.Items);
        foreach (var room in Rooms)
        {
            Apply(room.Cells);
            foreach (var group in room.Groups)
            {
                group.IsVisible = shown.Contains(group.FilterKey);
                Apply(group.Header.Items);
                foreach (var row in group.Rows)
                {
                    Apply(row.Cells);
                }
            }
        }
    }

    public static TrcTree Build(IReadOnlyList<TrcObject> objects)
    {
        var byPk = objects.ToDictionary(o => o.Pk, StringComparer.Ordinal);
        var elements = objects.Where(o => o.ParentPks.Count > 0).ToList();
        var roomObjects = objects.Where(o => o.ParentPks.Count == 0 && (First(o, TrcCodes.Category) ?? TrcCategories.Space) == TrcCategories.Space).ToList();
        var orphans = objects.Where(o => o.ParentPks.Count == 0 && !roomObjects.Contains(o)).ToList();
        string RoomName(TrcObject room) => First(room, TrcCodes.Username) ?? room.BimId;

        var usedKeys = new HashSet<string>(StringComparer.Ordinal);
        IReadOnlyList<TrcGroup> Groups(IEnumerable<TrcObject> items) => items
            .GroupBy(item => First(item, TrcCodes.Category) ?? string.Empty, StringComparer.Ordinal)
            .Select(g => (Info: Describe(g.Key), Items: g.ToList()))
            .OrderBy(g => g.Info.Order)
            .Select(g =>
            {
                usedKeys.Add(g.Info.Key);
                var pieces = new Dictionary<TrcObject, string>(g.Items.Select(i => new KeyValuePair<TrcObject, string>(i,
                    string.Join(" · ", i.ParentPks.Where(byPk.ContainsKey).Select(pk => RoomName(byPk[pk]))))));
                var (header, rows) = Table(g.Items, ElementIdentity, extra: ("Pièces", item => pieces[item]));
                return new TrcGroup(g.Info.Title, g.Info.Key, g.Info.Color, header,
                    g.Items.Select((item, index) => new TrcElementNode(item.BimId, rows[index])).ToList());
            })
            .ToList();

        var roomGroups = roomObjects.ToDictionary(room => room, room => Groups(elements.Where(e => e.ParentPks.Contains(room.Pk, StringComparer.Ordinal))));
        var (roomHeader, roomRows) = Table(roomObjects, RoomIdentity, extra: ("Contenu", room => Summarize(roomGroups[room])));
        var rooms = roomObjects.Select((room, index) => new TrcRoomNode(room.BimId, RoomName(room), roomRows[index], roomGroups[room])).ToList();

        var homeless = orphans.Concat(elements.Where(e => !e.ParentPks.Any(byPk.ContainsKey))).ToList();
        if (homeless.Count > 0)
        {
            var cells = roomHeader.Columns.Select((c, i) => new TrcCell(i == 0 ? NoRoom : null, c.Width, c.Section)).ToList();
            rooms.Add(new TrcRoomNode(string.Empty, NoRoom, cells, Groups(homeless)));
        }

        var filters = DisciplineOrder.Prepend(DoorKey)
            .Where(usedKeys.Contains)
            .Select(key => key == DoorKey ? new TrcFilter(key, "Portes", DoorColor) : new TrcFilter(key, Capitalize(Disciplines[key].Label), Disciplines[key].Color))
            .Append(new TrcFilter(OccurrenceKey, "Occurrence", "#0369A1"))
            .Append(new TrcFilter(TypeKey, "Type", "#92400E"))
            .ToList();
        return new TrcTree(roomHeader, rooms, filters);
    }

    /// <summary>
    /// Tableau d'un ensemble d'objets : colonnes d'identification connues et renseignées, autres codes TRC, colonne calculée
    /// (<paramref name="extra"/>), puis tous les paramètres d'occurrence et tous les paramètres de type présents (union, ordre d'apparition).
    /// </summary>
    private static (TrcHeader Header, IReadOnlyList<IReadOnlyList<TrcCell>> Rows) Table(
        IReadOnlyList<TrcObject> items,
        (string Code, string Header)[] identity,
        (string Header, Func<TrcObject, string?> Value) extra)
    {
        var known = identity.Select(c => c.Code).ToHashSet(StringComparer.Ordinal);
        var codes = items.SelectMany(i => i.Values.Keys).Distinct(StringComparer.Ordinal).ToList();
        var columns = new List<(string Header, TrcSection Section, Func<TrcObject, string?> Value)>();
        foreach (var (code, title) in identity.Where(c => items.Any(i => First(i, c.Code) is not null)))
        {
            columns.Add((title, TrcSection.Identification, item => Display(code, First(item, code))));
        }

        foreach (var code in codes.Where(c => !known.Contains(c) && !Hidden.Contains(c) && !c.StartsWith(OccurrencePrefix, StringComparison.Ordinal) && !c.StartsWith(TypePrefix, StringComparison.Ordinal)))
        {
            columns.Add((Label(code), TrcSection.Identification, item => First(item, code)));
        }

        columns.Add((extra.Header, TrcSection.Identification, extra.Value));
        foreach (var code in codes.Where(c => c.StartsWith(OccurrencePrefix, StringComparison.Ordinal)))
        {
            columns.Add((Capitalize(code[OccurrencePrefix.Length..]), TrcSection.Occurrence, item => First(item, code)));
        }

        foreach (var code in codes.Where(c => c.StartsWith(TypePrefix, StringComparison.Ordinal)))
        {
            columns.Add((Capitalize(code[TypePrefix.Length..]), TrcSection.Type, item => First(item, code)));
        }

        var texts = items.Select(item => columns.Select(c => c.Value(item)).ToList()).ToList();
        var widths = columns.Select((c, index) => Width(texts.Select(row => row[index]).Append(c.Header))).ToList();
        var bands = columns
            .Select((c, index) => (c.Section, Width: widths[index]))
            .GroupBy(c => c.Section)
            .Select(g => new TrcBand(BandTitle(g.Key, g.Count()), g.Sum(c => c.Width), g.Key))
            .ToList();
        var header = new TrcHeader(bands, columns.Select((c, index) => new TrcCell(c.Header, widths[index], c.Section)).ToList());
        var rows = texts.Select(row => (IReadOnlyList<TrcCell>)row.Select((text, index) => new TrcCell(text, widths[index], columns[index].Section)).ToList()).ToList();
        return (header, rows);
    }

    private static string BandTitle(TrcSection section, int count) => section switch
    {
        TrcSection.Occurrence => $"Paramètres d'occurrence ({count.ToString(CultureInfo.InvariantCulture)})",
        TrcSection.Type => $"Paramètres de type ({count.ToString(CultureInfo.InvariantCulture)})",
        _ => "Identification",
    };

    /// <summary>Résumé des objets d'une pièce : « 1 porte · 2 équipements · 1 terminal ».</summary>
    public static string Summarize(IReadOnlyList<TrcGroup> groups)
    {
        int Count(Func<TrcGroup, bool> predicate) => groups.Where(predicate).Sum(g => g.Rows.Count);
        var parts = new List<string>();
        void Add(int n, string one, string many)
        {
            if (n > 0)
            {
                parts.Add($"{n.ToString(CultureInfo.InvariantCulture)} {(n == 1 ? one : many)}");
            }
        }

        Add(Count(g => g.FilterKey == DoorKey), "porte", "portes");
        Add(Count(g => g.Title.StartsWith("Équipements", StringComparison.Ordinal)), "équipement", "équipements");
        Add(Count(g => g.Title.StartsWith("Terminaux", StringComparison.Ordinal)), "terminal", "terminaux");
        return parts.Count == 0 ? "aucun objet" : string.Join(" · ", parts);
    }

    /// <summary>Titre, filtre, couleur et rang d'un groupe d'après la Category TRC (<c>Door</c>, <c>cvc equipement</c>, <c>electricite terminal</c>…).</summary>
    private static (string Title, string Key, string Color, int Order) Describe(string category)
    {
        if (category == TrcCategories.Door)
        {
            return ("Portes", DoorKey, DoorColor, 0);
        }

        var parts = category.Split(' ', 2);
        if (parts.Length == 2 && Disciplines.TryGetValue(parts[0], out var discipline))
        {
            var rank = Array.IndexOf(DisciplineOrder, parts[0]);
            return parts[1] switch
            {
                "equipement" => ($"Équipements {discipline.Label}", parts[0], discipline.Color, 10 + rank),
                "terminal" => ($"Terminaux {discipline.Label}", parts[0], discipline.Color, 20 + rank),
                _ => (category, parts[0], discipline.Color, 30),
            };
        }

        return (string.IsNullOrEmpty(category) ? "Autres objets" : category, "autres", Disciplines["autres"].Color, 40);
    }

    private static string? First(TrcObject o, string code) => o.Values.TryGetValue(code, out var list) && list.Count > 0 ? string.Join(" | ", list) : null;

    /// <summary>Libellé court d'un code TRC : <c>a:identification data_building</c> → Building, <c>bim_object_username</c> → Nom.</summary>
    private static string Label(string code)
    {
        if (code == TrcCodes.Username)
        {
            return "Nom";
        }

        var colon = code.IndexOf(':');
        var underscore = code.IndexOf('_');
        return Capitalize(colon >= 0 && underscore > colon ? code[(underscore + 1)..] : code);
    }

    /// <summary>Valeur affichée : unité ajoutée aux dimensions de porte (<c>0.92m</c> → <c>0.92 m</c>, <c>2.13</c> → <c>2.13 m</c>).</summary>
    private static string? Display(string code, string? value) =>
        value is null ? null
        : code is TrcCodes.DoorWidth or TrcCodes.DoorHeight ? value.TrimEnd('m') + " m"
        : value;

    /// <summary>Largeur d'une colonne d'après son texte le plus long (≈ 7,5 px par caractère), entre 60 et 320 px.</summary>
    private static double Width(IEnumerable<string?> texts) =>
        Math.Clamp(texts.Max(t => t?.Length ?? 0) * 7.5 + 24, 60, 320);

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
