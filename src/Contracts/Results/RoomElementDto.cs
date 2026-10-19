using System.Text.Json.Serialization;

namespace EgRoomTracker.Contracts.Results;

/// <summary>Relation d'un élément avec la pièce qui le porte.</summary>
public static class ElementRoomRelation
{
    /// <summary>Porte : la pièce est du côté « depuis ».</summary>
    public const string From = "from";

    /// <summary>Porte : la pièce est du côté « vers ».</summary>
    public const string To = "to";

    /// <summary>Équipement ou terminal situé dans la pièce.</summary>
    public const string In = "in";
}

/// <summary>
/// Élément (porte, équipement, terminal) associé à une pièce (schéma 1.4). Une porte entre deux pièces figure dans
/// chacune des deux, avec la relation <c>from</c> ou <c>to</c>.
/// </summary>
public sealed record RoomElementDto
{
    /// <summary><c>ElementId.Value</c> de l'instance, sérialisé en chaîne.</summary>
    [JsonPropertyName("id_revit")]
    public required string ElementId { get; init; }

    [JsonPropertyName("unique_id")]
    public required string UniqueId { get; init; }

    [JsonPropertyName("categorie")]
    public required string Category { get; init; }

    [JsonPropertyName("famille")]
    public string? Family { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("repere")]
    public string? Mark { get; init; }

    /// <summary>Numéro de la porte (<c>DOOR_NUMBER</c>, ex. 1.02) ; absent pour les autres éléments.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("numero")]
    public string? Number { get; init; }

    /// <summary>Nom du niveau de l'instance.</summary>
    [JsonPropertyName("niveau")]
    public string? Level { get; init; }

    [JsonPropertyName("relation")]
    public string Relation { get; init; } = ElementRoomRelation.In;

    /// <summary>
    /// Discipline de l'élément selon sa catégorie Revit : <c>cvc</c>, <c>plomberie</c>, <c>electricite</c> ou <c>autres</c>
    /// (équipements et terminaux) ; absente pour les portes. Sert à ranger les éléments par section.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("discipline")]
    public string? Discipline { get; init; }

    /// <summary>
    /// Titre du modèle lié d'où vient l'élément (<c>elementId</c> et <c>uniqueId</c> sont alors ceux du modèle lié) ;
    /// absent pour un élément du modèle principal.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("nom_du_lien")]
    public string? LinkName { get; init; }

    /// <summary>Largeur de passage libre en mètres (portes), mesurée sur la géométrie ; absente pour les autres éléments.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("largeur_m")]
    public double? WidthM { get; init; }

    /// <summary>Hauteur de passage libre en mètres (portes), mesurée sur la géométrie ; absente pour les autres éléments.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("hauteur_m")]
    public double? HeightM { get; init; }

    /// <summary>
    /// Origine de <see cref="WidthM"/> et <see cref="HeightM"/> : <c>geometry</c> (mesurées), <c>parameter</c> (repli sur les
    /// paramètres du type quand la géométrie est inexploitable) ou <c>mixed</c> ; absente sans dimensions.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("source_dimensions")]
    public string? DimensionSource { get; init; }

    /// <summary>Tous les paramètres de l'instance à plat <c>{ nom : valeur }</c>, seulement avec <c>roomParameters: all</c> (voir <see cref="RoomDto.Parameters"/>).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("parametres")]
    public IReadOnlyDictionary<string, object?>? Parameters { get; init; }
}
