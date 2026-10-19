using EgRoomTracker.Contracts.Configuration;
using EgRoomTracker.Contracts.Results;

namespace EgRoomTracker.Application.Revit;

/// <summary>
/// Service de collecte d'une famille d'éléments rattachés aux pièces (portes, équipements, terminaux, schéma 1.4).
/// Appelé par <see cref="RoomExtractionService"/> pour chaque <see cref="ElementKind"/> demandé par <c>settings.json</c>.
/// Les éléments des documents liés ne sont pas parcourus.
/// </summary>
public interface IRoomElementService
{
    ElementKind Kind { get; }

    /// <summary>Collecte les éléments du document principal et la ou les pièces qui leur correspondent.</summary>
    /// <param name="includeAllParameters">Joint tous les paramètres de chaque élément (<c>roomParameters: all</c>).</param>
    /// <param name="categories">Noms de <c>BuiltInCategory</c> remplaçant les catégories par défaut du service ; null = défaut.</param>
    /// <param name="includeLinks">Parcourt aussi les modèles liés (équipements et terminaux) ; sans effet pour les portes.</param>
    ElementCollection Collect(bool includeAllParameters, IReadOnlyList<string>? categories = null, bool includeLinks = true);
}

/// <summary>Pièce (ElementId) liée à un élément, avec la relation (<see cref="ElementRoomRelation"/>).</summary>
public sealed record RoomLink(long RoomId, string Relation);

/// <summary>Élément collecté et ses pièces ; aucune pièce : l'élément est signalé ELEMENT_WITHOUT_ROOM.</summary>
public sealed record ElementPlacement(long ElementId, RoomElementDto Element, IReadOnlyList<RoomLink> Links);

/// <summary>
/// Éléments collectés, triés par ElementId ; <see cref="Unreadable"/> : éléments dont la lecture a échoué ;
/// <see cref="Categories"/> : catégories effectivement parcourues ; <see cref="UnknownCategories"/> : noms demandés
/// qui ne sont pas des catégories Revit (ignorés) ; <see cref="Sources"/> : titres des modèles parcourus (principal, puis liens).
/// </summary>
public sealed record ElementCollection(
    IReadOnlyList<ElementPlacement> Placements,
    int Unreadable,
    IReadOnlyList<string>? Categories = null,
    IReadOnlyList<string>? UnknownCategories = null,
    IReadOnlyList<string>? Sources = null);
