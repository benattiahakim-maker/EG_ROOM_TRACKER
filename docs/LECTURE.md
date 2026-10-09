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

Finitions et paramètres partagés (EGR-10) : vides en attendant.

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
posée par le contrôle qualité (EGR-11) à partir des `volumeStatus`. Mode `compute` (EGR-16) : refusé en
`CONFIG_INVALID` dès la validation de configuration (contrôle défensif conservé dans `VolumeCalculationService`). Les tolérances d'écart (D5) se vérifient sur le
jeu de référence (EGR-18).

## Limite connue

Revit peut ne pas distinguer une chaîne vide saisie d'un paramètre jamais renseigné (`HasValue` faux) : dans ce cas
la valeur exportée est `null`. À confirmer sur le jeu de référence (EGR-18).
