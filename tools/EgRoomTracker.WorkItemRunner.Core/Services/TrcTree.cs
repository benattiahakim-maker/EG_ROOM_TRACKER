using System.Globalization;
using EgRoomTracker.Application.Trc;
using EgRoomTracker.WorkItemRunner.Mvvm;

namespace EgRoomTracker.WorkItemRunner.Services;

/// <summary>Cellule d'un tableau de l'arbre : texte et largeur de sa colonne (pixels).</summary>
public sealed record TrcCell(string? Text, double Width);

/// <summary>Paramètre d'un objet (fiche Paramètre / Valeur).</summary>
public sealed record TrcParameter(string Name, string? Value);

/// <summary>Objet dépliable (pièce, groupe, élément) avec une flèche ▶ / ▼.</summary>
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

/// <summary>Fiche des paramètres d'un objet, repliable, masquée par le filtre « Paramètres Revit ».</summary>
public sealed class TrcParameterBlock : TrcExpandable
{
    private bool _isVisible = true;

    public TrcParameterBlock(IReadOnlyList<TrcParameter> parameters) => Parameters = parameters;

    public IReadOnlyList<TrcParameter> Parameters { get; }

    public string Title => $"Paramètres ({Parameters.Count.ToString(CultureInfo.InvariantCulture)})";

    public bool IsVisible
    {
        get => _isVisible && Parameters.Count > 0;
        set => SetProperty(ref _isVisible, value);
    }
}

/// <summary>Ligne d'un sous-tableau : une porte, un équipement ou un terminal, dépliable sur ses paramètres.</summary>
public sealed class TrcElementNode(string bimId, IReadOnlyList<TrcCell> cells, TrcParameterBlock parameters) : TrcExpandable
{
    public string BimId { get; } = bimId;

    public IReadOnlyList<TrcCell> Cells { get; } = cells;

    public TrcParameterBlock Parameters { get; } = parameters;
}

/// <summary>Sous-tableau d'une catégorie dans une pièce (Portes, Équipements CVC…), avec ses propres colonnes.</summary>
public sealed class TrcGroup : TrcExpandable
{
    private bool _isVisible = true;

    public TrcGroup(string title, string filterKey, string color, IReadOnlyList<TrcCell> headers, IReadOnlyList<TrcElementNode> rows)
    {
        Title = title;
        FilterKey = filterKey;
        Color = color;
        Headers = headers;
        Rows = rows;
        IsExpanded = true;
    }

    public string Title { get; }

    /// <summary>Clé du filtre qui affiche ce groupe : <c>door</c> ou la discipline.</summary>
    public string FilterKey { get; }

    /// <summary>Couleur de la catégorie (<c>#RRGGBB</c>) ; <see cref="ChipBackground"/> en est la version transparente.</summary>
    public string Color { get; }

    public string ChipBackground => "#1F" + Color[1..];

    public IReadOnlyList<TrcCell> Headers { get; }

    public IReadOnlyList<TrcElementNode> Rows { get; }

    public string CountText => Rows.Count == 1 ? "1 élément" : $"{Rows.Count.ToString(CultureInfo.InvariantCulture)} éléments";

    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }
}

/// <summary>Pièce : ligne du tableau des pièces, dépliable sur ses paramètres et un sous-tableau par catégorie d'objets.</summary>
public sealed class TrcRoomNode(string bimId, string name, string? level, string? surface, string? volume, string? height, TrcParameterBlock parameters, IReadOnlyList<TrcGroup> groups)
    : TrcExpandable
{
    public string BimId { get; } = bimId;

    public string Name { get; } = name;

    public string? Level { get; } = level;

    public string? Surface { get; } = surface;

    public string? Volume { get; } = volume;

    public string? Height { get; } = height;

    public TrcParameterBlock Parameters { get; } = parameters;

    public IReadOnlyList<TrcGroup> Groups { get; } = groups;

    /// <summary>Résumé du contenu : « 1 porte · 2 équipements · 1 terminal ».</summary>
    public string Content
    {
        get
        {
            int Count(Func<TrcGroup, bool> predicate) => Groups.Where(predicate).Sum(g => g.Rows.Count);
            var parts = new List<string>();
            void Add(int n, string one, string many)
            {
                if (n > 0)
                {
                    parts.Add($"{n.ToString(CultureInfo.InvariantCulture)} {(n == 1 ? one : many)}");
                }
            }

            Add(Count(g => g.FilterKey == TrcTree.DoorKey), "porte", "portes");
            Add(Count(g => g.Title.StartsWith("Équipements", StringComparison.Ordinal)), "équipement", "équipements");
            Add(Count(g => g.Title.StartsWith("Terminaux", StringComparison.Ordinal)), "terminal", "terminaux");
            return parts.Count == 0 ? "aucun objet" : string.Join(" · ", parts);
        }
    }
}

