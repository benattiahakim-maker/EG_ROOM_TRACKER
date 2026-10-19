namespace EgRoomTracker.Application.Revit;

/// <summary>
/// Hauteur d'une pièce par lancer de rayons (méthode de l'application TRC) : rayons verticaux sur une grille de la
/// pièce, obstacles Plafonds, Sols, Toits et Ossature du modèle et des liens. Appelé par
/// <see cref="RoomExtractionService"/> pour chaque pièce placée quand <c>roomHeights</c> est actif, document ouvert.
/// </summary>
public interface IRoomHeightService
{
    /// <param name="roomElementId">ElementId de la pièce.</param>
    /// <param name="stepM">Pas de la grille de rayons en mètres.</param>
    RoomHeightMeasurement Measure(long roomElementId, double stepM);
}

/// <summary>
/// Mesure d'une pièce : hauteur renseignée (<c>ROOM_HEIGHT</c>), hauteur minimale mesurée (null sans obstacle), objet limitant
/// et minimum par catégorie, nombre de rayons lancés.
/// </summary>
public sealed record RoomHeightMeasurement(
    double? DeclaredM,
    double? MeasuredMinM,
    string? LimitingCategory,
    string? LimitingDocument,
    IReadOnlyDictionary<string, double>? ByCategoryM,
    int Samples);

/// <summary>
/// Cumul des impacts de rayons d'une pièce, comme <c>GetRoomMinHeightByRaytracing</c> : les impacts à moins de
/// <see cref="MinHitDistanceM"/> du point de départ sont ignorés (sol de la pièce), la hauteur minimale est la plus courte
/// distance restante, avec sa catégorie et son document, et le minimum de chaque catégorie est conservé.
/// </summary>
public sealed class HeightHitAccumulator
{
    /// <summary>Distance en dessous de laquelle un impact est ignoré (m).</summary>
    public const double MinHitDistanceM = 0.1;

    private readonly Dictionary<string, double> _byCategory = new(StringComparer.Ordinal);

    public double? MinM { get; private set; }

    public string? LimitingCategory { get; private set; }

    public string? LimitingDocument { get; private set; }

    /// <summary>Minimum par catégorie, trié par nom (sérialisation déterministe) ; null sans impact.</summary>
    public IReadOnlyDictionary<string, double>? ByCategoryM =>
        _byCategory.Count == 0 ? null : _byCategory.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    /// <summary>Ajoute un impact ; retourne faux s'il est ignoré (trop proche ou non fini).</summary>
    public bool Add(string category, string document, double distanceM)
    {
        if (!double.IsFinite(distanceM) || distanceM < MinHitDistanceM)
        {
            return false;
        }

        if (!_byCategory.TryGetValue(category, out var current) || distanceM < current)
        {
            _byCategory[category] = distanceM;
        }

        if (MinM is null || distanceM < MinM)
        {
            (MinM, LimitingCategory, LimitingDocument) = (distanceM, category, document);
        }

        return true;
    }
}
