using System.Globalization;
using System.Security.Cryptography;
using System.Text;
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

    // A : identification
    public const string Building = "a:identification data_building";
    public const string Storey = "a:identification data_storey";
    public const string Classification = "a:identification data_classification";
    public const string Numero = "a:identification data_numero";
    public const string Ouvrage = "a:identification data_ouvrage";

    // B : dimensions
    public const string Surface = "b:dimensions_surface";
    public const string Volume = "b:dimensions_volume";
    public const string Height = "b:dimensions_hauteur";
    public const string HeightMeasured = "b:dimensions_hauteur mesuree";
    public const string HeightDeclared = "b:dimensions_hauteur renseignee";
    public const string HeightUnbounded = "b:dimensions_hauteur non limitee";
    public const string HeightLimiting = "b:dimensions_hauteur element limitant";

    // B : porte (une valeur par porte, même ordre pour toutes les propriétés)
    public const string DoorCount = "b:porte_nombre";
    public const string DoorNumber = "b:porte_numero";
    public const string DoorId = "b:porte_id";
    public const string DoorFamily = "b:porte_famille";
    public const string DoorType = "b:porte_type";
    public const string DoorWidth = "b:porte_largeur";
    public const string DoorHeight = "b:porte_hauteur";

    // B : équipements (total), puis une section par discipline
    public const string EquipmentTotal = "b:equipements_quantite";

    /// <summary>
    /// Code d'un paramètre de la pièce (<c>roomParameters: all</c>) : section <c>C:Parametres Revit</c> (<c>c:</c> : après les sections
    /// <c>a:</c> et <c>b:</c>), propriété = nom du paramètre en minuscules (ex. <c>c:parametres revit_hauteur limite</c>).
    /// </summary>
    public static string RevitParameter(string name) => $"c:parametres revit_{name.Trim().ToLowerInvariant()}";

    public static string EquipmentQuantity(string discipline) => $"b:equipements {discipline}_quantite";

    public static string EquipmentList(string discipline) => $"b:equipements {discipline}_liste";
}

/// <summary>Valeur d'une propriété pour une pièce ; <see cref="KeepNull"/> : écrite même sans valeur (alignement des listes de portes).</summary>
public sealed record TrcValue(string Code, string? Value, bool KeepNull = false);

/// <summary>Fichier d'annexe : texte complet d'une valeur trop longue pour <c>latest_value</c>.</summary>
public sealed record TrcAnnex(string FileName, string Text);

/// <summary>Contenu du fichier TRC : liste de fiches <c>{model, pk, fields}</c> et annexes.</summary>
public sealed record TrcDocument(JsonArray Fiches, IReadOnlyList<TrcAnnex> Annexes);

