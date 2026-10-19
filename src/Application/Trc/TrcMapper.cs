using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EgRoomTracker.Application.Revit;
using EgRoomTracker.Contracts.Results;

namespace EgRoomTracker.Application.Trc;

/// <summary>Noms des paramètres de pièce lus pour l'identification TRC (<c>a:identification data_…</c>).</summary>
public static class TrcIdentification
{
    public const string Ouvrage = "SGP-DI-Ouvrage";
    public const string Typologie = "SGP-DI-Typologie_Espace";
    public const string LocalCode = "SGP-DI-Local_Code";

    public static IReadOnlyList<string> ParameterNames { get; } = [Ouvrage, Typologie, LocalCode];
}

/// <summary>Codes de propriété TRC : <c>{section}_{propriété}</c> en minuscules, section préfixée <c>a:</c> ou <c>b:</c> (tri des sections).</summary>
public static class TrcCodes
{
    public const string Username = "bim_object_username";

    // Type d'objet et rattachement à la pièce (noms des fichiers TRC de la plateforme, comme ParentGlobalId)
    public const string Category = "Category";

    /// <summary>
    /// Pièces contenant une porte, un équipement ou un terminal : une seule valeur, liste JSON des <c>pk</c> des <c>trc.bimobject</c>
    /// des pièces (<c>["trc.bimobject_…","trc.bimobject_…"]</c>).
    /// </summary>
    public const string SpaceList = "ListeSpaces";

    // A : identification
    public const string Building = "a:identification data_building";
    public const string Storey = "a:identification data_storey";
    public const string Classification = "a:identification data_classification";
    public const string Numero = "a:identification data_numero";
    public const string Ouvrage = "a:identification data_ouvrage";

    // A : identification d'un élément (porte, équipement, terminal)
    public const string ElementRevitId = "a:identification data_id revit";
    public const string ElementRevitCategory = "a:identification data_categorie";
    public const string ElementFamily = "a:identification data_famille";
    public const string ElementType = "a:identification data_type";
    public const string ElementMark = "a:identification data_repere";
    public const string ElementDiscipline = "a:identification data_discipline";
    public const string ElementLink = "a:identification data_lien";

    // B : dimensions
    public const string Surface = "b:dimensions_surface";
    public const string Volume = "b:dimensions_volume";
    public const string Height = "b:dimensions_hauteur";
    public const string HeightMeasured = "b:dimensions_hauteur mesuree";
    public const string HeightDeclared = "b:dimensions_hauteur renseignee";
    public const string HeightUnbounded = "b:dimensions_hauteur non limitee";
    public const string HeightLimiting = "b:dimensions_hauteur element limitant";

    // B : passage libre d'une porte
    public const string DoorWidth = "b:porte_largeur";
    public const string DoorHeight = "b:porte_hauteur";

    /// <summary>
    /// Code d'un paramètre Revit de l'objet (<c>roomParameters: all</c>) : section <c>C:Parametres Revit</c> (<c>c:</c> : après les sections
    /// <c>a:</c> et <c>b:</c>), propriété = nom du paramètre en minuscules (ex. <c>c:parametres revit_hauteur limite</c>).
    /// </summary>
    public static string RevitParameter(string name) => $"c:parametres revit_{name.Trim().ToLowerInvariant()}";
}

/// <summary>Valeurs de <see cref="TrcCodes.Category"/> : pièce, porte, et équipement ou terminal suivi de sa discipline.</summary>
public static class TrcCategories
{
    public const string Space = "Space";
    public const string Door = "Door";

    /// <summary>Équipement d'une discipline : <c>cvc equipement</c>, <c>plomberie equipement</c>…</summary>
    public static string Equipment(string? discipline) => $"{discipline ?? ElementDisciplines.Other} equipement";

    /// <summary>Terminal d'une discipline : <c>cvc terminal</c>, <c>electricite terminal</c>…</summary>
    public static string Terminal(string? discipline) => $"{discipline ?? ElementDisciplines.Other} terminal";
}

/// <summary>Valeur d'une propriété d'un objet ; <see cref="KeepNull"/> : écrite même sans valeur.</summary>
public sealed record TrcValue(string Code, string? Value, bool KeepNull = false);

/// <summary>Fichier d'annexe : texte complet d'une valeur trop longue pour <c>latest_value</c>.</summary>
public sealed record TrcAnnex(string FileName, string Text);

