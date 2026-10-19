# Hauteurs des pièces : renseignée et mesurée (schéma 1.4)

Implémentation : `src/Revit/RoomHeightService.cs` (lancer de rayons), `src/Application/Revit/IRoomHeightService.cs`
(`HeightHitAccumulator`, cumul des impacts), orchestration dans `src/Application/RoomExtractionService.cs`. Tests :
`src/Tests/Application/RoomHeightsTests.cs`, sans Revit. **Non encore validé sur un vrai Revit** (`ReferenceIntersector` dans DA).

La méthode reprend `GetRoomMinHeightByRaytracing` de l'application TRC (`EG_Automation_TRC`).

## Réglages

| Clé | Défaut | Effet |
| --- | --- | --- |
| `roomHeights` | `false` | `true` : chaque pièce placée reçoit `heights` |
| `heightStepM` | `0.5` | pas de la grille de rayons en mètres, de 0,1 à 2 (coût proportionnel à la surface divisée par le pas au carré) |

Sans `roomHeights`, rien ne change dans `result.json`.

## Contenu de `rooms[].hauteurs`

| Champ | Contenu |
| --- | --- |
| `renseignee_m` | hauteur **renseignée** : paramètre `ROOM_HEIGHT` de la pièce (« Hauteur »), en m ; null si absente |
| `mesuree_min_m` | hauteur minimale **mesurée** par rayons sous un obstacle, en m ; null s'il n'y a aucun obstacle au-dessus |
| `effective_m` | `mesuree_min_m` s'il existe, sinon `renseignee_m` (règle de TRC) |
| `source_effective` | `measured`, `declared` ou null |
| `rayons` | nombre de rayons lancés dans la pièce |
| `categorie_limitante` | catégorie de l'objet qui limite (ex. `Ceilings`) ; absent sans mesure |
| `modele_limitant` | titre du modèle principal ou du lien d'où vient l'objet limitant ; absent sans mesure |
| `par_categorie_m` | hauteur minimale par catégorie d'obstacle, triée par nom ; absent sans mesure |

La hauteur non limitée (`UnboundedHeight`) reste dans `rooms[].hauteur_non_limitee_m`. Mètres, sans arrondi.

**Jamais de zéro :** une hauteur absente est `null`, pas `0`. Sans impact de rayon, `mesuree_min_m` est `null` (et `categorie_limitante`,
`modele_limitant`, `par_categorie_m` sont absents) ; sans hauteur renseignée exploitable, `renseignee_m` est `null` ; `effective_m` et
`source_effective` sont `null` si les deux manquent. Une valeur nulle ou négative n'est jamais exportée comme hauteur. Même règle pour
la largeur et la hauteur des portes de repli (paramètres).

## Méthode

1. Vue 3D : la vue d'export IFC du modèle (nom contenant « EXP » et « IFC ») si elle existe, sinon une vue 3D temporaire créée
   **une seule fois** pour tout le document (TRC en crée une par pièce), tous les jeux de travail visibles. Rien n'est sauvegardé.
2. Rayons : vers le haut (+Z), depuis 0,1 pied au-dessus du bas de la pièce, sur une grille de pas `heightStepM` dans la boîte
   englobante, aux seuls points situés dans la pièce (`IsPointInRoom`). Si aucun point de la grille ne tombe dans une petite
   pièce, le point d'insertion de la pièce est essayé.
3. Obstacles : Plafonds, Sols, Toits et Ossature, du **modèle principal et des liens** (`FindReferencesInRevitLinks`), avec
   la transformation du lien appliquée par Revit.
4. Cumul : les impacts à moins de 0,1 m du point de départ sont ignorés ; la hauteur minimale est la plus courte distance restante.

## Anomalies

`HEIGHT_MEASUREMENT_FAILED` (document, warning) : au moins une pièce non mesurée (détails `requested`, `failed`, `reason`). La
pièce garde tous les champs du contrat, sans `heights`. `summary.heights` : `requested`, `measured`, `withoutObstacle`, `failed`.
