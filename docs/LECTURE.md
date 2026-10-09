# Lecture des pièces (EGR-08 : identité, nom, numéro, niveau, occupation)

Implémentation : `EgRoomTracker.Revit.RoomParameterReader` ; règles indépendantes de Revit :
`EgRoomTracker.Application.Revit.RoomReadBuilder` et `ParameterReading`.

## Champs (spécifications, section 3)

| Champ | Source | Règle |
| --- | --- | --- |
| `elementId` | `ElementId.Value` | chaîne invariante, entier 64 bits préservé |
| `uniqueId` | `Element.UniqueId` | identité stable ; jamais le numéro comme clé |
| `name` | `ROOM_NAME` | nom seul, sans concaténation du numéro (pas `Element.Name`) |
| `number` | `ROOM_NUMBER` | chaîne, zéros initiaux conservés |
| `level` | `Room.LevelId` → `Level` | `elementId`, `uniqueId`, `name` ; null + `LEVEL_NOT_FOUND` si invalide ou introuvable |
| `occupancy` | `ROOM_OCCUPANCY` | texte d'usage, pas un nombre d'occupants |
| `phase` | `ROOM_PHASE_ID` → `Phase` | `elementId`, `name` ; null si absente |
| `designOption` | `Element.DesignOption` | `elementId`, `name` ; null hors variante |

Paramètres natifs lus par `BuiltInParameter`, jamais par libellé français/anglais.

## Représentation des valeurs

- Paramètre présent avec valeur : valeur telle quelle (chaîne vide `""` conservée).
- Paramètre présent sans valeur : `null`.
- Paramètre absent : `null` et indicateur `MISSING_PARAMETER`, une anomalie par paramètre (`details.parameter`) ;
  l'indicateur n'apparaît qu'une fois dans `qualityFlags`.

## Indicateurs posés à la lecture

`UNPLACED` (`Location` nulle, `details.locationIsNull`), `MISSING_PARAMETER`, `LEVEL_NOT_FOUND`.
Le placement n'est transmis au contrôle qualité que par l'indicateur `UNPLACED` (aucun champ interne) :
`ZERO_AREA` = `areaM2 == 0` et pas `UNPLACED` (EGR-11). `DUPLICATE_NUMBER`, `ZERO_AREA` et `VOLUME_NOT_COMPUTED` relèvent d'EGR-11 ; une exception sur une
pièce est convertie en `EXTRACTION_ERROR` par l'orchestration.

## Champs complétés par d'autres cartes

## Finitions et paramètres partagés (EGR-10)

Finitions : textes saisis sur la pièce, lus par paramètre natif, aucune déduction depuis murs, sols ou plafonds,
aucun libellé localisé (résultat identique en Revit français et anglais).

| Champ | Paramètre natif |
| --- | --- |
| `finishes.floor` | `ROOM_FINISH_FLOOR` |
| `finishes.wall` | `ROOM_FINISH_WALL` |
| `finishes.ceiling` | `ROOM_FINISH_CEILING` |
| `finishes.base` | `ROOM_FINISH_BASE` |

Même représentation que les autres textes : valeur telle quelle, `""` conservée, `null` sans valeur, `null` +
`MISSING_PARAMETER` (détail `parameter`) si absent.

Paramètres partagés (`settings.sharedParameters`) : une entrée par clé de mapping, dans l'ordre de la configuration,
`{ guid, value }` (le GUID est la provenance). Lecture par `Element.get_Parameter(Guid)`, jamais par nom.

| Situation | `value` | Anomalie |
| --- | --- | --- |
| GUID inconnu du document (`SharedParameterElement.Lookup` nul) | `null` | document `SHARED_PARAMETER_NOT_FOUND` (une par GUID, détails `key`, `guid`) ; pas de `MISSING_PARAMETER` par pièce |
| GUID connu, paramètre absent de la pièce | `null` | `MISSING_PARAMETER` (détails `parameter` = clé, `guid`) |
| Présent sans valeur (`HasValue` faux) | `null` | — |
| Texte | chaîne telle quelle (`""` conservée) | — |
| Entier (dont Oui/Non : 0/1) | nombre | — |
| Double longueur / surface / volume (`SpecTypeId.Length/Area/Volume`) | nombre en m / m² / m³ (UnitUtils, sans arrondi) | — |
| Double nombre sans unité (`SpecTypeId.Number`) | nombre tel quel | — |
| Double d'une autre spec mesurable (angle, pourcentage, monnaie…) ou spec inconnue | `null` (jamais la valeur interne) | document `UNSUPPORTED_PARAMETER_TYPE` (info, une par GUID, détails `key`, `guid`, `typeId`) |
| ElementId | chaîne `ElementId.Value` ; `null` si `-1` | — |
| Double NaN/infini | `null` | — |

