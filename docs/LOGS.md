# Logs d'exécution

Implémentation : `EgRoomTracker.Logging.JsonConsoleLogService` (EGR-04, spécifications section 5).

## Format

Une ligne JSON par événement sur la **sortie standard** (console), récupérée dans le rapport APS du WorkItem.
Aucun fichier de log n'est produit et les logs ne font pas partie de `result.json`.

| Champ | Présence | Contenu |
| --- | --- | --- |
| `timestampUtc` | toujours | ISO 8601 UTC, millisecondes (`2026-10-09T12:00:03.250Z`) |
| `level` | toujours | `Info`, `Warning`, `Error` |
| `eventCode` | toujours | code d'événement (`EXTRACTION_STARTED`, …, voir `LogEventCodes`) |
| `correlationId` | toujours | identifiant de corrélation de l'exécution, identique à `summary.correlationId` |
| `stage` | toujours | identifiant d'étape (voir ci-dessous) |
| `message` | toujours | texte lisible, secrets masqués |
| `documentPath` | si pertinent | chemin relatif au répertoire d'entrée |
| `roomElementId` | si pertinent | ElementId de la pièce |
| `durationMs` | si pertinent | durée en millisecondes |
| `data` | si pertinent | données complémentaires (compteurs, `errorCode`, …) |
| `exception` | si présente | `type`, `message`, `stackTrace`, `inner` (5 niveaux max) |

Identifiants d'étape (`Stages`, également utilisés dans `summary.errors[].stage` de `result.json`) :

| `stage` | Étape |
| --- | --- |
| `startup` | démarrage, adaptation du contexte APS |
| `configuration` | lecture et validation de `settings.json` |
| `input` | entrée : ZIP décompressé, résolution du RVT principal |
| `document` | accès au document Revit |
| `collection` | collecte et lecture des pièces |
| `quality` | indicateurs qualité et anomalies |
| `export` | écriture de `result.json` |
| `completion` | bilan final |
| `logging` | événements propres au journal (`LOG_EVENTS_SUPPRESSED`) |

Les lignes sont en ASCII pur : les caractères non ASCII (accents) sont échappés en `\uXXXX` et restitués par tout
lecteur JSON.

Exemple :

```json
{"timestampUtc":"2026-10-09T12:00:41.120Z","level":"Error","eventCode":"EXTRACTION_FAILED","correlationId":"wi-7f3c2a9e5b1d4c08","stage":"configuration","message":"CONFIG_INVALID : settings.json absent.","data":{"errorCode":"CONFIG_INVALID"},"exception":{"type":"EgRoomTracker.Application.ExtractionFailedException","message":"settings.json absent.","stackTrace":"..."}}
```

## Événements minimaux

`EXTRACTION_STARTED`, `CONFIGURATION_VALIDATED`, `DOCUMENT_ACCESSIBLE`, `COLLECTION_COMPLETED`, `ANOMALIES_DETECTED`
(compteur agrégé par code), `EXPORT_COMPLETED`, `EXTRACTION_FAILED` (avec `data.errorCode`), `EXTRACTION_SUMMARY`
(statut au format de `result.json`, par exemple `succeededWithWarnings`, compteurs, erreurs, durée).
`ROOM_EXTRACTION_FAILED` (niveau `Warning`, avec `roomElementId` et l'exception) signale une pièce exportée avec
l'indicateur `EXTRACTION_ERROR`.

Événements de collecte (EGR-07) : `ROOM_COLLECTION_DETAILS` (nombre de pièces, non placées, en variante, exclues
par le filtre de phase, filtre appliqué). Une phase demandée absente du document provoque un échec global
`CONFIG_INVALID` (`EXTRACTION_FAILED`). `LINKED_DOCUMENT_FAILURES` (Warning) compte les messages d'échec émis par des
liens à l'ouverture.

Événement qualité : `DUPLICATE_OPTION_SET_UNKNOWN` (Warning, étape `quality`, au plus une ligne) signale des
`DUPLICATE_NUMBER` posés entre variantes différentes dont le jeu d'options n'a pas pu être lu (détail
`optionSetUnknown: true` dans `result.json`) ; `data.roomsAffected`, `data.elementIds`.

Événements de volume (EGR-16), étape `document`, après `DOCUMENT_ACCESSIBLE` :

- `VOLUME_COMPUTATION` (Info ; Warning si l'annulation de la transaction temporaire n'est pas confirmée) : `data`
  `volumeMode`, `documentComputeVolumes`, `computeApplied`, `transactionRolledBack`, `volumesComputed`, `roomsRead`
  (nombre de volumes lus pendant la transaction, null sans transaction) ;
- `VOLUME_COMPUTE_FAILED` (Warning) : activation temporaire impossible en mode `compute` ; message = motif. Les volumes
  sont exportés en `notAvailable` et l'anomalie document `VOLUME_COMPUTE_FAILED` figure dans `result.json`.

En échec, `EXTRACTION_FAILED` puis `EXTRACTION_SUMMARY` restent écrits et le
journal est vidé : le rapport APS reste exploitable.

## Volume

Les événements portant un `roomElementId` sont limités à 20 par `eventCode` ; au-delà, ils sont comptés et un
événement `LOG_EVENTS_SUPPRESSED` (niveau `Warning`, compteurs par code dans `data`) est écrit au flush.
Le détail des anomalies reste dans `result.json`.

## Secrets

Jamais de token, secret ni URL signée. Masquage systématique (`***`) :

- valeurs des clés de `data` dont le nom évoque un secret (`token`, `password`, `secret`, `apiKey`, `authorization`,
  `credential`, `signature`, …), y compris imbriquées ;
- dans les textes (message, chemins, valeurs, exceptions) : chaîne de requête des URL `http(s)`, `Bearer`/`Basic`,
  JWT, paires `clé=valeur` / `"clé": "valeur"` sensibles.

Le masquage est un filet de sécurité : le code ne doit pas transmettre de secret au journal.

## Flush

`Flush()` est appelé en fin de `RoomExtractionService.Run` et dans le bloc `finally` du gestionnaire
`DesignAutomationReadyEvent` (`AutomationApplication`) ; `Dispose()` vide également le journal.

## Décision D8 — tranchée

Console uniquement : aucun fichier de log de débogage dans le MVP. Le rapport APS conserve la sortie standard en
succès comme en échec. Un fichier facultatif ne pourra être ajouté que comme évolution explicite, sortie distincte
de l'Activity, sans modifier `result.json` (spécifications, section 11).