/// <summary>
/// Conversion de l'extraction en fiches de l'application TRC (<c>TRCValidationDict.json</c>), <b>données seules</b> : pas de
/// spécification, d'exigence, de statut ni de conformité. Une fiche <c>trc.bimobject</c> par pièce (<c>bim_id</c> = UniqueId,
/// <c>code_file</c> = nom du RVT), une fiche <c>trc.bimobjectpropertyvalue</c> par valeur, et <c>special.setcodefile</c> en fin.
/// Les <c>pk</c> sont stables (GUID déterministe de <c>code_file</c>, pièce, code et rang) : un même élément garde le même
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

        var objectPk = result.Rooms.ToDictionary(room => room.UniqueId, room => Pk("trc.bimobject", $"{codeFile}|{room.UniqueId}"), StringComparer.Ordinal);
        foreach (var room in result.Rooms)
        {
            fiches.Add(new JsonObject
            {
                ["model"] = "trc.bimobject",
                ["pk"] = objectPk[room.UniqueId],
                ["fields"] = new JsonObject { ["bim_id"] = room.UniqueId, ["code_file"] = codeFile },
            });
        }

        var usernames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var room in result.Rooms)
        {
            var rank = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var value in RoomValues(room, Username(room, usernames)))
            {
                if (value.Value is null && !value.KeepNull)
                {
                    continue;
                }

                var text = value.Value;
                if (text is { Length: > MaxValueLength })
                {
                    annexes.Add(new TrcAnnex(AnnexFileName(room.UniqueId, value.Code, rank), text));
                    text = AnnexNote;
                }

                var n = rank[value.Code] = rank.GetValueOrDefault(value.Code) + 1;
                fiches.Add(new JsonObject
                {
                    ["model"] = "trc.bimobjectpropertyvalue",
                    ["pk"] = Pk("trc.bimobjectpropertyvalue", $"{codeFile}|{room.UniqueId}|{value.Code}|{n}"),
                    ["fields"] = new JsonObject
                    {
                        ["latest_value"] = text,
                        ["original_value"] = text,
                        ["bim_object_id"] = objectPk[room.UniqueId],
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

    /// <summary>
    /// Nom d'objet BIM : <c>{niveau}_{nom}_{numéro}</c> sans espaces (nom de secours de l'application TRC, celui qu'elle produit
    /// réellement), suffixé <c>_1</c>, <c>_2</c>… en cas de doublon.
    /// </summary>
    public static string Username(RoomDto room, HashSet<string> used)
    {
        var baseName = $"{room.Level?.Name}_{room.Name}_{room.Number}".Replace(" ", string.Empty);
        var name = baseName;
        for (var counter = 1; !used.Add(name); counter++)
        {
            name = $"{baseName}_{counter.ToString(CultureInfo.InvariantCulture)}";
        }

        return name;
    }

    /// <summary>Valeurs d'une pièce, dans l'ordre de sortie : identification, dimensions, portes, équipements par discipline.</summary>
    public static IEnumerable<TrcValue> RoomValues(RoomDto room, string username)
    {
        var named = room.NamedParameters;
        string? Named(string name) => named is not null && named.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

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

        // B : portes, une valeur par porte dans le même ordre ; une dimension inconnue reste vide (jamais 0)
        if (room.Doors is { } doors)
        {
            yield return new(TrcCodes.DoorCount, doors.Count.ToString(CultureInfo.InvariantCulture));
            foreach (var code in new[] { TrcCodes.DoorNumber, TrcCodes.DoorId, TrcCodes.DoorFamily, TrcCodes.DoorType, TrcCodes.DoorWidth, TrcCodes.DoorHeight })
            {
                foreach (var door in doors)
                {
                    yield return new(code, DoorValue(code, door), KeepNull: true);
                }
            }
        }

        // C : tous les paramètres de la pièce (roomParameters: all), un code par paramètre, triés par nom
        if (room.Parameters is { } parameters)
        {
            foreach (var (name, value) in parameters)
            {
                yield return new(TrcCodes.RevitParameter(name), ParameterText(value));
            }
        }

        // B : équipements et terminaux, une section par discipline
        if (room.Equipment is not null || room.Terminals is not null)
        {
            var items = (room.Equipment ?? []).Concat(room.Terminals ?? []).ToList();
            yield return new(TrcCodes.EquipmentTotal, Quantity(items.Count));
            foreach (var discipline in ElementDisciplines.All)
            {
                var ofDiscipline = items.Where(item => (item.Discipline ?? ElementDisciplines.Other) == discipline).ToList();
                yield return new(TrcCodes.EquipmentQuantity(discipline), Quantity(ofDiscipline.Count));
                if (ofDiscipline.Count > 0)
                {
                    yield return new(TrcCodes.EquipmentList(discipline), Summarize(ofDiscipline));
                }
            }
        }
    }

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

    private static string? DoorValue(string code, RoomElementDto door) => code switch
    {
        TrcCodes.DoorNumber => door.Number,
        TrcCodes.DoorId => door.ElementId,
        TrcCodes.DoorFamily => door.Family,
        TrcCodes.DoorType => door.Type,
        TrcCodes.DoorWidth => door.WidthM is > 0 ? Number(door.WidthM) + "m" : null,
        TrcCodes.DoorHeight => Number(door.HeightM),
        _ => null,
    };

    /// <summary>Nombre à 2 décimales, point décimal, sans unité ; null si absent ou non positif (jamais 0 pour une dimension inconnue).</summary>
    private static string? Number(double? value) =>
        value is > 0 && double.IsFinite(value.Value) ? value.Value.ToString("F2", CultureInfo.InvariantCulture) : null;

    /// <summary>« N équipements présents dans le local » : formulation des sorties TRC les plus récentes.</summary>
    private static string Quantity(int count) => $"{count.ToString(CultureInfo.InvariantCulture)} équipements présents dans le local";

    /// <summary>« Catégorie : Famille [lien] × n », groupé et trié, séparé par « | ».</summary>
    private static string Summarize(IEnumerable<RoomElementDto> items) =>
        string.Join(" | ", items
            .GroupBy(item => (item.Category, Family: item.Family ?? "(sans famille)", item.LinkName))
            .OrderBy(group => group.Key.Category, StringComparer.Ordinal)
            .ThenBy(group => group.Key.Family, StringComparer.Ordinal)
            .ThenBy(group => group.Key.LinkName, StringComparer.Ordinal)
            .Select(group => $"{group.Key.Category} : {group.Key.Family}{(group.Key.LinkName is null ? string.Empty : $" [{group.Key.LinkName}]")} × {group.Count().ToString(CultureInfo.InvariantCulture)}"));

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