Une pièce en `EXTRACTION_ERROR` garde une entrée `null` par clé de mapping. Une exception de lecture d'un
paramètre partagé convertit la pièce en `EXTRACTION_ERROR` (filet de sécurité de l'orchestration).

## Quantités et volume (EGR-09)

| Champ | Source Revit | Conversion |
| --- | --- | --- |
| `areaM2` | `SpatialElement.Area` | `UnitUtils.ConvertFromInternalUnits(v, UnitTypeId.SquareMeters)` |
| `unboundedHeightM` | `Room.UnboundedHeight` | `UnitUtils.ConvertFromInternalUnits(v, UnitTypeId.Meters)` |
| `volumeM3` | `Room.Volume` | `UnitUtils.ConvertFromInternalUnits(v, UnitTypeId.CubicMeters)` |

Aucun `AsValueString`, aucun arrondi (D7) ; les nombres JSON sont des `double` sérialisés indépendamment de la culture.
Règles pures dans `RoomQuantities` (testées sous Linux) :

| Situation | `volumeM3` | `volumeStatus` |
| --- | --- | --- |
| `AreaVolumeSettings.ComputeVolumes` faux (lu une fois par `VolumeCalculationService.Prepare`) | `null` | `notComputed` (volume non lu) |
| Calcul actif, pièce placée, lecture finie | m³ | `computed` |
| Calcul actif, pièce non placée | `null` | `notAvailable` |
| Calcul actif, lecture en échec ou valeur NaN/infinie | `null` | `notAvailable` |

Surface et hauteur NaN/infinies : `null` (UnitUtils lève `ArgumentException` sur une valeur non finie ; filtrées
avant l'appel). Une pièce non placée garde la surface lue (0 attendu). L'anomalie document `VOLUME_NOT_COMPUTED` est
posée par le contrôle qualité (EGR-11) à partir des `volumeStatus`. Les tolérances d'écart (D5) se vérifient sur le
jeu de référence (EGR-18).

### Mode `compute` (EGR-16)

`VolumeCalculationService.Prepare` lit `AreaVolumeSettings.ComputeVolumes` :

| Réglage du document | Traitement | `volumeComputation` (`summary`) |
| --- | --- | --- |
| vrai | lecture directe de `Room.Volume`, sans transaction | `documentComputeVolumes: true`, `computeApplied: false`, `transactionRolledBack: null` |
| faux | transaction temporaire : `Start()` (statut `Started` exigé), `SetClearAfterRollback(true)`, `ComputeVolumes = true`, `Document.Regenerate()`, lecture de `Room.Volume` de toutes les pièces (`OST_Rooms`) **avant** `RollBack()`, puis `RollBack()` (statut `RolledBack` vérifié) | `documentComputeVolumes: false`, `computeApplied: true`, `transactionRolledBack: true` (ou `false` si l'annulation n'est pas confirmée : log `VOLUME_COMPUTATION` en Warning) |

Les volumes lus pendant la transaction sont mis en cache par ElementId (`VolumeComputationState.ComputedVolumes`) :
après l'annulation, `Room.Volume` reflète de nouveau le réglage d'origine et n'est plus lu. Règles :

| Situation (mode `compute`) | `volumeM3` | `volumeStatus` |
| --- | --- | --- |
| Volume lu (direct ou pendant la transaction), pièce placée, valeur finie | m³ | `computed` |
| Pièce non placée, lecture en échec, valeur NaN/infinie ou absente du cache | `null` | `notAvailable` |
| Activation impossible (transaction non démarrée, `InvalidOperationException`…) | `null` | `notAvailable` (+ `VOLUME_COMPUTE_FAILED`, warning) |

`RegenerationFailedException` (document corrompu, lecture illégale) : annulation puis erreur globale `DOCUMENT_CORRUPTED`
(étape `document`). Le RVT n'est jamais sauvegardé : la transaction est annulée et le document fermé sans sauvegarde.

## Limite connue

Revit peut ne pas distinguer une chaîne vide saisie d'un paramètre jamais renseigné (`HasValue` faux) : dans ce cas
la valeur exportée est `null`. À confirmer sur le jeu de référence (EGR-18).
