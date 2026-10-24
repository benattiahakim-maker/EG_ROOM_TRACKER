# Accès au document Revit en environnement Automation (EGR-06)

Implémentation : `EgRoomTracker.Revit.RevitDocumentService` et `OpenFailurePolicy` ; règles indépendantes de Revit
dans `EgRoomTracker.Application.Revit` (`MainModelLocation`, `RevitVersionCompatibility`, `DocumentAnomalies`).

## D1 — Stratégie d'ouverture (tranchée)

- **Ouverture explicite par l'application** : l'Activity lance RevitCoreConsole **sans `/i`** ; l'entrée ZIP est
  décompressée et l'application ouvre le RVT principal avec `Application.OpenDocumentFile`. Raison : l'entrée est un
  ZIP pouvant contenir plusieurs RVT et des liens ; seul `mainModelPath` désigne le document à ouvrir.
- **Chemin** : `mainModelPath` résolu par la configuration (EGR-05), y compris pour un ZIP à RVT unique.
  Contrôle défensif : chemin confiné au répertoire d'entrée (`PATH_OUTSIDE_INPUT`), fichier `.rvt` existant
  (`RVT_NOT_FOUND`).
- **Version** : `BasicFileInfo` lu avant ouverture ; RVT postérieur au moteur → `REVIT_VERSION_INCOMPATIBLE`.
  RVT antérieur : mise à niveau en mémoire seulement (événement `DOCUMENT_UPGRADE_IN_MEMORY`, `LogEventCodes.DocumentUpgradeInMemory`).
- **Maquettes collaboratives** : détachement du central en conservant les worksets
  (`DetachAndPreserveWorksets`), tous les worksets ouverts (`OpenAllWorksets`) pour ne manquer aucune pièce.
  Le central n'est jamais modifié.
- **Propriétaire de fermeture** : `RevitDocumentService`, qui ferme le document sans sauvegarde
  (`Document.Close(false)`) ; `RoomExtractionService` appelle `Close()` dans un bloc `finally` dès la fin de la lecture, en succès comme
  en échec, avant l'export. Le RVT source n'est jamais sauvegardé.
- **Document fourni par l'Activity** (`DesignAutomationData.RevitDoc`, si `/i` était utilisé) : réutilisé, sans
  être fermé, uniquement s'il correspond au RVT principal ; sinon `DOCUMENT_OPEN_FAILED`.

## D2 — Avertissements et erreurs à l'ouverture (tranchée : mode strict pour le MVP)

Pendant l'ouverture seulement (abonnement à `Application.FailuresProcessing`) :

- **Avertissements** (`FailureSeverity.Warning`) : tolérés, retirés et consignés. Une anomalie de document
  `REVIT_WARNING_ON_OPEN` agrège le nombre total et les descriptions distinctes (20 max, avec occurrences).
- **Erreurs et corruptions** (`Error`, `DocumentCorruption`) : aucune résolution automatique ; annulation
  (`ProceedWithRollBack`) et échec global `DOCUMENT_CORRUPTED`.
- `CorruptModelException` → `DOCUMENT_CORRUPTED` ; autre exception Revit d'ouverture → `DOCUMENT_OPEN_FAILED`.
- **Documents liés** : seuls les échecs du document principal sont traités par cette politique. Les messages émis
  par un document lié pendant son chargement (`FailuresAccessor.GetDocument().IsLinked`) sont laissés au traitement
  par défaut de Revit et comptés (log `LINKED_DOCUMENT_FAILURES`, `LogEventCodes.LinkedDocumentFailures`) : un lien défectueux ne fait pas échouer le
  document principal.
- Tolérance opt-in de certaines erreurs : à étudier après EGR-18, hors MVP.

## Liens

Après ouverture, chaque `RevitLinkType` de premier niveau au statut `NotFound` produit une anomalie `LINK_MISSING`
(`details.linkName`, `details.linkPath`) ; ces deux valeurs sont des **noms de fichier** (jamais un chemin de la machine d'origine). Un lien présent mais déchargé (`Unloaded`, `LocallyUnloaded`) produit
une anomalie d'information `LINK_NOT_LOADED` (`details.linkStatus` en plus), sans effet sur le statut. Les pièces des liens ne sont pas collectées (MVP). L'arborescence des
liens dans le ZIP relève de D4 (EGR-05).

## À valider dans un WorkItem réel (EGR-02/EGR-14)

- Déclenchement de `FailuresProcessing` pendant `OpenDocumentFile` dans RevitCoreConsole et classement des
  avertissements usuels.
- Résolution des chemins relatifs des liens après décompression.
- Comportement des maquettes collaboratives (détachement, worksets) et des fichiers antérieurs sur les trois moteurs.
