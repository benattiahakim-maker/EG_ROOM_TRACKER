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
| `stage` | toujours | étape (`startup`, `configuration`, `document`, `collection`, `quality`, `export`, `completion`) |
| `message` | toujours | texte lisible, secrets masqués |
| `documentPath` | si pertinent | chemin relatif au répertoire d'entrée |
| `roomElementId` | si pertinent | ElementId de la pièce |
| `durationMs` | si pertinent | durée en millisecondes |
| `data` | si pertinent | données complémentaires (compteurs, `errorCode`, …) |
| `exception` | si présente | `type`, `message`, `stackTrace`, `inner` (5 niveaux max) |

Exemple :

```json
{"timestampUtc":"2026-10-09T12:00:41.120Z","level":"Error","eventCode":"EXTRACTION_FAILED","correlationId":"wi-7f3c2a9e5b1d4c08","stage":"configuration","message":"CONFIG_INVALID : settings.json absent.","data":{"errorCode":"CONFIG_INVALID"},"exception":{"type":"EgRoomTracker.Application.ExtractionFailedException","message":"settings.json absent.","stackTrace":"..."}}
```

## Événements minimaux

`EXTRACTION_STARTED`, `CONFIGURATION_VALIDATED`, `DOCUMENT_ACCESSIBLE`, `COLLECTION_COMPLETED`, `ANOMALIES_DETECTED`
(compteur agrégé par code), `EXPORT_COMPLETED`, `EXTRACTION_FAILED` (avec `data.errorCode`), `EXTRACTION_SUMMARY`
(statut, compteurs, erreurs, durée). En échec, `EXTRACTION_FAILED` puis `EXTRACTION_SUMMARY` restent écrits et le
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

## Décision D8 — proposition

Pas de fichier de log de débogage dans le MVP : la console suffit, car le rapport APS conserve la sortie standard
en succès comme en échec. Un fichier facultatif pourra être ajouté plus tard comme sortie distincte de l'Activity,
sans modifier `result.json`. **À valider par le porteur**, puis à reporter en section 11 des spécifications.