/// <summary>Case du filtre d'affichage (Portes, CVC, Électricité…, Paramètres Revit).</summary>
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
/// Arbre des résultats TRC (maquette Figma « WorkItemRunner – arbre de résultats TRC ») : tableau des pièces ; une pièce dépliée montre
/// ses paramètres puis un sous-tableau par catégorie (Portes, Équipements CVC, Terminaux électricité…), chacun avec ses colonnes ; chaque
/// ligne se déplie sur ses paramètres Revit. Une porte entre deux pièces figure sous chacune.
/// </summary>
public sealed class TrcTree
{
    public const string DoorKey = "door";
    public const string ParametersKey = "parametres";
    public const string NoRoom = "(sans pièce)";

    private const string RevitParameterPrefix = "c:parametres revit_";

    private static readonly string[] DisciplineOrder = ["cvc", "electricite", "plomberie", "autres"];

    private static readonly Dictionary<string, (string Label, string Color)> Disciplines = new(StringComparer.Ordinal)
    {
        ["cvc"] = ("CVC", "#0E7490"),
        ["electricite"] = ("électricité", "#7C3AED"),
        ["plomberie"] = ("plomberie", "#1D4ED8"),
        ["autres"] = ("autres", "#475569"),
    };

    private const string DoorColor = "#B45309";
    private const string ParameterColor = "#475569";

    /// <summary>Colonnes possibles d'un sous-tableau, dans l'ordre d'affichage ; seules celles renseignées dans le groupe apparaissent.</summary>
    private static readonly (string Code, string Header)[] ElementColumns =
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

    /// <summary>Codes d'un élément jamais repris dans les paramètres (déjà portés par le groupe ou la colonne « Pièces »).</summary>
    private static readonly HashSet<string> ElementStructural = new(StringComparer.Ordinal)
    {
        TrcCodes.Category, TrcCodes.SpaceList, "Space", TrcCodes.ElementDiscipline,
    };

    /// <summary>Codes d'une pièce affichés dans ses colonnes (le reste va dans ses paramètres).</summary>
    private static readonly HashSet<string> RoomColumns = new(StringComparer.Ordinal)
    {
        TrcCodes.Category, TrcCodes.Username, TrcCodes.Storey, TrcCodes.Surface, TrcCodes.Volume, TrcCodes.Height,
    };

    private TrcTree(IReadOnlyList<TrcRoomNode> rooms, IReadOnlyList<TrcFilter> filters)
    {
        Rooms = rooms;
        Filters = filters;
        foreach (var filter in filters)
        {
            filter.PropertyChanged += (_, _) => ApplyFilters();
        }
    }

    public IReadOnlyList<TrcRoomNode> Rooms { get; }

    /// <summary>Filtres d'affichage : un par catégorie présente (Portes, CVC…), puis « Paramètres Revit ».</summary>
    public IReadOnlyList<TrcFilter> Filters { get; }

