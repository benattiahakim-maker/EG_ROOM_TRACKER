using System.Text.Json.Serialization;

namespace EgRoomTracker.Contracts.Results;

/// <summary>
/// Pièce exportée (section 3). Quantités en unités SI, déjà converties.
/// Les champs absents ou illisibles sont à null, jamais à zéro par défaut.
/// </summary>
public sealed record RoomDto
{
    /// <summary><c>ElementId.Value</c> sérialisé en chaîne.</summary>
    [JsonPropertyName("id_revit")]
    public required string ElementId { get; init; }

    [JsonPropertyName("unique_id")]
    public required string UniqueId { get; init; }

    [JsonPropertyName("nom")]
    public string? Name { get; init; }

    [JsonPropertyName("numero")]
    public string? Number { get; init; }

    [JsonPropertyName("surface_m2")]
    public double? AreaM2 { get; init; }

    [JsonPropertyName("hauteur_non_limitee_m")]
    public double? UnboundedHeightM { get; init; }

    [JsonPropertyName("niveau")]
    public LevelRef? Level { get; init; }

    [JsonPropertyName("occupation")]
    public string? Occupancy { get; init; }

    /// <summary>Null sauf si <see cref="VolumeStatus"/> = <see cref="Results.VolumeStatus.Computed"/>.</summary>
    [JsonPropertyName("volume_m3")]
    public double? VolumeM3 { get; init; }

    [JsonPropertyName("statut_volume")]
    public required VolumeStatus VolumeStatus { get; init; }

    [JsonPropertyName("finitions")]
    public RoomFinishes Finishes { get; init; } = RoomFinishes.Empty;

    [JsonPropertyName("phase")]
    public ElementRef? Phase { get; init; }

    [JsonPropertyName("variante")]
    public ElementRef? DesignOption { get; init; }

    /// <summary>
    /// Identifiant du jeu de variantes (option set) de <see cref="DesignOption"/>, lu côté Revit
    /// (<c>OPTION_SET_ID</c> de la variante) ; null hors variante ou si illisible. Donnée interne pour
    /// DUPLICATE_NUMBER (deux variantes d'un même jeu s'excluent) ; non sérialisée, absente de <c>result.json</c>.
    /// </summary>
    [JsonIgnore]
    public string? DesignOptionSetId { get; init; }

    /// <summary>Une entrée par clé de mapping de <c>settings.json</c>.</summary>
    [JsonPropertyName("parametres_partages")]
    public IReadOnlyDictionary<string, SharedParameterValue> SharedParameters { get; init; } =
        new Dictionary<string, SharedParameterValue>();

    /// <summary>Codes d'anomalie de portée pièce (<see cref="Codes.QualityFlagCodes"/>).</summary>
    [JsonPropertyName("indicateurs_qualite")]
    public IReadOnlyList<string> QualityFlags { get; init; } = [];

    /// <summary>
    /// Paramètres nommés lus pour l'identification TRC (<c>SGP-DI-Ouvrage</c>, <c>SGP-DI-Typologie_Espace</c>,
    /// <c>SGP-DI-Local_Code</c>) quand le format de sortie n'est pas <c>contract</c> ; donnée interne, non sérialisée.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyDictionary<string, string?>? NamedParameters { get; init; }

    /// <summary>
    /// Tous les paramètres de la pièce, <b>à plat</b> <c>{ nom : valeur }</c> comme <c>areaM2</c> ou <c>volumeM3</c> (schéma 1.4) ; absent
    /// sauf <c>roomParameters: all</c>. Valeur : texte, entier, ElementId en chaîne, ou nombre converti en SI (longueur en m, surface en
    /// m², volume en m³, nombre sans unité) ; pour une autre grandeur (angle, pourcentage…), la valeur affichée par Revit. Triés par nom ;
    /// un nom en double est suffixé « (2) », « (3) ».
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("parametres")]
    public IReadOnlyDictionary<string, object?>? Parameters { get; init; }

    /// <summary>Hauteurs renseignée, mesurée et effective (schéma 1.4) ; absent sauf <c>roomHeights</c>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("hauteurs")]
    public RoomHeightsDto? Heights { get; init; }

    /// <summary>Portes de la pièce (schéma 1.4) ; absent sauf <c>elements</c> contenant <c>doors</c>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("portes")]
    public IReadOnlyList<RoomElementDto>? Doors { get; init; }

    /// <summary>Équipements de la pièce (schéma 1.4) ; absent sauf <c>elements</c> contenant <c>equipment</c>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("equipements")]
    public IReadOnlyList<RoomElementDto>? Equipment { get; init; }

    /// <summary>Terminaux de la pièce (schéma 1.4) ; absent sauf <c>elements</c> contenant <c>terminals</c>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("terminaux")]
    public IReadOnlyList<RoomElementDto>? Terminals { get; init; }
}

public sealed record LevelRef(
    [property: JsonPropertyName("id_revit")] string ElementId,
    [property: JsonPropertyName("unique_id")] string UniqueId,
    [property: JsonPropertyName("nom")] string Name);

/// <summary>Référence de phase ou de variante.</summary>
public sealed record ElementRef(
    [property: JsonPropertyName("id_revit")] string ElementId,
    [property: JsonPropertyName("nom")] string Name);

/// <summary>Finitions textuelles renseignées sur la pièce, sans inférence.</summary>
public sealed record RoomFinishes(
    [property: JsonPropertyName("sol")] string? Floor,
    [property: JsonPropertyName("murs")] string? Wall,
    [property: JsonPropertyName("plafond")] string? Ceiling,
    [property: JsonPropertyName("plinthe")] string? Base)
{
    public static RoomFinishes Empty { get; } = new(null, null, null, null);
}

/// <summary>Valeur d'un paramètre partagé : chaîne, nombre ou null.</summary>
public sealed record SharedParameterValue(
    [property: JsonPropertyName("guid")] Guid Guid,
    [property: JsonPropertyName("valeur")] object? Value);

public enum VolumeStatus
{
    /// <summary>Valeur lue.</summary>
    Computed,

    /// <summary>Calcul des volumes désactivé dans le document.</summary>
    NotComputed,

    /// <summary>Lecture impossible.</summary>
    NotAvailable,
}
