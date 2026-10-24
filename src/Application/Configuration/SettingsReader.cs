using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using EgRoomTracker.Contracts.Codes;
using EgRoomTracker.Contracts.Configuration;

namespace EgRoomTracker.Application.Configuration;

/// <summary>
/// Lecture et validation de <c>settings.json</c> (section 2), sans accès au répertoire d'entrée.
/// <see cref="ExtractionSettings.MainModelPath"/> et <see cref="ExtractionSettings.PhaseIds"/> sont retournés tels que lus ;
/// <see cref="ExtractionSettings.PhaseElementIds"/> porte les ElementId typés. La résolution du RVT relève de
/// <see cref="MainModelResolver"/>.
/// </summary>
public static partial class SettingsReader
{
    /// <summary>Version majeure prise en charge et version complète dont les propriétés sont connues.</summary>
    public const int SupportedMajor = SchemaVersions.SettingsMajor;

    /// <summary>Taille maximale de <c>settings.json</c>.</summary>
    public const int MaxSizeBytes = 1024 * 1024;

    private static readonly string[] KnownProperties =
        ["schemaVersion", "mainModelPath", "phaseIds", "volumeMode", "sharedParameters", "roomParameters", "elements", "elementCategories", "roomImages", "imageWidthPx", "roomHeights", "heightStepM", "linkedElements", "outputFormat"];
    private static readonly string[] KnownMappingProperties = ["key", "guid"];

    public static ExtractionSettings Read(string settingsPath, OutputFormat defaultOutputFormat = OutputFormat.Contract)
    {
        var text = ReadText(settingsPath);
        return Parse(text, defaultOutputFormat);
    }