/// <summary>Contenu du fichier TRC : liste de fiches <c>{model, pk, fields}</c> et annexes.</summary>
public sealed record TrcDocument(JsonArray Fiches, IReadOnlyList<TrcAnnex> Annexes);

/// <summary>
/// Conversion de l'extraction en fiches de l'application TRC (<c>TRCValidationDict.json</c>), <b>données seules</b> : pas de
/// spécification, d'exigence, de statut ni de conformité. Une fiche <c>trc.bimobject</c> par pièce, puis une par porte, équipement et
/// terminal (<c>bim_id</c> = UniqueId, <c>code_file</c> = nom du RVT) ; chaque élément est rattaché à sa ou ses pièces par la propriété
/// <c>ListeSpaces</c> (liste JSON des <c>pk</c> des pièces) et n'apparaît qu'une fois, même s'il dessert deux pièces (porte). Une fiche
/// <c>trc.bimobjectpropertyvalue</c> par valeur, et <c>special.setcodefile</c> en fin.
/// Les <c>pk</c> sont stables (GUID déterministe de <c>code_file</c>, objet, code et rang) : un même élément garde le même
/// <c>pk</c> d'un lancement à l'autre. Une valeur de plus de 255 caractères est remplacée par « Voir détail en annexe » et son
/// texte écrit dans <c>Details_Annexes/</c>.
/// </summary>
public static class TrcMapper
{
    /// <summary>Longueur maximale d'une valeur ; au-delà, annexe (comportement de TRC).</summary>
    public const int MaxValueLength = 255;

    public const string AnnexNote = "Voir détail en annexe";
    public const string AnnexDirectory = "Details_Annexes";

    public static TrcDocument Map(ExtractionResult result)
    {
        var source = result.Summary.Source ?? throw new InvalidOperationException("Modèle source inconnu : export TRC impossible.");
        var codeFile = TrcNaming.CodeFile(source.FileName);
        var fiches = new JsonArray();
        var annexes = new List<TrcAnnex>();

        string ObjectPk(string uniqueId) => Pk("trc.bimobject", $"{codeFile}|{uniqueId}");
        var usernames = new HashSet<string>(StringComparer.Ordinal);
        var objects = result.Rooms
            .Select(room => (room.UniqueId, Values: RoomValues(room, Username(room, usernames)).ToList()))
            .Concat(Elements(result.Rooms).Select(element => (element.Item.UniqueId, Values: ElementValues(element, ElementUsername(element.Item, usernames), ObjectPk).ToList())))
            .ToList();

        var objectPk = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (uniqueId, _) in objects)
        {
            var pk = objectPk[uniqueId] = ObjectPk(uniqueId);
            fiches.Add(new JsonObject
            {
                ["model"] = "trc.bimobject",
                ["pk"] = pk,
                ["fields"] = new JsonObject { ["bim_id"] = uniqueId, ["code_file"] = codeFile },
            });
        }