    /// <summary>Déplie toutes les pièces, leurs sous-tableaux et les paramètres des pièces (pas ceux de chaque élément).</summary>
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
        foreach (var room in Rooms)
        {
            room.Parameters.IsVisible = shown.Contains(ParametersKey);
            foreach (var group in room.Groups)
            {
                group.IsVisible = shown.Contains(group.FilterKey);
                foreach (var row in group.Rows)
                {
                    row.Parameters.IsVisible = shown.Contains(ParametersKey);
                }
            }
        }
    }

    public static TrcTree Build(IReadOnlyList<TrcObject> objects)
    {
        string? First(TrcObject o, string code) => o.Values.TryGetValue(code, out var list) && list.Count > 0 ? string.Join(" | ", list) : null;
        var byPk = objects.ToDictionary(o => o.Pk, StringComparer.Ordinal);
        var elements = objects.Where(o => o.ParentPks.Count > 0).ToList();
        var roomObjects = objects.Where(o => o.ParentPks.Count == 0 && (First(o, TrcCodes.Category) ?? TrcCategories.Space) == TrcCategories.Space).ToList();
        var orphans = objects.Where(o => o.ParentPks.Count == 0 && !roomObjects.Contains(o)).ToList();
        string RoomName(TrcObject room) => First(room, TrcCodes.Username) ?? room.BimId;

        var usedKeys = new HashSet<string>(StringComparer.Ordinal);
        IReadOnlyList<TrcGroup> Groups(IEnumerable<TrcObject> items)
        {
            return items
                .GroupBy(item => First(item, TrcCodes.Category) ?? string.Empty, StringComparer.Ordinal)
                .Select(g => (Describe(g.Key), Items: g.ToList()))
                .OrderBy(g => g.Item1.Order)
                .Select(g =>
                {
                    var (title, key, color, _) = g.Item1;
                    usedKeys.Add(key);
                    return BuildGroup(title, key, color, g.Items);
                })
                .ToList();
        }

        TrcGroup BuildGroup(string title, string key, string color, IReadOnlyList<TrcObject> items)
        {
            var columns = ElementColumns.Where(c => items.Any(i => First(i, c.Code) is not null)).ToList();
            var pieces = items.Select(i => string.Join(" · ", i.ParentPks.Where(byPk.ContainsKey).Select(pk => RoomName(byPk[pk])))).ToList();
            var headers = new List<string>(columns.Select(c => c.Header)) { "Pièces" };
            var texts = items.Select((item, index) => columns.Select(c => Display(c.Code, First(item, c.Code))).Append(pieces[index]).ToList()).ToList();
            var widths = headers.Select((header, column) => Width(texts.Select(row => row[column]).Append(header))).ToList();
            var rows = items.Select((item, index) => new TrcElementNode(
                    item.BimId,
                    texts[index].Select((text, column) => new TrcCell(text, widths[column])).ToList(),
                    new TrcParameterBlock(Parameters(item, ElementStructural.Concat(columns.Select(c => c.Code)).ToHashSet(StringComparer.Ordinal)))))
                .ToList();
            return new TrcGroup(title, key, color, headers.Select((h, i) => new TrcCell(h, widths[i])).ToList(), rows);
        }

        var rooms = roomObjects
            .Select(room => new TrcRoomNode(
                room.BimId,
                RoomName(room),
                First(room, TrcCodes.Storey),
                First(room, TrcCodes.Surface),
                First(room, TrcCodes.Volume),
                First(room, TrcCodes.Height),
                new TrcParameterBlock(Parameters(room, RoomColumns)),
                Groups(elements.Where(e => e.ParentPks.Contains(room.Pk, StringComparer.Ordinal)))))
            .ToList();
        var homeless = orphans.Concat(elements.Where(e => !e.ParentPks.Any(byPk.ContainsKey))).ToList();
        if (homeless.Count > 0)
        {
            rooms.Add(new TrcRoomNode(string.Empty, NoRoom, null, null, null, null, new TrcParameterBlock([]), Groups(homeless)));
        }

        var filters = DisciplineOrder.Prepend(DoorKey)
            .Where(usedKeys.Contains)
            .Select(key => key == DoorKey ? new TrcFilter(key, "Portes", DoorColor) : new TrcFilter(key, Capitalize(Disciplines[key].Label), Disciplines[key].Color))
            .Append(new TrcFilter(ParametersKey, "Paramètres Revit", ParameterColor))
            .ToList();
        return new TrcTree(rooms, filters);
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

    /// <summary>Paramètres d'un objet : propriétés TRC hors colonnes (libellé court), puis paramètres Revit (<c>c:parametres revit_…</c>).</summary>
    private static List<TrcParameter> Parameters(TrcObject item, IReadOnlySet<string> shown)
    {
        var values = item.Values.Where(pair => !shown.Contains(pair.Key)).ToList();
        return values.Where(pair => !pair.Key.StartsWith(RevitParameterPrefix, StringComparison.Ordinal))
            .Select(pair => new TrcParameter(Label(pair.Key), string.Join(" | ", pair.Value)))
            .Prepend(new TrcParameter("bim_id", item.BimId))
            .Concat(values.Where(pair => pair.Key.StartsWith(RevitParameterPrefix, StringComparison.Ordinal))
                .Select(pair => new TrcParameter(pair.Key[RevitParameterPrefix.Length..], string.Join(" | ", pair.Value))))
            .ToList();
    }

    /// <summary>Libellé court d'un code TRC : <c>a:identification data_building</c> → <c>building</c>, <c>bim_object_username</c> → Nom.</summary>
    private static string Label(string code)
    {
        if (code == TrcCodes.Username)
        {
            return "Nom";
        }

        var colon = code.IndexOf(':');
        var underscore = code.IndexOf('_');
        return colon >= 0 && underscore > colon ? code[(underscore + 1)..] : code;
    }

    /// <summary>Valeur affichée : unité ajoutée aux dimensions de porte (<c>0.92m</c> → <c>0.92 m</c>, <c>2.13</c> → <c>2.13 m</c>).</summary>
    private static string? Display(string code, string? value) =>
        value is null ? null
        : code is TrcCodes.DoorWidth or TrcCodes.DoorHeight ? value.TrimEnd('m') + " m"
        : value;

    /// <summary>Largeur d'une colonne d'après son texte le plus long (≈ 7,5 px par caractère), entre 60 et 320 px.</summary>
    private static double Width(IEnumerable<string?> texts) =>
        Math.Clamp(texts.Max(t => t?.Length ?? 0) * 7.5 + 24, 60, 320);

    private static string Capitalize(string text) => char.ToUpperInvariant(text[0]) + text[1..];
}
