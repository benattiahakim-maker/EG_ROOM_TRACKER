# Logs d'exécution

Implémentation : `EgRoomTracker.Logging.JsonConsoleLogService` (EGR-04, spécifications section 5).

## Format

Une ligne JSON par événement sur la **sortie standard** (console), récupérée dans le rapport APS du WorkItem.
Les **mêmes lignes** sont aussi écrites dans `log.jsonl` (voir « Fichier `log.jsonl` »). Les logs ne font pas partie de
`result.json` ni de `result.generic.json`.

| Champ | Présence | Contenu |
| --- | --- | --- |
| `timestampUtc` | toujours | ISO 8601 UTC, millisecondes (`2026-10-09T12:00:03.250Z`) |
| `level` | toujours | `Info`, `Warning`, `Error` |
| `eventCode` | toujours | code d'événement (`EXTRACTION_STARTED`, …, voir `LogEventCodes`) |
| `correlationId` | toujours | identifiant de corrélation de l'exécution, identique à `summary.correlationId` : identifiant du WorkItem (`DAS_WORKITEM_ID`), sinon `run-<guid>` |
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
{"timestampUtc":"2026-10-09T12:00:41.120Z","level":"Error","eventCode":"EXTRACTION_FAILED","correlationId":"7f3c2a9e5b1d4c08a1b2c3d4e5f60718","stage":"configuration","message":"CONFIG_INVALID : settings.json absent.","data":{"errorCode":"CONFIG_INVALID"},"exception":{"type":"EgRoomTracker.Application.ExtractionFailedException","message":"settings.json absent.","stackTrace":"..."}}
```

## Événements minimaux

`EXTRACTION_STARTED`, `CONFIGURATION_VALIDATED`, `DOCUMENT_ACCESSIBLE`, `COLLECTION_COMPLETED`, `ANOMALIES_DETECTED`
(compteur agrégé par code), `VOLUME_NOT_COMPUTED` (seulement si le calcul des volumes est désactivé, même texte que l'enveloppe generic quand N > 0 ; voir plus bas), `EXPORT_COMPLETED`, `EXTRACTION_FAILED` (avec `data.errorCode`), `EXTRACTION_SUMMARY`
(statut au format de `result.json`, par exemple `succeededWithWarnings`, compteurs, erreurs, durée). Dans `data`,
`roomsCollected` est le nombre de pièces lues dans le document et `roomsExported` le nombre de pièces réellement présentes dans le résultat :
en sortie `generic`, les pièces d'options de conception secondaires exclues (`includeSecondaryDesignOptions` : `false`) ne sont pas comptées
(67 collectées, 10 exclues : `roomsExported` = 57). `roomsWithAnomalies` compte les pièces portant au moins un indicateur qualité
(`qualityFlags` : `UNPLACED`, `ZERO_AREA`, `DUPLICATE_NUMBER`…), pas les anomalies : une anomalie de portée document comme
`VOLUME_NOT_COMPUTED` ou `LINK_MISSING` n'est attachée à aucune pièce et n'y entre pas (12 pièces sans volume calculé : `roomsWithAnomalies`
peut valoir 0). Même définition que `summary.counts.roomsWithAnomalies` de `result.json`. Les noms et le schéma du journal ne changent pas.
`ROOM_EXTRACTION_FAILED` (niveau `Warning`, avec `roomElementId` et l'exception) signale une pièce exportée avec
l'indicateur `EXTRACTION_ERROR`.

Événements de collecte (EGR-07) : `ROOM_COLLECTION_DETAILS` (nombre de pièces, non placées, en variante, exclues
par le filtre de phase, filtre appliqué). Une phase demandée absente du document provoque un échec global
`CONFIG_INVALID` (`EXTRACTION_FAILED`).

Événements d'ouverture, étape `document` (`LogEventCodes.DocumentUpgradeInMemory` et `LogEventCodes.LinkedDocumentFailures`) :
`DOCUMENT_UPGRADE_IN_MEMORY` (Info) signale un RVT d'une version antérieure mis à niveau en mémoire, sans sauvegarde ;
`LINKED_DOCUMENT_FAILURES` (Warning) compte les messages d'échec émis par des liens à l'ouverture.

Événement qualité : `DUPLICATE_OPTION_SET_UNKNOWN` (Warning, étape `quality`, au plus une ligne) signale des
`DUPLICATE_NUMBER` posés entre variantes différentes dont le jeu d'options n'a pas pu être lu (détail
`optionSetUnknown: true` dans `result.json`) ; `data.roomsAffected`, `data.elementIds`.

Événements de volume (EGR-16), étape `document`, après `DOCUMENT_ACCESSIBLE` :

- `VOLUME_COMPUTATION` (Info ; Warning si l'annulation de la transaction temporaire n'est pas confirmée) : `data`
  `volumeMode`, `documentComputeVolumes`, `computeApplied`, `transactionRolledBack`, `volumesComputed`, `roomsRead`
  (nombre de volumes lus pendant la transaction, null sans transaction) ;
- `VOLUME_COMPUTE_FAILED` (Warning) : activation temporaire impossible en mode `compute` ; message = motif. Les volumes
  sont exportés en `notAvailable` et l'anomalie document `VOLUME_COMPUTE_FAILED` figure dans `result.json`.

Événement `VOLUME_NOT_COMPUTED` (Info, étape `quality`, au plus une ligne, juste après `ANOMALIES_DETECTED`) : le document a le calcul
des volumes désactivé (mode `preserve`). Le message est `N pièce(s) sans volume calculé : calcul des volumes désactivé dans le document
(volumeM3 exporté à null ; mode preserve).` ; il est **identique quand N > 0** à l'information `VOLUME_NOT_COMPUTED` de `warnings[]` dans
`result.generic.json` (même nombre N, lu une seule fois dans `roomsAffected` de l'anomalie document `VOLUME_NOT_COMPUTED`). Aucune ligne
si le calcul est actif ou si le mode `compute` a échoué (c'est alors `VOLUME_COMPUTE_FAILED`). `result.json` garde l'anomalie document.

Cas limite : N = 0. La ligne est écrite dès que l'anomalie document `VOLUME_NOT_COMPUTED` existe ; `roomsAffected` absent compte pour 0. Le
journal écrit alors « 0 pièce(s) sans volume calculé… », alors que l'information de `result.generic.json` est omise quand N vaut 0. Comportement
actuel, volontairement inchangé ici (ne pas écrire la ligne quand N = 0 est une décision à part).

En échec, `EXTRACTION_FAILED` puis `EXTRACTION_SUMMARY` restent écrits et le
journal est vidé : le rapport APS reste exploitable.

## Volume

Les événements portant un `roomElementId` sont limités à 20 par `eventCode` ; au-delà, ils sont comptés et un
événement `LOG_EVENTS_SUPPRESSED` (niveau `Warning`, compteurs par code dans `data`) est écrit au flush.
Le détail des anomalies reste dans le fichier de sortie, et la ligne `ANOMALIES_DETECTED` le nomme selon `outputFormat` :
`détail dans result.json.` (`contract`, `both`), `détail dans result.generic.json.` (`generic`), `aucun fichier de détail avec le format trc.`
(`trc` : `TRCValidationDict.json` ne contient que les données). Seul le texte du message change ; `data` (compteurs par code) est identique.

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

## Fichier `log.jsonl`

L'AppBundle copie son journal dans `result/log.jsonl`, donc à la racine du ZIP de sortie (à côté de `result.json`,
`result.generic.json` et `Images/`) : le Testeur ou la plateforme peuvent le lire sans passer par le rapport APS.

- Mêmes lignes, mêmes champs, même masquage des secrets que sur la console : JSON Lines en ASCII pur, une ligne par
  événement, sans BOM, fin de ligne `\n`. Même plafond d'événements par pièce, même résumé `LOG_EVENTS_SUPPRESSED`.
- Fichier annexe : il n'est ni dans `result.json` ni dans l'enveloppe `generic`, dont le format ne change pas.
- Le fichier est créé à la première ligne et vidé à chaque flush, puis fermé (`Dispose`) avant le zip de l'Activity,
  y compris en cas d'échec de l'extraction, tant que le dossier `result/` existe (il est créé au démarrage).
- Une erreur d'écriture ne fait jamais échouer l'extraction : un seul avertissement `LOG_FILE_UNAVAILABLE` (étape
  `logging`, `data.reason` = type de l'erreur) est écrit sur la console, le fichier est abandonné et la console continue.
- Le backend de la plateforme n'est pas modifié ; il n'expose pas ce fichier aujourd'hui.

## Décision D8 — révisée

Décision initiale : console uniquement. Sur demande du chef de projet, le journal est aussi copié dans `log.jsonl`
dans le ZIP de sortie (ci-dessus). Le rapport APS conserve toujours la sortie standard en succès comme en échec.