    /// <summary>
    /// Valide le contenu de <c>settings.json</c>. <paramref name="defaultOutputFormat"/> s'applique quand <c>outputFormat</c>
    /// est absent ou <c>null</c> ; un <c>outputFormat</c> explicite est toujours respecté. Sans option : <see cref="OutputFormat.Contract"/>.
    /// </summary>
    public static ExtractionSettings Parse(string json, OutputFormat defaultOutputFormat = OutputFormat.Contract)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
        }
        catch (JsonException ex)
        {
            throw Invalid($"JSON illisible (ligne {ex.LineNumber + 1}).", ex);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw Invalid("la racine doit être un objet JSON.");
            }

            var properties = ReadObject(root, "settings.json");
            var schemaVersion = ReadSchemaVersion(properties, out var minor);

            // Version connue (1.0) : propriété inconnue refusée (faute de frappe). Mineure plus récente : ajouts facultatifs ignorés.
            if (minor == 0)
            {
                RejectUnknown(properties, KnownProperties, "settings.json");
            }

            var phases = ReadPhaseIds(properties);
            return new ExtractionSettings
            {
                SchemaVersion = schemaVersion,
                MainModelPath = ReadMainModelPath(properties),
                PhaseIds = phases?.Raw,
                PhaseElementIds = phases?.Ids,
                VolumeMode = ReadVolumeMode(properties),
                SharedParameters = ReadSharedParameters(properties, strict: minor == 0),
                RoomParameters = ReadRoomParameters(properties),
                Elements = ReadElements(properties),
                ElementCategories = ReadElementCategories(properties),
                RoomImages = ReadRoomImages(properties),
                ImageWidthPx = ReadImageWidth(properties),
                RoomHeights = ReadRoomHeights(properties),
                HeightStepM = ReadHeightStep(properties),
                LinkedElements = ReadLinkedElements(properties),
                OutputFormat = ReadOutputFormat(properties, defaultOutputFormat),
            };
        }
    }

    private static string ReadText(string settingsPath)
    {
        byte[] bytes;
        try
        {
            var info = new FileInfo(settingsPath);
            if (!info.Exists)
            {
                throw Invalid("fichier absent.");
            }

            if (info.Length > MaxSizeBytes)
            {
                throw Invalid($"fichier trop volumineux ({info.Length} octets, maximum {MaxSizeBytes}).");
            }

            bytes = File.ReadAllBytes(settingsPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw Invalid("fichier illisible.", ex);
        }

        try
        {
            var preamble = Encoding.UTF8.GetPreamble();
            var offset = bytes.AsSpan().StartsWith(preamble) ? preamble.Length : 0;
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes, offset, bytes.Length - offset);
        }
        catch (DecoderFallbackException ex)
        {
            throw Invalid("encodage invalide, UTF-8 attendu.", ex);
        }
    }

    private static string ReadSchemaVersion(IReadOnlyDictionary<string, JsonElement> properties, out int minor)
    {
        if (!properties.TryGetValue("schemaVersion", out var element))
        {
            throw Invalid("schemaVersion obligatoire.");
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            throw Invalid("schemaVersion doit être une chaîne (ex. \"1.0\").");
        }

        var value = element.GetString()!;
        var match = SchemaVersionPattern().Match(value);
        if (!match.Success
            || !int.TryParse(match.Groups[1].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out var major)
            || !int.TryParse(match.Groups[2].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out minor))
        {
            throw Invalid($"schemaVersion « {Truncate(value)} » mal formée (majeure.mineure attendu, ex. \"1.0\").");
        }

        if (major != SupportedMajor)
        {
            throw new ExtractionFailedException(
                ErrorCodes.ConfigSchemaUnsupported,
                Stages.Configuration,
                $"settings.json : schemaVersion {value} non prise en charge (version majeure {SupportedMajor} attendue).");
        }

        return value;
    }

    private static string? ReadMainModelPath(IReadOnlyDictionary<string, JsonElement> properties)
    {
        if (!properties.TryGetValue("mainModelPath", out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            throw Invalid("mainModelPath doit être une chaîne ou null.");
        }

        var value = element.GetString()!;
        if (string.IsNullOrWhiteSpace(value))
        {
            throw Invalid("mainModelPath vide ; utiliser null pour la détection automatique.");
        }

        return value;
    }

    private static (IReadOnlyList<string> Raw, IReadOnlyList<long> Ids)? ReadPhaseIds(IReadOnlyDictionary<string, JsonElement> properties)
    {
        if (!properties.TryGetValue("phaseIds", out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            throw Invalid("phaseIds doit être un tableau d'ElementId (chaînes) ou null.");
        }

        if (element.GetArrayLength() == 0)
        {
            throw Invalid("phaseIds vide ; utiliser null pour toutes les phases.");
        }

        var raw = new List<string>();
        var ids = new List<long>();
        var index = 0;
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String
                || !ElementIdPattern().IsMatch(item.GetString()!)
                || !long.TryParse(item.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                || id <= 0)
            {
                throw Invalid($"phaseIds[{index}] : ElementId entier positif attendu sous forme de chaîne (ex. \"86961\").");
            }

            if (ids.Contains(id))
            {
                throw Invalid($"phaseIds[{index}] : ElementId {id.ToString(CultureInfo.InvariantCulture)} en double.");
            }

            raw.Add(item.GetString()!);
            ids.Add(id);
            index++;
        }

        return (raw, ids);
    }

    private static VolumeMode ReadVolumeMode(IReadOnlyDictionary<string, JsonElement> properties)
    {
        if (!properties.TryGetValue("volumeMode", out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return VolumeMode.Preserve;
        }

        return element.ValueKind == JsonValueKind.String
            ? element.GetString() switch
            {
                "preserve" => VolumeMode.Preserve,
                "compute" => VolumeMode.Compute,
                var other => throw Invalid($"volumeMode « {Truncate(other!)} » inconnu (preserve ou compute)."),
            }
            : throw Invalid("volumeMode doit être \"preserve\" ou \"compute\".");
    }

    private static RoomParametersMode ReadRoomParameters(IReadOnlyDictionary<string, JsonElement> properties)
    {
        if (!properties.TryGetValue("roomParameters", out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return RoomParametersMode.None;
        }

        return element.ValueKind == JsonValueKind.String
            ? element.GetString() switch
            {
                "none" => RoomParametersMode.None,
                "all" => RoomParametersMode.All,
                var other => throw Invalid($"roomParameters « {Truncate(other!)} » inconnu (none ou all)."),
            }
            : throw Invalid("roomParameters doit être \"none\" ou \"all\".");
    }

    private static IReadOnlyList<ElementKind> ReadElements(IReadOnlyDictionary<string, JsonElement> properties)
    {
        if (!properties.TryGetValue("elements", out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return [];
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            throw Invalid($"elements doit être un tableau de noms ({string.Join(", ", ElementKinds.Names)}) ou null.");
        }

        var kinds = new List<ElementKind>();
        var index = 0;
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || !ElementKinds.TryParse(item.GetString(), out var kind))
            {
                throw Invalid($"elements[{index}] : nom inconnu (attendus : {string.Join(", ", ElementKinds.Names)}).");
            }

            if (kinds.Contains(kind))
            {
                throw Invalid($"elements[{index}] : « {ElementKinds.ToName(kind)} » en double.");
            }

            kinds.Add(kind);
            index++;
        }

        return kinds;
    }

    private static bool ReadRoomImages(IReadOnlyDictionary<string, JsonElement> properties)
    {
        if (!properties.TryGetValue("roomImages", out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return false;
        }

        return element.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? element.GetBoolean()
            : throw Invalid("roomImages doit être true ou false.");
    }

    private static OutputFormat ReadOutputFormat(IReadOnlyDictionary<string, JsonElement> properties, OutputFormat defaultOutputFormat)
    {
        if (!properties.TryGetValue("outputFormat", out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return defaultOutputFormat;
        }

        return element.ValueKind == JsonValueKind.String
            ? element.GetString() switch
            {
                "contract" => OutputFormat.Contract,
                "trc" => OutputFormat.Trc,
                "both" => OutputFormat.Both,
                "generic" => OutputFormat.Generic,
                var other => throw Invalid($"outputFormat « {Truncate(other!)} » inconnu (contract, trc, both ou generic)."),
            }
            : throw Invalid("outputFormat doit être \"contract\", \"trc\", \"both\" ou \"generic\".");
    }

    private static bool ReadLinkedElements(IReadOnlyDictionary<string, JsonElement> properties)
    {
        if (!properties.TryGetValue("linkedElements", out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        return element.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? element.GetBoolean()
            : throw Invalid("linkedElements doit être true ou false.");
    }

    private static bool ReadRoomHeights(IReadOnlyDictionary<string, JsonElement> properties)
    {
        if (!properties.TryGetValue("roomHeights", out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return false;
        }

        return element.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? element.GetBoolean()
            : throw Invalid("roomHeights doit être true ou false.");
    }

    private static double ReadHeightStep(IReadOnlyDictionary<string, JsonElement> properties)
    {
        if (!properties.TryGetValue("heightStepM", out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return ExtractionSettings.DefaultHeightStepM;
        }

        if (element.ValueKind != JsonValueKind.Number || !element.TryGetDouble(out var step) || !double.IsFinite(step)
            || step < ExtractionSettings.MinHeightStepM || step > ExtractionSettings.MaxHeightStepM)
        {
            throw Invalid($"heightStepM doit être un nombre de {ExtractionSettings.MinHeightStepM.ToString(CultureInfo.InvariantCulture)} à {ExtractionSettings.MaxHeightStepM.ToString(CultureInfo.InvariantCulture)} (mètres).");
        }

        return step;
    }

    private static int ReadImageWidth(IReadOnlyDictionary<string, JsonElement> properties)
    {
        if (!properties.TryGetValue("imageWidthPx", out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return ExtractionSettings.DefaultImageWidthPx;
        }

        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out var width)
            || width < ExtractionSettings.MinImageWidthPx || width > ExtractionSettings.MaxImageWidthPx)
        {
            throw Invalid($"imageWidthPx doit être un entier de {ExtractionSettings.MinImageWidthPx} à {ExtractionSettings.MaxImageWidthPx} (pixels).");
        }

        return width;
    }

    private static IReadOnlyDictionary<ElementKind, IReadOnlyList<string>> ReadElementCategories(IReadOnlyDictionary<string, JsonElement> properties)
    {
        var result = new Dictionary<ElementKind, IReadOnlyList<string>>();
        if (!properties.TryGetValue("elementCategories", out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return result;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            throw Invalid("elementCategories doit être un objet { famille : [catégories] } ou null.");
        }

        foreach (var (name, list) in ReadObject(element, "elementCategories"))
        {
            if (!ElementKinds.TryParse(name, out var kind))
            {
                throw Invalid($"elementCategories : famille « {Truncate(name)} » inconnue (attendues : {string.Join(", ", ElementKinds.Names)}).");
            }

            if (list.ValueKind != JsonValueKind.Array || list.GetArrayLength() == 0)
            {
                throw Invalid($"elementCategories.{name} doit être un tableau non vide de noms de catégories (ex. \"OST_PlumbingFixtures\").");
            }

            var categories = new List<string>();
            var index = 0;
            foreach (var item in list.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String || !CategoryNamePattern().IsMatch(item.GetString()!))
                {
                    throw Invalid($"elementCategories.{name}[{index}] : nom de catégorie attendu, de la forme OST_… (ex. \"OST_PlumbingFixtures\").");
                }

                if (categories.Contains(item.GetString()!, StringComparer.Ordinal))
                {
                    throw Invalid($"elementCategories.{name}[{index}] : « {item.GetString()} » en double.");
                }

                categories.Add(item.GetString()!);
                index++;
            }

            result[kind] = categories;
        }

        return result;
    }

    private static IReadOnlyList<SharedParameterMapping> ReadSharedParameters(IReadOnlyDictionary<string, JsonElement> properties, bool strict)
    {
        if (!properties.TryGetValue("sharedParameters", out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return [];
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            throw Invalid("sharedParameters doit être un tableau d'objets { key, guid }.");
        }

        var result = new List<SharedParameterMapping>();
        var index = 0;
        foreach (var item in element.EnumerateArray())
        {
            var path = $"sharedParameters[{index}]";
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw Invalid($"{path} doit être un objet {{ key, guid }}.");
            }

            var mapping = ReadObject(item, path);
            if (strict)
            {
                RejectUnknown(mapping, KnownMappingProperties, path);
            }

            if (!mapping.TryGetValue("key", out var keyElement) || keyElement.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(keyElement.GetString()))
            {
                throw Invalid($"{path}.key : chaîne non vide obligatoire.");
            }

            var key = keyElement.GetString()!;
            if (key.Trim() != key)
            {
                throw Invalid($"{path}.key « {Truncate(key)} » : espaces en début ou fin interdits.");
            }

            if (result.Any(m => string.Equals(m.Key, key, StringComparison.Ordinal)))
            {
                throw Invalid($"{path}.key « {Truncate(key)} » en double ; chaque clé doit être unique.");
            }

            if (!mapping.TryGetValue("guid", out var guidElement) || guidElement.ValueKind != JsonValueKind.String
                || !Guid.TryParseExact(guidElement.GetString(), "D", out var guid))
            {
                throw Invalid($"{path}.guid : GUID attendu au format xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx.");
            }

            if (guid == Guid.Empty)
            {
                throw Invalid($"{path}.guid : GUID nul interdit.");
            }

            result.Add(new SharedParameterMapping(key, guid));
            index++;
        }

        return result;
    }

    /// <summary>Propriétés d'un objet ; une propriété en double est refusée (JSON ambigu).</summary>
    private static Dictionary<string, JsonElement> ReadObject(JsonElement element, string path)
    {
        var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!properties.TryAdd(property.Name, property.Value))
            {
                throw Invalid($"{path} : propriété « {Truncate(property.Name)} » en double.");
            }
        }

        return properties;
    }

    private static void RejectUnknown(IReadOnlyDictionary<string, JsonElement> properties, string[] known, string path)
    {
        var unknown = properties.Keys.FirstOrDefault(k => !known.Contains(k, StringComparer.Ordinal));
        if (unknown is not null)
        {
            throw Invalid($"{path} : propriété inconnue « {Truncate(unknown)} » (attendues : {string.Join(", ", known)}).");
        }
    }

    private static ExtractionFailedException Invalid(string message, Exception? inner = null) =>
        new(ErrorCodes.ConfigInvalid, Stages.Configuration, $"settings.json : {message}", inner);

    private static string Truncate(string value) => value.Length <= 80 ? value : value[..80] + "…";

    [GeneratedRegex(@"^(0|[1-9][0-9]{0,8})\.(0|[1-9][0-9]{0,8})$", RegexOptions.CultureInvariant)]
    private static partial Regex SchemaVersionPattern();

    [GeneratedRegex(@"^[0-9]{1,19}$", RegexOptions.CultureInvariant)]
    private static partial Regex ElementIdPattern();

    [GeneratedRegex(@"^OST_[A-Za-z0-9_]{1,80}$", RegexOptions.CultureInvariant)]
    private static partial Regex CategoryNamePattern();
}
