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
`RoomDto.IsPlaced` (interne, non sérialisé) transmet le placement au contrôle qualité (`ZERO_AREA` pour une pièce
placée, EGR-11). `DUPLICATE_NUMBER`, `ZERO_AREA` et `VOLUME_NOT_COMPUTED` relèvent d'EGR-11 ; une exception sur une
pièce est convertie en `EXTRACTION_ERROR` par l'orchestration.

## Champs complétés par d'autres cartes

Quantités et volume (EGR-09) : `null` en attendant ; `volumeStatus` provisoire `notComputed` si le calcul est
désactivé, sinon `notAvailable`. Finitions et paramètres partagés (EGR-10) : vides en attendant.

## Limite connue

Revit peut ne pas distinguer une chaîne vide saisie d'un paramètre jamais renseigné (`HasValue` faux) : dans ce cas
la valeur exportée est `null`. À confirmer sur le jeu de référence (EGR-18).