        foreach (var (uniqueId, values) in objects)
        {
            var rank = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var value in values)
            {
                if (value.Value is null && !value.KeepNull)
                {
                    continue;
                }

                var text = value.Value;
                if (text is { Length: > MaxValueLength })
                {
                    annexes.Add(new TrcAnnex(AnnexFileName(uniqueId, value.Code, rank), text));
                    text = AnnexNote;
                }

                var n = rank[value.Code] = rank.GetValueOrDefault(value.Code) + 1;
                fiches.Add(new JsonObject
                {
                    ["model"] = "trc.bimobjectpropertyvalue",
                    ["pk"] = Pk("trc.bimobjectpropertyvalue", $"{codeFile}|{uniqueId}|{value.Code}|{n}"),
                    ["fields"] = new JsonObject
                    {
                        ["latest_value"] = text,
                        ["original_value"] = text,
                        ["bim_object_id"] = objectPk[uniqueId],
                        ["bim_property_code_property"] = value.Code,
                    },
                });
            }
        }

        fiches.Add(new JsonObject
        {
            ["model"] = "special.setcodefile",
            ["fields"] = new JsonObject { ["file_name"] = $"{codeFile}.rvt", ["code_file"] = codeFile },
        });
        return new TrcDocument(fiches, annexes);
    }

    /// <summary>Porte, équipement ou terminal exporté comme objet BIM, avec sa catégorie TRC et les pièces qui le contiennent (UniqueId).</summary>
    public sealed record TrcElement(RoomElementDto Item, string Category, IReadOnlyList<string> Spaces);

    /// <summary>
    /// Éléments des pièces, chacun une seule fois (clé : UniqueId), dans l'ordre de première apparition (pièces dans l'ordre du
    /// résultat ; portes, puis équipements, puis terminaux) ; une porte entre deux pièces garde les deux pièces dans <c>Spaces</c>.
    /// </summary>
    public static IReadOnlyList<TrcElement> Elements(IEnumerable<RoomDto> rooms)
    {
        var order = new List<string>();
        var byId = new Dictionary<string, (RoomElementDto Item, string Category, List<string> Spaces)>(StringComparer.Ordinal);
        foreach (var room in rooms)
        {
            var items = (room.Doors ?? []).Select(item => (item, Category: TrcCategories.Door))
                .Concat((room.Equipment ?? []).Select(item => (item, Category: TrcCategories.Equipment(item.Discipline))))
                .Concat((room.Terminals ?? []).Select(item => (item, Category: TrcCategories.Terminal(item.Discipline))));
            foreach (var (item, category) in items)
            {
                if (!byId.TryGetValue(item.UniqueId, out var entry))
                {
                    byId[item.UniqueId] = entry = (item, category, []);
                    order.Add(item.UniqueId);
                }

                if (!entry.Spaces.Contains(room.UniqueId, StringComparer.Ordinal))
                {
                    entry.Spaces.Add(room.UniqueId);
                }
            }
        }

        return order.Select(id => byId[id]).Select(entry => new TrcElement(entry.Item, entry.Category, entry.Spaces)).ToList();
    }

    /// <summary>
    /// Nom d'objet BIM : <c>{niveau}_{nom}_{numéro}</c> sans espaces (nom de secours de l'application TRC, celui qu'elle produit
    /// réellement), suffixé <c>_1</c>, <c>_2</c>… en cas de doublon.
    /// </summary>
    public static string Username(RoomDto room, HashSet<string> used) => Unique($"{room.Level?.Name}_{room.Name}_{room.Number}", used);

    /// <summary>Nom d'objet BIM d'un élément : <c>{niveau}_{famille}_{ElementId}</c> sans espaces, unique parmi tous les objets.</summary>
    public static string ElementUsername(RoomElementDto element, HashSet<string> used) => Unique($"{element.Level}_{element.Family}_{element.ElementId}", used);

    private static string Unique(string raw, HashSet<string> used)
    {
        var baseName = raw.Replace(" ", string.Empty);
        var name = baseName;
        for (var counter = 1; !used.Add(name); counter++)
        {
            name = $"{baseName}_{counter.ToString(CultureInfo.InvariantCulture)}";
        }

        return name;
    }

    /// <summary>
    /// Valeurs d'une pièce, dans l'ordre de sortie : catégorie, identification, dimensions, paramètres Revit. Les portes et les
    /// équipements sont des objets à part (<see cref="ElementValues"/>), rattachés à la pièce par <c>ListeSpaces</c>.
    /// </summary>
    public static IEnumerable<TrcValue> RoomValues(RoomDto room, string username)
    {
        var named = room.NamedParameters;
        string? Named(string name) => named is not null && named.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

        yield return new(TrcCodes.Category, TrcCategories.Space);

        // A : identification (le bâtiment est « A », comme dans TRC)
        yield return new(TrcCodes.Ouvrage, Named(TrcIdentification.Ouvrage));
        yield return new(TrcCodes.Building, "A");
        yield return new(TrcCodes.Storey, string.IsNullOrWhiteSpace(room.Level?.Name) ? null : room.Level.Name);
        yield return new(TrcCodes.Classification, Named(TrcIdentification.Typologie));
        yield return new(TrcCodes.Numero, Named(TrcIdentification.LocalCode));
        yield return new(TrcCodes.Username, username);

        // B : dimensions
        yield return new(TrcCodes.Surface, Number(room.AreaM2));
        yield return new(TrcCodes.Volume, room.VolumeStatus == VolumeStatus.Computed ? Number(room.VolumeM3) : null);
        if (room.Heights is { } heights)
        {
            yield return new(TrcCodes.Height, Number(heights.EffectiveM));
            yield return new(TrcCodes.HeightMeasured, Number(heights.MeasuredMinM));
            yield return new(TrcCodes.HeightDeclared, Number(heights.DeclaredM));
            yield return new(TrcCodes.HeightUnbounded, Number(room.UnboundedHeightM));
            yield return new(TrcCodes.HeightLimiting, heights.LimitingCategory is null ? null : $"{heights.LimitingCategory} ({heights.LimitingDocument})");
        }

        // C : tous les paramètres de la pièce (roomParameters: all), un code par paramètre, triés par nom
        foreach (var value in ParameterValues(room.Parameters))
        {
            yield return value;
        }
    }

    /// <summary>
    /// Valeurs d'une porte, d'un équipement ou d'un terminal : catégorie, pièces (<c>ListeSpaces</c>, liste JSON des <c>pk</c> donnés par
    /// <paramref name="roomPk"/>), identification, passage libre (portes) et paramètres Revit de l'instance (<c>roomParameters: all</c>).
    /// Une dimension inconnue n'est pas écrite (jamais 0).
    /// </summary>
    public static IEnumerable<TrcValue> ElementValues(TrcElement element, string username, Func<string, string> roomPk)
    {
        var item = element.Item;
        yield return new(TrcCodes.Category, element.Category);
        yield return new(TrcCodes.SpaceList, JsonSerializer.Serialize(element.Spaces.Select(roomPk)));

        // A : identification
        yield return new(TrcCodes.Storey, string.IsNullOrWhiteSpace(item.Level) ? null : item.Level);
        yield return new(TrcCodes.Numero, string.IsNullOrWhiteSpace(item.Number) ? null : item.Number);
        yield return new(TrcCodes.ElementMark, string.IsNullOrWhiteSpace(item.Mark) ? null : item.Mark);
        yield return new(TrcCodes.ElementRevitId, item.ElementId);
        yield return new(TrcCodes.ElementRevitCategory, item.Category);
        yield return new(TrcCodes.ElementFamily, item.Family);
        yield return new(TrcCodes.ElementType, item.Type);
        yield return new(TrcCodes.ElementDiscipline, item.Discipline);
        yield return new(TrcCodes.ElementLink, item.LinkName);
        yield return new(TrcCodes.Username, username);

        // B : passage libre (portes) ; largeur avec « m », hauteur sans unité, comme TRC
        yield return new(TrcCodes.DoorWidth, item.WidthM is > 0 ? Number(item.WidthM) + "m" : null);
        yield return new(TrcCodes.DoorHeight, Number(item.HeightM));

        // C : paramètres de l'instance
        foreach (var value in ParameterValues(item.Parameters))
        {
            yield return value;
        }
    }

    private static IEnumerable<TrcValue> ParameterValues(IReadOnlyDictionary<string, object?>? parameters) =>
        parameters is null ? [] : parameters.Select(pair => new TrcValue(TrcCodes.RevitParameter(pair.Key), ParameterText(pair.Value)));

    /// <summary>
    /// Texte d'un paramètre : nombres en notation invariante (4 décimales au plus, sans zéros inutiles), booléens Revit (entiers) tels quels ;
    /// null pour un paramètre sans valeur ou un texte vide, qui n'est pas écrit.
    /// </summary>
    private static string? ParameterText(object? value) => value switch
    {
        null => null,
        string text => string.IsNullOrWhiteSpace(text) ? null : text,
        double number => double.IsFinite(number) ? number.ToString("0.####", CultureInfo.InvariantCulture) : null,
        float number => double.IsFinite(number) ? number.ToString("0.####", CultureInfo.InvariantCulture) : null,
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };

    /// <summary>Nombre à 2 décimales, point décimal, sans unité ; null si absent ou non positif (jamais 0 pour une dimension inconnue).</summary>
    private static string? Number(double? value) =>
        value is > 0 && double.IsFinite(value.Value) ? value.Value.ToString("F2", CultureInfo.InvariantCulture) : null;

    private static string AnnexFileName(string uniqueId, string code, Dictionary<string, int> rank)
    {
        var raw = $"{uniqueId}_{code}_{(rank.GetValueOrDefault(code) + 1).ToString(CultureInfo.InvariantCulture)}";
        var safe = new StringBuilder(raw.Length);
        foreach (var c in raw)
        {
            safe.Append(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_');
        }

        return safe + ".txt";
    }

    /// <summary><c>pk</c> stable : <c>{modèle}_{GUID}</c>, GUID déterministe (SHA-1 de la clé, bits de version 5).</summary>
    private static string Pk(string model, string key)
    {
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(key));
        hash[6] = (byte)((hash[6] & 0x0F) | 0x50);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        // Ordre des octets « réseau » (RFC 4122) : sinon .NET inverse les trois premiers groupes et la version 5 n'apparaît pas.
        return $"{model}_{new Guid(hash.AsSpan(0, 16), bigEndian: true).ToString("D")}";
    }
}
