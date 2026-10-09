# Indicateurs qualité et statut (EGR-11)

Implémentation : `EgRoomTracker.Application.Quality.QualityCheckService`, sans appel Revit (sections 3, 9.1 et 9.3).

## Répartition des indicateurs

| Code | Posé par | Règle |
| --- | --- | --- |
| `UNPLACED`, `MISSING_PARAMETER`, `LEVEL_NOT_FOUND` | lecture (`RoomReadBuilder`) | Conservés tels quels |
| `EXTRACTION_ERROR` | orchestration (filet de sécurité par pièce) | Conservé ; pièce exportée avec son identité |
| `ZERO_AREA` | qualité | `areaM2 == 0` (pas `null`) et pas `UNPLACED` |
| `DUPLICATE_NUMBER` | qualité | Même `number` (comparaison exacte : `"001"` ≠ `"1"`) et même phase (`phase.elementId`) ; les pièces sans phase (`phase: null`) forment un même groupe entre elles ; numéros `""` ou `null` ignorés ; posé sur chaque pièce du groupe, détails `number` et `elementIds` (tri numérique) |
| `VOLUME_NOT_COMPUTED` | qualité | Calcul des volumes désactivé (`VolumeComputationState.VolumesComputed` faux, sans échec du mode compute) : une anomalie document info, détails `volumeMode` et `roomsAffected` (pièces en `notComputed`) |
| `VOLUME_COMPUTE_FAILED` | qualité | Mode `compute`, activation temporaire impossible (`VolumeComputationState.ComputeFailure`) : une anomalie document warning (au lieu de `VOLUME_NOT_COMPUTED`), détails `volumeMode`, `reason` et `roomsAffected` (pièces en `notAvailable`) ; statut `succeededWithWarnings` (EGR-16) |

`qualityFlags` : sans doublon, indicateurs de lecture d'abord puis `ZERO_AREA` et `DUPLICATE_NUMBER`. Chaque indicateur
de pièce a son anomalie détaillée (même `elementId`). Aucune pièce n'est supprimée, fusionnée ni réordonnée (l'orchestration
retrie ensuite par `elementId` numérique).

## Ordre des anomalies

Anomalies document (dans l'ordre reçu, puis `VOLUME_NOT_COMPUTED` ou `VOLUME_COMPUTE_FAILED`), puis anomalies de pièce dans l'ordre des pièces :
pour chaque pièce, celles de la lecture puis celles du contrôle qualité.

## Statut (9.3)

`succeededWithWarnings` dès qu'une anomalie est de sévérité warning ou error, sinon `succeeded` (une anomalie info comme
`VOLUME_NOT_COMPUTED`, `LINK_NOT_LOADED` ou `UNSUPPORTED_PARAMETER_TYPE` n'empêche pas `succeeded`). Le service ne produit
jamais `failed` : les échecs globaux (9.2) relèvent de l'orchestration. Une pièce `EXTRACTION_ERROR` force aussi
`succeededWithWarnings` côté orchestration (D2).
