using System.Text.Json.Serialization;

namespace EgRoomTracker.Contracts.Results;

/// <summary>
/// Pièce exportée (section 3). Quantités en unités SI, déjà converties.
/// Les champs absents ou illisibles sont à null, jamais à zéro par défaut.
/// </summary>
public sealed record RoomDto
{
    /// <summary><c>ElementId.Value</c> sérialisé en chaîne.</summary>
    public required string ElementId { get; init; }

    public required string UniqueId { get; init; }

    public string? Name { get; init; }

    public string? Number { get; init; }

    public double? AreaM2 { get; init; }

    public double? UnboundedHeightM { get; init; }

    public LevelRef? Level { get; init; }

    public string? Occupancy { get; init; }

    /// <summary>Null sauf si <see cref="VolumeStatus"/> = <see cref="Results.VolumeStatus.Computed"/>.</summary>
    public double? VolumeM3 { get; init; }

    public required VolumeStatus VolumeStatus { get; init; }

    public RoomFinishes Finishes { get; init; } = RoomFinishes.Empty;

    public ElementRef? Phase { get; init; }

    public ElementRef? DesignOption { get; init; }

    /// <summary>
    /// Identifiant du jeu de variantes (option set) de <see cref="DesignOption"/>, lu côté Revit
    /// (<c>OPTION_SET_ID</c> de la variante) ; null hors variante ou si illisible. Donnée interne pour
    /// DUPLICATE_NUMBER (deux variantes d'un même jeu s'excluent) ; non sérialisée, absente de <c>result.json</c>.
    /// </summary>
    [JsonIgnore]
    public string? DesignOptionSetId { get; init; }

    /// <summary>Une entrée par clé de mapping de <c>settings.json</c>.</summary>
    public IReadOnlyDictionary<string, SharedParameterValue> SharedParameters { get; init; } =
        new Dictionary<string, SharedParameterValue>();

    /// <summary>Codes d'anomalie de portée pièce (<see cref="Codes.QualityFlagCodes"/>).</summary>
    public IReadOnlyList<string> QualityFlags { get; init; } = [];
}

public sealed record LevelRef(string ElementId, string UniqueId, string Name);

/// <summary>Référence de phase ou de variante.</summary>
public sealed record ElementRef(string ElementId, string Name);

/// <summary>Finitions textuelles renseignées sur la pièce, sans inférence.</summary>
public sealed record RoomFinishes(string? Floor, string? Wall, string? Ceiling, string? Base)
{
    public static RoomFinishes Empty { get; } = new(null, null, null, null);
}

/// <summary>Valeur d'un paramètre partagé : chaîne, nombre ou null.</summary>
public sealed record SharedParameterValue(Guid Guid, object? Value);

public enum VolumeStatus
{
    /// <summary>Valeur lue.</summary>
    Computed,

    /// <summary>Calcul des volumes désactivé dans le document.</summary>
    NotComputed,

    /// <summary>Lecture impossible.</summary>
    NotAvailable,
}
