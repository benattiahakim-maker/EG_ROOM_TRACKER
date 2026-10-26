using System.Text.Json;
using System.Text.Json.Serialization;

namespace EgRoomTracker.Contracts.Generic;

/// <summary>Version du contrat de l'enveloppe commune des résultats de la plateforme.</summary>
public static class GenericContract
{
    /// <summary>Version de l'enveloppe (<c>contractVersion</c>), celle de la plateforme.</summary>
    public const string Version = "1.0";

    /// <summary>Identifiant de ce service sur la plateforme (<c>serviceId</c>).</summary>
    public const string ServiceId = "pieces-roomtracker";

    /// <summary>Clé de la maquette principale dans <c>models[]</c>.</summary>
    public const string MainModelKey = "main";

    /// <summary>Propriété réservée : UniqueId Revit de l'objet, pour le retrouver dans le viewer 3D.</summary>
    public const string ViewerIdKey = "viewerId";
}

/// <summary>Statuts de l'enveloppe : <c>done</c>, ou <c>partial</c> avec au moins un avertissement (gravité <c>warning</c>) dans <c>warnings</c>.</summary>
public static class GenericStatuses
{
    public const string Done = "done";
    public const string Partial = "partial";
}

/// <summary>Gravités d'un élément de <c>warnings[]</c>.</summary>
public static class GenericSeverities
{
    public const string Info = "info";
    public const string Warning = "warning";
}

/// <summary>
/// Élément de <c>warnings[]</c> : un texte (= avertissement) ou un objet <c>{ severity, message, code? }</c>. <see cref="Code"/> : lettres,
/// chiffres, <c>-</c> et <c>_</c>, 64 au plus, commence par une lettre ou un chiffre. Message : 400 caractères au plus (la plateforme refuse au-delà). Un avertissement sans code s'écrit comme un simple texte ; une information
/// (<see cref="GenericSeverities.Info"/>) ne rend jamais l'enveloppe <c>partial</c> (un seul message info par code, 20 au plus).
/// </summary>
[JsonConverter(typeof(GenericWarningConverter))]
public sealed record GenericWarning(string Severity, string Message, string? Code = null)
{
    /// <summary>Longueur maximale d'un message (limite de la validation de la plateforme).</summary>
    public const int MaxMessageLength = 400;

    /// <summary>Nombre maximal d'éléments de <c>warnings[]</c> (limite de la validation de la plateforme).</summary>
    public const int MaxItems = 20;

    public static GenericWarning Warning(string message) => new(GenericSeverities.Warning, message);

    public static GenericWarning Info(string code, string message) => new(GenericSeverities.Info, message, code);
}

/// <summary>Écrit un avertissement sans code comme un texte, tout autre cas comme un objet.</summary>
public sealed class GenericWarningConverter : JsonConverter<GenericWarning>
{
    public override GenericWarning Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new NotSupportedException("Lecture d'une enveloppe non prise en charge.");

    public override void Write(System.Text.Json.Utf8JsonWriter writer, GenericWarning value, JsonSerializerOptions options)
    {
        if (value.Severity == GenericSeverities.Warning && value.Code is null)
        {
            writer.WriteStringValue(value.Message);
            return;
        }

        writer.WriteStartObject();
        writer.WriteString("severity", value.Severity);
        writer.WriteString("message", value.Message);
        if (value.Code is not null)
        {
            writer.WriteString("code", value.Code);
        }

        writer.WriteEndObject();
    }
}

/// <summary>Types de propriété de l'enveloppe commune.</summary>
public static class GenericPropertyTypes
{
    public const string Text = "text";
    public const string Number = "number";
    public const string Boolean = "boolean";
    public const string Date = "date";
    public const string Box = "box";
    public const string Ref = "ref";
    public const string File = "file";
}

/// <summary>
/// Enveloppe commune des résultats de la plateforme (docs/frontend-contract.md de system-filter-backend, « Enveloppe commune des
/// résultats » et « Ajouts RoomTracker »). Un seul modèle générique : un arbre d'objets et leurs propriétés, sans type de contenu.
/// </summary>
public sealed record GenericEnvelope
{
    [JsonPropertyName("contractVersion")]
    public string ContractVersion { get; init; } = GenericContract.Version;

    [JsonPropertyName("serviceId")]
    public string ServiceId { get; init; } = GenericContract.ServiceId;

    /// <summary><see cref="GenericStatuses.Done"/> ou <see cref="GenericStatuses.Partial"/>.</summary>
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    /// <summary>Avertissements et informations (voir <see cref="GenericWarning"/>) ; <c>partial</c> exige au moins un avertissement.</summary>
    [JsonPropertyName("warnings")]
    public IReadOnlyList<GenericWarning> Warnings { get; init; } = [];

    [JsonPropertyName("models")]
    public IReadOnlyList<GenericModel> Models { get; init; } = [];

    [JsonPropertyName("objects")]
    public IReadOnlyList<GenericObject> Objects { get; init; } = [];
}

/// <summary>
/// Maquette du viewer : <see cref="Key"/> citée par la propriété <c>modelKey</c> des objets. <see cref="Urn"/> est connu de la
/// plateforme (conversion), pas de l'extracteur : il reste vide dans le fichier et la plateforme le renseigne.
/// </summary>
public sealed record GenericModel(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("urn")] string Urn);

/// <summary>Objet de l'arbre : <see cref="Id"/> unique, enfants et propriétés.</summary>
public sealed record GenericObject
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("children")]
    public IReadOnlyList<GenericObject> Children { get; init; } = [];

    [JsonPropertyName("properties")]
    public IReadOnlyList<GenericProperty> Properties { get; init; } = [];
}

/// <summary>
/// Propriété d'un objet. <see cref="Value"/> selon <see cref="Type"/> : texte, nombre (avec <see cref="Unit"/>), booléen, date ISO 8601,
/// <see cref="GenericBox"/>, identifiant d'un autre objet (<c>ref</c>) ou <see cref="GenericFile"/>. Une clé est unique par objet,
/// sauf pour <c>ref</c> qui peut se répéter.
/// </summary>
public sealed record GenericProperty
{
    [JsonPropertyName("key")]
    public required string Key { get; init; }

    [JsonPropertyName("label")]
    public required string Label { get; init; }

    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("value")]
    public required object Value { get; init; }

    /// <summary>Unité, pour un nombre seulement (<c>m</c>, <c>m²</c>, <c>m³</c>).</summary>
    [JsonPropertyName("unit")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Unit { get; init; }

    /// <summary>Libellé de groupe pour l'affichage (texte non vide, 120 caractères au plus).</summary>
    [JsonPropertyName("group")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Group { get; init; }
}

/// <summary>Boîte <c>{min:[x,y,z], max:[x,y,z]}</c> en millimètres, dans le repère de la maquette principale.</summary>
public sealed record GenericBox(
    [property: JsonPropertyName("min")] IReadOnlyList<double> Min,
    [property: JsonPropertyName("max")] IReadOnlyList<double> Max);

/// <summary>Fichier : nom, type MIME <c>type/sous-type</c> et lien (<c>https://…</c> ou chemin relatif commençant par <c>/</c>).</summary>
public sealed record GenericFile(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("mimeType")] string MimeType,
    [property: JsonPropertyName("url")] string Url);
