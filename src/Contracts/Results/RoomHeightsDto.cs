using System.Text.Json.Serialization;

namespace EgRoomTracker.Contracts.Results;

/// <summary>Source de la hauteur effective d'une pièce.</summary>
public static class HeightSources
{
    /// <summary>Hauteur minimale mesurée par lancer de rayons sous un plafond, un sol, un toit ou une ossature.</summary>
    public const string Measured = "measured";

    /// <summary>Aucun obstacle au-dessus : hauteur renseignée dans Revit (<c>ROOM_HEIGHT</c>).</summary>
    public const string Declared = "declared";
}

/// <summary>
/// Hauteurs d'une pièce (schéma 1.4, <c>roomHeights</c>) : la hauteur <b>renseignée</b> dans Revit et la hauteur minimale
/// <b>mesurée</b> séparées, plus la hauteur effective (mesurée si un obstacle existe, sinon renseignée, comme TRC).
/// Mètres, sans arrondi. La hauteur non limitée reste dans <c>unboundedHeightM</c>.
/// </summary>
public sealed record RoomHeightsDto(
    [property: JsonPropertyName("renseignee_m")] double? DeclaredM,
    [property: JsonPropertyName("mesuree_min_m")] double? MeasuredMinM,
    [property: JsonPropertyName("effective_m")] double? EffectiveM,
    [property: JsonPropertyName("source_effective")] string? EffectiveSource,
    [property: JsonPropertyName("rayons")] int Samples)
{
    /// <summary>Catégorie de l'objet qui limite la hauteur minimale (ex. Plafonds) ; absente sans mesure.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("categorie_limitante")]
    public string? LimitingCategory { get; init; }

    /// <summary>Titre du document de l'objet limitant : le modèle principal ou le nom du lien ; absent sans mesure.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("modele_limitant")]
    public string? LimitingDocument { get; init; }

    /// <summary>Hauteur minimale mesurée par catégorie d'obstacle (m), triée par nom ; absente sans mesure.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("par_categorie_m")]
    public IReadOnlyDictionary<string, double>? ByCategoryM { get; init; }
}

/// <summary>Bilan de la mesure des hauteurs : pièces visées, mesurées, sans obstacle (hauteur renseignée retenue), en échec.</summary>
public sealed record HeightsSummary(int Requested, int Measured, int WithoutObstacle, int Failed);
