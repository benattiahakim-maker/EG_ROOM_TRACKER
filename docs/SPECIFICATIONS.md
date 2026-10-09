# EG_ROOM_TRACKER — Spécifications techniques

Version 0.3 — 9 octobre 2026 — Alignée sur le code de `main` (9205e2c, PR #40) et le schéma `result.json` 1.3

Historique :

- **v0.3** (9 octobre 2026) : sections 9 et 10 alignées sur le code et le schéma 1.3 (codes, sévérités, détails des anomalies, champs de `summary`, événements de log, exemples de variantes) ; statut des décisions D1 à D9 avec la PR qui les a tranchées ou mises en œuvre (section 11) ; `volumeMode: compute` livré (EGR-16, PR #40).
- **v0.2** (9 octobre 2026, PR #19) : sortie réduite à un fichier JSON unique `result.json` (bilan, pièces, anomalies) et CSV retiré du MVP ; logs uniquement sur la console ; schéma JSON formel ; exemples de configuration et de résultat ; table des codes et statuts ; décisions ouvertes affectées aux cartes ; cartes EGR-17 (tests unitaires) et EGR-18 (maquettes de référence).
- **v0.1** : proposition initiale.

Rappel des changements de la v0.2 depuis la v0.1 : sortie réduite à un fichier JSON unique `result.json` (bilan, pièces, anomalies) et CSV retiré du MVP ; logs uniquement sur la console ; schéma JSON formel ; exemples de configuration et de résultat ; table des codes et statuts ; décisions ouvertes affectées aux cartes ; cartes EGR-17 (tests unitaires) et EGR-18 (maquettes de référence).

## 1. Objectif et périmètre

Créer une application C# exécutée exclusivement dans Autodesk Platform Services (APS), **Design Automation for Revit**, pour extraire les pièces d'un document RVT reçu dans une archive ZIP et **retourner le résultat dans un fichier JSON unique**. Livrer le code, les builds par version, les AppBundles et les définitions d'Activities nécessaires à leur exécution.

Versions cibles : Revit 2025, 2026 et 2027. Vérifier les moteurs réellement disponibles via l'API APS avant publication. Adapter et tester les dépendances Revit et le runtime .NET au moteur utilisé ; ne pas supposer un runtime identique pour les trois versions.

Hors périmètre : interface web, backend métier d'envoi des WorkItems, Viewer, modification ou sauvegarde du RVT source, synchronisation ACC, déduction géométrique des matériaux de finition, comparaison entre versions de maquettes, export CSV (évolution possible, voir section 12).

## 2. Contrat d'entrée et de sortie

Entrée APS : archive ZIP de maquette et fichier `settings.json`. L'Activity déclare explicitement la décompression de l'entrée et les chemins locaux. Le chemin relatif du RVT principal est obligatoire si plusieurs RVT sont présents. Un ZIP avec un seul RVT peut être résolu automatiquement. Ne jamais sélectionner arbitrairement le premier RVT.

La stratégie d'ouverture retenue dans le prototype doit être cohérente avec l'Activity : RVT principal ouvert par RevitCoreConsole et fourni par `DesignAutomationData.RevitDoc`, ou ouverture explicite du RVT par l'application si nécessaire. Documenter une seule stratégie validée, son propriétaire de fermeture et son comportement pour les documents collaboratifs (décision D1, section 11).

Paramètres de `settings.json` (exemple en section 10.1) :

| Clé | Type | Règle |
| --- | --- | --- |
| `schemaVersion` | chaîne | Obligatoire ; `1.0` pour cette version. Version majeure inconnue : `CONFIG_SCHEMA_UNSUPPORTED` |
| `mainModelPath` | chaîne ou null | Chemin relatif au répertoire d'entrée ; obligatoire si le ZIP contient plusieurs RVT ; un RVT unique est détecté automatiquement |
| `phaseIds` | tableau d'ElementId (chaînes) ou null | Null ou absent : toutes les phases. Sinon entiers positifs sans doublon. Tableau vide : `CONFIG_INVALID` (étape `configuration`). Phase absente du document : `CONFIG_INVALID` (étape `collection`), identifiants inconnus listés dans le message ; jamais de résultat vide présenté comme réussi |
| `volumeMode` | `preserve` ou `compute` | `preserve` par défaut ; `compute` (P2, EGR-16) : calcul temporaire dans une transaction annulée, voir section 3 |
| `sharedParameters` | tableau `{ key, guid }` | Facultatif ; mapping de paramètres partagés par GUID, `key` unique |

Les chemins restent confinés au répertoire d'entrée. Les liens présents dans le ZIP conservent une arborescence documentée (décision D4, règles détaillées de validation dans [`docs/INPUTS.md`](INPUTS.md)) ; les liens absents sont signalés (`LINK_MISSING`), les liens présents mais déchargés sont indiqués (`LINK_NOT_LOADED`, info). Le MVP collecte les pièces du document principal uniquement, sans parcourir les pièces des documents liés.

Sortie : **un seul fichier `result.json`**, paramètre de sortie de l'Activity, UTF-8 sans BOM. C'est l'unique contrat de résultat, décrit par le schéma [`docs/schema/result.schema.json`](schema/result.schema.json) (JSON Schema draft 2020-12) et illustré en section 10.2. Il contient trois blocs :

- `summary` : identifiant de corrélation, version de l'extracteur, version/build Revit, moteur APS lorsque disponible, nom/chemin relatif de la source, dates UTC de début et de fin, options appliquées, traçabilité du calcul des volumes (`volumeComputation`, depuis 1.3), nombres de pièces collectées/exportées/en anomalie, statut final (section 9.3) et erreurs globales (section 9.2) ;
- `rooms` : toutes les pièces collectées (section 3), triées par `elementId` numérique croissant ;
- `anomalies` : anomalies non bloquantes détaillées, au niveau pièce ou document (section 9.1).

`schemaVersion` figure à la racine. Toute modification incompatible du contrat incrémente la version majeure ; un ajout de champ facultatif ou de code incrémente la mineure. Version courante : `1.3` (1.1 : ajout de `LINK_NOT_LOADED` ; 1.2 : ajout de `UNSUPPORTED_PARAMETER_TYPE` ; 1.3 : ajout de `summary.volumeComputation` et de `VOLUME_COMPUTE_FAILED`). Aucun autre fichier de résultat (`rooms.csv`, `summary.json`, `execution.jsonl`) n'est produit. Le rapport natif APS, qui contient les logs console, reste disponible via le WorkItem ; son téléchargement appartient au consommateur, hors bundle.

## 3. Données des pièces

Collecter les instances `Autodesk.Revit.DB.Architecture.Room`, catégorie `OST_Rooms`, sans filtre dépendant de la vue active. Ne pas confondre pièces, espaces MEP, surfaces ou étiquettes.

| Champ | Type JSON | Source et règle |
| --- | --- | --- |
| `elementId` | chaîne | `ElementId.Value`, exporté comme chaîne pour préserver l'identifiant entier |
| `uniqueId` | chaîne | `Element.UniqueId`, rattaché au document décrit par `summary.source` ; ne pas utiliser le numéro de pièce comme clé |
| `name` | chaîne ou null | Paramètre natif `ROOM_NAME` ; éviter un nom qui concatène nom et numéro |
| `number` | chaîne ou null | `Room.Number`, chaîne conservant les zéros initiaux |
| `areaM2` | nombre ou null | `Room.Area`, conversion par `UnitUtils` vers m² |
| `unboundedHeightM` | nombre ou null | `Room.UnboundedHeight`, conversion vers mètres ; hauteur non bornée, pas hauteur libre sous plafond |
| `level` | objet ou null | `Room.LevelId` puis niveau du document : `elementId`, `uniqueId`, `name` ; null si introuvable (`LEVEL_NOT_FOUND`) |
| `occupancy` | chaîne ou null | `ROOM_OCCUPANCY` : usage/occupation textuel, pas nombre d'occupants |
| `volumeM3` | nombre ou null | `Room.Volume`, conversion vers m³ ; null sauf si `volumeStatus` = `computed` |
| `volumeStatus` | énumération | `computed`, `notComputed` (calcul désactivé dans le document, mode `preserve`) ou `notAvailable` (pièce non placée, lecture impossible, erreur de pièce ou échec du calcul temporaire en mode `compute`) |
| `finishes.floor` | chaîne ou null | `ROOM_FINISH_FLOOR` |
| `finishes.wall` | chaîne ou null | `ROOM_FINISH_WALL` |
| `finishes.ceiling` | chaîne ou null | `ROOM_FINISH_CEILING` |
| `finishes.base` | chaîne ou null | `ROOM_FINISH_BASE`, finition des plinthes |
| `phase` | objet ou null | `elementId` et `name` de la phase de la pièce |
| `designOption` | objet ou null | `elementId` et `name` de la variante lorsque applicable |
| `sharedParameters` | objet | Une entrée par clé de mapping : `guid` (provenance) et `value` |
| `qualityFlags` | tableau | Codes d'anomalie de la pièce (section 9.1) ; détails dans `anomalies` |

Les finitions sont les textes renseignés sur la pièce, sans inférence depuis les murs, sols ou plafonds. Utiliser des identifiants de paramètres natifs et les GUID pour les paramètres partagés ; aucun mapping principal basé sur les libellés français/anglais. Ne pas employer `AsValueString()` pour sérialiser les quantités numériques.

Représentation des valeurs : paramètre présent sans valeur → `null` ; chaîne vide renseignée → `""` ; paramètre absent → `null` et indicateur `MISSING_PARAMETER` avec le paramètre en détail ; valeur numérique nulle → `0`. Les nombres restent numériques dans le JSON, en notation invariante, sans arrondi de présentation (précision à confirmer, décision D7).

Paramètres partagés : lecture par GUID ; GUID inconnu du document → `value: null` et une anomalie document `SHARED_PARAMETER_NOT_FOUND` ; paramètre absent de la pièce → `null` et `MISSING_PARAMETER`. Valeurs : texte tel quel, entier en nombre, double de longueur/surface/volume converti en m/m²/m³, nombre sans unité tel quel, autre double jamais exporté en valeur interne (`null` et anomalie document `UNSUPPORTED_PARAMETER_TYPE`), ElementId en chaîne ; détail dans [`docs/LECTURE.md`](LECTURE.md) (EGR-10).

Conserver toutes les pièces, y compris non placées ou de surface nulle, avec indicateurs. Ne pas déduire une cause précise uniquement d'une surface nulle : distinguer `UNPLACED` si l'absence de placement est vérifiée, et `ZERO_AREA` autrement. Signaler les doublons de numéro, sans supprimer ni fusionner de pièces.

Volume : en mode `preserve`, lire le réglage du document ; lorsque le calcul est désactivé, exporter `volumeM3: null` et `volumeStatus: notComputed` pour toutes les pièces et une anomalie document `VOLUME_NOT_COMPUTED`, jamais présenter zéro comme un volume calculé. En mode `compute`, activer le calcul dans une transaction sur la copie de travail, régénérer et lire les résultats. Ne pas sauvegarder le RVT et consigner ce choix. Ce mode doit être validé sur les trois moteurs. Mise en œuvre (EGR-16) : si le réglage du document est déjà actif, lecture directe sans transaction ; sinon transaction temporaire (`ComputeVolumes = true`, `Document.Regenerate()`), lecture des volumes de toutes les pièces **avant** l'annulation (`Transaction.RollBack()` rejette toutes les modifications), puis annulation ; le document est ensuite fermé sans sauvegarde. Volume lu : `computed` ; pièce non placée ou lecture impossible : `notAvailable`. Échec d'activation (transaction refusée, réglage non modifiable) : extraction poursuivie, toutes les pièces en `notAvailable` et anomalie document `VOLUME_COMPUTE_FAILED` (warning) ; échec de régénération (document corrompu) : `DOCUMENT_CORRUPTED`. Traçabilité : `summary.volumeComputation` (`documentComputeVolumes`, `computeApplied`, `transactionRolledBack`) et événement de log `VOLUME_COMPUTATION` (section 5).

## 4. Architecture par services

Le point d'entrée implémente le cycle de vie requis par Design Automation, s'abonne à l'événement prévu par le SDK et délègue au service d'orchestration. Il gère le résultat global de l'événement (`Succeeded`) conformément au SDK. Aucun traitement métier dans l'événement.

| Composant | Responsabilité |
| --- | --- |
| `AutomationApplication` | Initialisation, abonnement/désabonnement, adaptation du contexte APS et résultat d'exécution |
| `RoomExtractionService` | Orchestration des étapes et production du bilan |
| `IInputConfigurationService` | Validation des options et chemins ; configuration typée |
| `IRevitDocumentService` | Accès/ouverture du document selon le contrat, contrôle de version et politique de liens |
| `IRoomCollectionService` | Collecte des Room et filtres explicites de phase |
| `IRoomParameterReader` | Lecture des champs natifs et mappings partagés ; production des DTO |
| `IUnitConversionService` | Conversions numériques explicites |
| `IVolumeCalculationService` | État du calcul et transactions du mode compute |
| `IQualityCheckService` | Calcul des indicateurs qualité et des anomalies à partir des DTO, sans appel Revit |
| `IExportService` | Sérialisation déterministe de `result.json` et publication locale |
| `ILogService` | Journal structuré sur la console, niveaux et contexte |

Organisation proposée : `EntryPoint/`, `Application/`, `Revit/`, `Contracts/`, `Export/`, `Logging/`, `Packaging/`, `Tests/`. Réutiliser le cœur fonctionnel ; isoler les adaptations et références propres à chaque version. DTO sans objets Revit pour séparer lecture et sérialisation. Le cœur indépendant de Revit (configuration, conversions, qualité, export, logs) est couvert par des tests unitaires exécutables hors Revit (EGR-17). Injection simple des dépendances ; éviter une abstraction sans responsabilité réelle.

Les appels Revit API restent sur le contexte d'exécution autorisé : aucun traitement des éléments/documents Revit via `Task.Run` ou accès parallèle. Aucune dépendance à `ActiveUIDocument`, vue active, session desktop ou interaction utilisateur.

## 5. Exécution sans interface et logs

Interdiction de `MessageBox`, `TaskDialog`, fenêtres, sélecteurs de fichiers et demandes de confirmation. Toute information passe par `ILogService`.

Les logs ne font pas partie du résultat : ils sont écrits **sur la console uniquement**, une ligne JSON par événement, et récupérés via le rapport APS du WorkItem. Champs : `timestampUtc`, `level` (`Info`, `Warning`, `Error`), `eventCode`, `correlationId`, `stage`, `message`, `documentPath` relatif, `roomElementId` si pertinent, durée et exception détaillée si présente. Flush en fin de traitement et dans le bloc de terminaison. Ne pas journaliser tokens, secrets ni URL signées. Aucun fichier de log de débogage n'est produit : console uniquement (décision D8, tranchée). Le champ `stage` des logs et de `summary.errors` utilise les identifiants d'étape de la section 9.2.

Événements minimaux : début, configuration validée, document accessible, collecte terminée, anomalies, export terminé, échec et bilan final. Une pièce défectueuse ne doit pas produire des milliers de messages redondants : détail limité et compteur agrégé. Le détail exploitable des anomalies est porté par `result.json`.

Les avertissements Revit à l'ouverture nécessitent une politique explicite, validée dans l'environnement Automation (décision D2). Ne jamais supprimer aveuglément tous les avertissements ni continuer après une corruption bloquante.

## 6. Erreurs et cohérence des résultats

Configuration invalide, RVT ambigu/absent, document non ouvrable, version incompatible ou export impossible : échec global, statut `failed` et code explicite (section 9.2). Propriété manquante : avertissement `MISSING_PARAMETER` et valeur `null`. Erreur limitée à une pièce : exporter son identité et un indicateur `EXTRACTION_ERROR`, continuer les autres et terminer `succeededWithWarnings`. Ne jamais perdre une pièce silencieusement : `counts.roomsExported` = `counts.roomsCollected` hors échec global.

En échec global, écrire si possible un `result.json` avec `status: failed`, `errors` renseigné, `rooms` et `anomalies` vides, puis signaler l'échec au SDK. La remontée de ce fichier par APS en cas d'échec doit être vérifiée (décision D6) ; le log console et le rapport APS restent donc indispensables pour le diagnostic.

Écrire `result.json` dans un fichier temporaire puis le renommer localement ; aucun résultat incomplet annoncé comme valide. Le résultat produit est validé contre le schéma dans les tests. Aucun appel réseau métier nécessaire dans le bundle.

Mise en œuvre (EGR-12, `ResultExportService`) : le document est entièrement sérialisé en mémoire avant toute écriture ; il est écrit dans un fichier temporaire caché du dossier de sortie, vidé sur disque, puis renommé sur `result.json` en remplaçant un fichier existant. En cas d'échec, le fichier temporaire est supprimé et l'orchestration rend `EXPORT_FAILED`. Format : indentation de deux espaces, fins de ligne `\n` et saut de ligne final quelle que soit la plateforme, ordre des propriétés fixé par les contrats, pièces et anomalies dans l'ordre reçu (le tri relève de l'orchestration). Une valeur non représentable en JSON (nombre non fini) est refusée sans rien écrire.

Repli après `EXPORT_FAILED` (EGR-13, décision du chef de projet) : une seule nouvelle tentative d'écrire un `result.json` minimal (`summary` avec `status: failed` et les erreurs, dont `EXPORT_FAILED` ; `rooms` et `anomalies` vides). Si elle échoue aussi, le diagnostic repose sur le seul log console (événement `EXTRACTION_FAILED` avec `fallback: true`).

## 7. Builds, AppBundles et Activities

Préparer trois artefacts explicitement versionnés et testés : 2025, 2026, 2027. Références API provenant de chaque SDK cible, non embarquées si fournies par le moteur. Inclure uniquement les dépendances applicatives nécessaires et les manifestes exigés par APS/Revit.

Les Activities déclarent le moteur, l'AppBundle, la commande RevitCoreConsole, l'entrée ZIP/configuration et le paramètre de sortie `result.json`. Valider les chemins après décompression dans un vrai WorkItem. Versionner bundles et Activities ; utiliser des alias de test puis de production avec procédure de retour à la version précédente.

Mise en œuvre (EGR-13) : manifestes, définitions JSON par version, script d'assemblage et de contrôle des ZIP, et script de publication (dry-run par défaut) dans [`src/Packaging`](../src/Packaging/README.md).

Le pipeline compile, exécute les tests unitaires (EGR-17), valide les exemples contre le schéma, vérifie le contenu des ZIP, publie une version de test et exécute les tests APS. Secrets fournis par le système de déploiement, jamais dans les bundles. La disponibilité des moteurs et l'évolution des runtimes sont vérifiées lors de chaque publication.

## 8. Recette et critères de livraison

Jeu de maquettes de référence pour chaque année, constitué tôt (EGR-18) avec les résultats attendus : pièces renseignées, accents/zéros initiaux, champs vides, non placées/surface nulle, doublons de numéro, plusieurs phases et variantes, calcul de volume désactivé/activé, finitions, paramètres partagés, liens manquants et maquette collaborative. Inclure ZIP ambigu et RVT incompatible.

Mise en œuvre (EGR-18) : matrice des cas, `settings.json` et `result.json` attendus, fiche de construction des maquettes (construites en 2025 puis mises à niveau pour 2026 et 2027) et comparateur dans [`docs/RECETTE.md`](RECETTE.md).

Critères : toutes les pièces attendues sont représentées ; `result.json` valide contre le schéma en succès, avertissement et échec ; quantités conformes à Revit après conversion (tolérances proposées : 0,001 m, 0,001 m², 0,001 m³, à confirmer sur le jeu de référence, décision D5) ; distinction null/zéro correcte ; codes conformes à la section 9 ; aucun dialogue ; logs et résultats récupérables ; résultat APS correct en succès et en échec ; source inchangée ; exécution réussie sur les trois moteurs. Mesurer temps et taille sur une maquette représentative avant de fixer les objectifs de performance.

Livrables : solution C#, tests unitaires du cœur, documentation de build et exécution, schéma JSON (`docs/schema/result.schema.json`), exemples de configuration et de résultat (`docs/examples/`), trois bundles, définitions d'Activities, maquettes de référence et résultats attendus, preuves de recette et procédure de publication/rollback.

## 9. Codes et statuts

### 9.1 Indicateurs qualité et anomalies

Les codes de portée pièce figurent dans `rooms[].qualityFlags` et sont détaillés, avec l'`elementId`, dans `anomalies`. Les codes de portée document figurent uniquement dans `anomalies`, avec `elementId: null`. Aucun de ces codes ne provoque d'échec global.

| Code | Portée | Sévérité | Déclenchement | `details` | Produit par |
| --- | --- | --- | --- | --- | --- |
| `UNPLACED` | pièce | warning | Absence de placement vérifiée (`Location` nulle) ; surface lue conservée | `locationIsNull` | lecture (EGR-08/09) |
| `ZERO_AREA` | pièce | warning | `areaM2 == 0` (pas `null`) sur une pièce sans `UNPLACED` (non fermée ou redondante) | `areaM2` | qualité (EGR-11) |
| `DUPLICATE_NUMBER` | pièce | warning | Même `number` (comparaison exacte, `"001"` ≠ `"1"`) et même phase (`phase.elementId`) ; les pièces sans phase forment un même groupe ; numéros `""` ou `null` ignorés ; posé sur chaque pièce du groupe. **Règle cible décidée en EGR-18, implémentation en attente (EGR Dev)** : comparaison par contexte (phase, variante) ; seules deux pièces de variantes différentes d'un **même** jeu d'options sont exclues (elles ne coexistent jamais) ; des variantes de jeux d'options différents peuvent coexister et sont comparées (doublon possible) ; une pièce du modèle principal (sans variante) est comparée à toutes les pièces de sa phase ; `elementIds` = la pièce et toutes les pièces en conflit avec elle ([`docs/RECETTE.md`](RECETTE.md)) | `number`, `elementIds` (tri numérique) | qualité (EGR-11) |
| `MISSING_PARAMETER` | pièce | warning | Paramètre natif ou partagé attendu absent de la pièce | `parameter` (nom du `BuiltInParameter` ou clé du mapping), `guid` pour un paramètre partagé | lecture (EGR-08/10) |
| `LEVEL_NOT_FOUND` | pièce | warning | `LevelId` invalide ou niveau introuvable ; `level: null` | `levelId` (chaîne ou null) | lecture (EGR-08) |
| `EXTRACTION_ERROR` | pièce | error | Exception limitée à la pièce ; identité exportée, champs illisibles à null, `volumeStatus: notAvailable` | — | orchestration (filet par pièce) |
| `VOLUME_NOT_COMPUTED` | document | info | Calcul des volumes désactivé dans le document et non activé (mode `preserve`) ; pièces en `notComputed` | `volumeMode`, `roomsAffected` | qualité (EGR-11) |
| `VOLUME_COMPUTE_FAILED` | document | warning | Mode `compute` : activation temporaire du calcul impossible ; `volumeM3: null` et `volumeStatus: notAvailable` pour toutes les pièces ; remplace `VOLUME_NOT_COMPUTED` (schéma 1.3) | `volumeMode`, `reason`, `roomsAffected` | qualité (EGR-16) |
| `LINK_MISSING` | document | warning | Lien Revit référencé absent du ZIP | `linkName`, `linkPath` | document (EGR-05/06) |
| `LINK_NOT_LOADED` | document | info | Lien Revit présent mais déchargé (statut `Unloaded` ou `LocallyUnloaded`) ; sans effet sur le statut (schéma 1.1) | `linkName`, `linkPath`, `linkStatus` | document (EGR-06) |
| `REVIT_WARNING_ON_OPEN` | document | warning | Avertissements Revit tolérés par la politique d'ouverture (D2) ; une anomalie agrégée | `warningCount`, `distinctCount`, `warnings` (`description`, `count`) | document (EGR-06) |
| `SHARED_PARAMETER_NOT_FOUND` | document | warning | GUID de `sharedParameters` inconnu dans le document ; `value: null` pour toutes les pièces, sans `MISSING_PARAMETER` | `key`, `guid` | lecture (EGR-10) |
| `UNSUPPORTED_PARAMETER_TYPE` | document | info | Paramètre partagé numérique d'un type autre que longueur, surface, volume ou nombre sans unité (angle, pourcentage, monnaie…) : `value: null` pour toutes les pièces ; une fois par GUID ; sans effet sur le statut (schéma 1.2) | `key`, `guid`, `typeId` (`ForgeTypeId.TypeId`) | lecture (EGR-10) |

Codes de référence : `QualityFlagCodes` et `DocumentAnomalyCodes` (`src/Contracts/Codes/QualityFlagCodes.cs`), identiques aux énumérations `qualityFlag` et `documentAnomalyCode` du schéma (vérifié par les tests de contrat).

Ordre : `qualityFlags` sans doublon, indicateurs de lecture puis `ZERO_AREA` et `DUPLICATE_NUMBER` ; `anomalies` = anomalies document (ouverture, paramètres partagés, puis `VOLUME_NOT_COMPUTED` ou `VOLUME_COMPUTE_FAILED`), puis anomalies de pièce dans l'ordre des pièces (lecture puis qualité). Détail : [`docs/QUALITE.md`](QUALITE.md).

### 9.2 Erreurs globales

Une erreur globale termine le traitement avec `status: failed` ; le code figure dans `summary.errors`, avec l'identifiant d'étape (`stage`).

| Code | `stage` | Cause |
| --- | --- | --- |
| `CONFIG_INVALID` | `configuration` (configuration) ; `collection` (collecte) pour une phase inconnue | `settings.json` absent, illisible ou non conforme, dont `phaseIds` vide ou `mainModelPath` contenant des caractères interdits ; ou `phaseIds` désignant une phase absente du document (identifiants listés dans le message) |
| `CONFIG_SCHEMA_UNSUPPORTED` | `configuration` (configuration) | Version majeure de `schemaVersion` non prise en charge |
| `PATH_OUTSIDE_INPUT` | `configuration` (configuration) | Chemin sortant du répertoire d'entrée |
| `RVT_NOT_FOUND` | `input` (entrée) | Aucun RVT, ou `mainModelPath` introuvable |
| `RVT_AMBIGUOUS` | `input` (entrée) | Plusieurs RVT sans `mainModelPath` |
| `REVIT_VERSION_INCOMPATIBLE` | `document` (document) | RVT enregistré dans une version postérieure au moteur ou non ouvrable par lui |
| `DOCUMENT_OPEN_FAILED` | `document` (document) | Échec d'ouverture ou d'accès au document |
| `DOCUMENT_CORRUPTED` | `document` (document) | Corruption ou erreur bloquante à l'ouverture ; échec de régénération pendant le calcul temporaire des volumes (mode `compute`) |
| `EXPORT_FAILED` | `export` (export) | Écriture ou finalisation de `result.json` impossible |
| `UNEXPECTED_ERROR` | étape en cours : `startup`, `configuration`, `input`, `document`, `collection`, `quality`, `export` ou `completion` (toutes) | Exception non prévue hors du périmètre d'une pièce. Un échec de composition dans `AutomationApplication` (avant l'orchestration) n'est visible que dans le log console (`EXTRACTION_FAILED`, étape `startup`), sans `result.json` |

Identifiants d'étape : `startup` (démarrage), `configuration` (configuration), `input` (entrée), `document` (document), `collection` (collecte), `quality` (qualité), `export` (export), `completion` (bilan), `logging` (journal, logs uniquement).

### 9.3 Statuts finaux

| Statut | Condition | Résultat SDK |
| --- | --- | --- |
| `succeeded` | Aucune erreur globale, aucune anomalie de sévérité warning ou error | Succès |
| `succeededWithWarnings` | Aucune erreur globale, au moins une anomalie warning ou error | Succès |
| `failed` | Au moins une erreur globale | Échec |

Une anomalie de sévérité info (`VOLUME_NOT_COMPUTED`, `LINK_NOT_LOADED`, `UNSUPPORTED_PARAMETER_TYPE`) n'empêche pas `succeeded`. Une pièce `EXTRACTION_ERROR` force `succeededWithWarnings` côté orchestration (D2). `Succeeded` du SDK = statut différent de `failed`.

### 9.4 Champs de `summary`

| Champ | Type | Règle (mise en œuvre) |
| --- | --- | --- |
| `correlationId` | chaîne | Identifiant de l'exécution, aussi porté par chaque ligne de log. Identifiant du WorkItem Design Automation (variable d'environnement `DAS_WORKITEM_ID`) ; à défaut (exécution hors APS), `run-<guid>` généré par l'add-in |
| `extractorVersion` | chaîne | Version de l'assembly, sans métadonnées de build : source unique `<Version>` de `Directory.Build.props`, propagée à l'assembly et lue à l'exécution (aussi `@APP_VERSION@` de l'AppBundle) |
| `revit.version` / `revit.build` | chaîne / chaîne ou null | `Application.VersionNumber` / `VersionBuild` du moteur |
| `apsEngine` | chaîne ou null | `EngineId` du fichier `<id du WorkItem>_job.das` écrit par Design Automation dans le répertoire de travail (par exemple `Autodesk.Revit+2026`) ; `null` si le fichier, le champ ou l'identifiant du WorkItem est absent ou illisible |
| `source` | objet ou null | `fileName`, `relativePath` (relatif au répertoire d'entrée), `isWorkshared` (null si inconnu) ; null si le RVT n'a pas été résolu |
| `startedAtUtc` / `completedAtUtc` | date-heure UTC | Début et fin de l'orchestration |
| `options` | objet | Options appliquées : `phaseIds` (null = toutes), `volumeMode`, `sharedParameterKeys` |
| `volumeComputation` | objet ou null | Depuis 1.3 : `documentComputeVolumes` (réglage à l'ouverture, null si non lu), `computeApplied` (calcul activé temporairement), `transactionRolledBack` (null sans transaction) ; null si l'échec survient avant la préparation des volumes ; facultatif dans le schéma, toujours émis par l'extracteur 1.3 |
| `counts` | objet | `roomsCollected`, `roomsExported` (= collectées hors échec global), `roomsWithAnomalies` (pièces avec au moins un `qualityFlag`) |
| `status` | énumération | Section 9.3 |
| `errors` | tableau | `code`, `stage`, `message` ; non vide si et seulement si `status = failed` |

### 9.5 Événements de log

Codes (`LogEventCodes`, détail dans [`docs/LOGS.md`](LOGS.md)) : `EXTRACTION_STARTED`, `CONFIGURATION_VALIDATED`, `DOCUMENT_ACCESSIBLE`, `VOLUME_COMPUTATION`, `COLLECTION_COMPLETED`, `ANOMALIES_DETECTED`, `EXPORT_COMPLETED`, `EXTRACTION_SUMMARY` (séquence nominale) ; `EXTRACTION_FAILED`, `ROOM_EXTRACTION_FAILED`, `DOCUMENT_CLOSE_FAILED`, `ROOM_COLLECTION_DETAILS`, `VOLUME_COMPUTE_FAILED`, `LOG_EVENTS_SUPPRESSED` ; côté document, `DOCUMENT_UPGRADE_IN_MEMORY` et `LINKED_DOCUMENT_FAILURES`.

## 10. Exemples

Données **illustratives** : identifiants, builds et valeurs sont fictifs. Les fichiers font foi : [`docs/examples/settings.example.json`](examples/settings.example.json) et [`docs/examples/result.example.json`](examples/result.example.json). Le second est validé contre le schéma par `python docs/schema/validate_examples.py`.

### 10.1 `settings.json`

```json
{
  "schemaVersion": "1.0",
  "mainModelPath": "Projet/EG_Batiment_A.rvt",
  "phaseIds": null,
  "volumeMode": "preserve",
  "sharedParameters": [
    { "key": "codeLocal", "guid": "3f2b8c1e-6d4a-4b7e-9a51-2c0d8e7f4a10" }
  ]
}
```

### 10.2 `result.json`

Fichier de référence, schéma 1.3. Trois pièces, document sans calcul de volume (mode `preserve`) : « Bureau » complet mais numéro 001 en doublon, « Local technique » sans anomalie (plafond renseigné vide, plinthe sans valeur), « Réserve » non placée et en doublon.

```json
{
  "schemaVersion": "1.3",
  "summary": {
    "correlationId": "7f3c2a9e5b1d4c08a1b2c3d4e5f60718",
    "extractorVersion": "0.1.0",
    "revit": { "version": "2026", "build": "26.0.4.409" },
    "apsEngine": "Autodesk.Revit+2026",
    "source": {
      "fileName": "EG_Batiment_A.rvt",
      "relativePath": "Projet/EG_Batiment_A.rvt",
      "isWorkshared": false
    },
    "startedAtUtc": "2026-10-09T12:00:03Z",
    "completedAtUtc": "2026-10-09T12:00:41Z",
    "options": {
      "phaseIds": null,
      "volumeMode": "preserve",
      "sharedParameterKeys": ["codeLocal"]
    },
    "volumeComputation": { "documentComputeVolumes": false, "computeApplied": false, "transactionRolledBack": null },
    "counts": { "roomsCollected": 3, "roomsExported": 3, "roomsWithAnomalies": 2 },
    "status": "succeededWithWarnings",
    "errors": []
  },
  "rooms": [
    {
      "elementId": "412345",
      "uniqueId": "5b1e2f8a-1c3d-4e5f-8a9b-0c1d2e3f4a5b-00064b39",
      "name": "Bureau",
      "number": "001",
      "areaM2": 18.425,
      "unboundedHeightM": 2.7,
      "level": { "elementId": "311", "uniqueId": "8d2c4f6a-0b1e-4c3d-9e8f-7a6b5c4d3e2f-00000137", "name": "R+0" },
      "occupancy": "Bureau individuel",
      "volumeM3": null,
      "volumeStatus": "notComputed",
      "finishes": { "floor": "Parquet chêne", "wall": "Peinture blanche", "ceiling": "Faux plafond 60x60", "base": "Plinthe bois" },
      "phase": { "elementId": "86961", "name": "Nouvelle construction" },
      "designOption": null,
      "sharedParameters": {
        "codeLocal": { "guid": "3f2b8c1e-6d4a-4b7e-9a51-2c0d8e7f4a10", "value": "A-001" }
      },
      "qualityFlags": ["DUPLICATE_NUMBER"]
    },
    {
      "elementId": "412398",
      "uniqueId": "5b1e2f8a-1c3d-4e5f-8a9b-0c1d2e3f4a5b-00064b6e",
      "name": "Local technique",
      "number": "002",
      "areaM2": 6.12,
      "unboundedHeightM": 2.7,
      "level": { "elementId": "311", "uniqueId": "8d2c4f6a-0b1e-4c3d-9e8f-7a6b5c4d3e2f-00000137", "name": "R+0" },
      "occupancy": null,
      "volumeM3": null,
      "volumeStatus": "notComputed",
      "finishes": { "floor": "Résine", "wall": "Peinture blanche", "ceiling": "", "base": null },
      "phase": { "elementId": "86961", "name": "Nouvelle construction" },
      "designOption": null,
      "sharedParameters": {
        "codeLocal": { "guid": "3f2b8c1e-6d4a-4b7e-9a51-2c0d8e7f4a10", "value": "A-002" }
      },
      "qualityFlags": []
    },
    {
      "elementId": "415002",
      "uniqueId": "5b1e2f8a-1c3d-4e5f-8a9b-0c1d2e3f4a5b-0006551a",
      "name": "Réserve",
      "number": "001",
      "areaM2": 0,
      "unboundedHeightM": 2.7,
      "level": { "elementId": "311", "uniqueId": "8d2c4f6a-0b1e-4c3d-9e8f-7a6b5c4d3e2f-00000137", "name": "R+0" },
      "occupancy": "Stockage",
      "volumeM3": null,
      "volumeStatus": "notComputed",
      "finishes": { "floor": null, "wall": null, "ceiling": null, "base": null },
      "phase": { "elementId": "86961", "name": "Nouvelle construction" },
      "designOption": null,
      "sharedParameters": {
        "codeLocal": { "guid": "3f2b8c1e-6d4a-4b7e-9a51-2c0d8e7f4a10", "value": null }
      },
      "qualityFlags": ["UNPLACED", "DUPLICATE_NUMBER"]
    }
  ],
  "anomalies": [
    {
      "code": "VOLUME_NOT_COMPUTED",
      "severity": "info",
      "scope": "document",
      "elementId": null,
      "message": "Calcul des volumes désactivé dans le document ; volumeM3 exporté à null (mode preserve).",
      "details": { "volumeMode": "preserve", "roomsAffected": 3 }
    },
    {
      "code": "DUPLICATE_NUMBER",
      "severity": "warning",
      "scope": "room",
      "elementId": "412345",
      "message": "Numéro 001 partagé par 2 pièces.",
      "details": { "number": "001", "elementIds": ["412345", "415002"] }
    },
    {
      "code": "UNPLACED",
      "severity": "warning",
      "scope": "room",
      "elementId": "415002",
      "message": "Pièce non placée (Location absente) ; surface 0 conservée.",
      "details": { "locationIsNull": true }
    },
    {
      "code": "DUPLICATE_NUMBER",
      "severity": "warning",
      "scope": "room",
      "elementId": "415002",
      "message": "Numéro 001 partagé par 2 pièces.",
      "details": { "number": "001", "elementIds": ["412345", "415002"] }
    }
  ]
}
```

### 10.3 Variantes (extraits)

Mode `compute`, réglage du document désactivé, calcul temporaire réussi (pièces placées en `computed`, aucune anomalie de volume) :

```json
"options": { "phaseIds": null, "volumeMode": "compute", "sharedParameterKeys": [] },
"volumeComputation": { "documentComputeVolumes": false, "computeApplied": true, "transactionRolledBack": true }
```

Mode `compute`, activation impossible (statut `succeededWithWarnings`, pièces en `notAvailable`) :

```json
{
  "code": "VOLUME_COMPUTE_FAILED",
  "severity": "warning",
  "scope": "document",
  "elementId": null,
  "message": "Activation temporaire du calcul des volumes impossible ; volumeM3 exporté à null (mode compute).",
  "details": { "volumeMode": "compute", "reason": "Activation du calcul des volumes impossible (InvalidOperationException).", "roomsAffected": 3 }
}
```

Échec global (configuration) : `rooms` et `anomalies` vides, `volumeComputation` null :

```json
"volumeComputation": null,
"counts": { "roomsCollected": 0, "roomsExported": 0, "roomsWithAnomalies": 0 },
"status": "failed",
"errors": [{ "code": "CONFIG_INVALID", "stage": "configuration", "message": "…" }]
```

## 11. Décisions

Statuts : **tranchée** (décidée et mise en œuvre), **proposée** (mise en œuvre par défaut, approbation attendue), **à tester / en attente** (dépend d'une exécution APS réelle ou d'une mesure).

| ID | Décision | Statut | Ce qui est décidé ou proposé | Carte / PR |
| --- | --- | --- | --- | --- |
| D1 | Stratégie d'ouverture du RVT, propriétaire de fermeture, maquettes collaboratives | **Tranchée** | Ouverture explicite du RVT principal par l'application (Activity sans `/i`) ; maquette collaborative détachée avec conservation des worksets, tous les worksets ouverts ; fermeture sans sauvegarde par l'application dans un bloc `finally` ; détail dans [`docs/DOCUMENT.md`](DOCUMENT.md). Validation dans un WorkItem réel : EGR-14 | EGR-06 — PR #23 (suivi #24) |
| D2 | Politique des avertissements et erreurs Revit à l'ouverture | **Tranchée** (mode strict MVP) | Avertissements tolérés et signalés (`REVIT_WARNING_ON_OPEN`) ; toute erreur ou corruption du document principal bloque (`DOCUMENT_CORRUPTED`) ; une pièce `EXTRACTION_ERROR` force `succeededWithWarnings`. Tolérance opt-in de certaines erreurs à étudier après EGR-18 | EGR-06 — PR #23 (suivi #24) |
| D3 | Choix métier par défaut (section 12) | **Proposée** | Mis en œuvre par défaut : pièces du document principal, toutes phases, hauteur non bornée, occupation et finitions textuelles, `volumeMode: preserve` (`compute` disponible, PR #40). Approbation du propriétaire attendue | EGR-01 (en attente du propriétaire) |
| D4 | Arborescence des liens dans le ZIP et résolution de leurs chemins | **Tranchée** | Arborescence du projet conservée sous la racine du ZIP, liens en chemin relatif à l'hôte, `mainModelPath` obligatoire dès qu'il y a des liens, lien introuvable signalé `LINK_MISSING` sans échec ; détail dans [`docs/INPUTS.md`](INPUTS.md) | EGR-05 — PR #27 |
| D5 | Tolérances de comparaison des quantités | **Proposée**, à confirmer | 0,001 m, 0,001 m², 0,001 m³ (section 8), et 0,001 sur les valeurs numériques des paramètres partagés, configurables dans le comparateur de recette ; confirmation sur le jeu de référence ([`docs/RECETTE.md`](RECETTE.md), point C14) | EGR-18 (en attente des maquettes) |
| D6 | Sortie en échec : remontée de `result.json` par APS quand le SDK signale l'échec | **À tester** | Comportement mis en œuvre : `result.json` `failed` écrit, une seule nouvelle tentative minimale après `EXPORT_FAILED`, puis `Succeeded = false`. La documentation APS ne dit pas si les sorties sont téléversées dans ce cas ; protocole dans [`src/Packaging/README.md`](../src/Packaging/README.md#d6--sortie-en-échec-à-tester) | EGR-13 — PR #34 ; test en EGR-14 |
| D7 | Précision numérique de sérialisation | **Proposée** | Mis en œuvre : valeur brute convertie par `UnitUtils`, sans arrondi, `double` sérialisé en notation invariante ([`docs/LECTURE.md`](LECTURE.md)) | EGR-09 — PR #29 ; approbation EGR-01 |
| D8 | Fichier de log de débogage facultatif | **Tranchée** | Console uniquement, aucun fichier de log dans le MVP | EGR-04 — PR #21 (suivi #22) |
| D9 | Objectifs de performance (temps, taille de maquette) | **En attente** | À fixer après mesure sur une maquette représentative | EGR-14 (en attente de Revit/APS) |

## 12. Hypothèses métier à confirmer

Par défaut : pièces du document principal, toutes phases, finitions textuelles, occupation textuelle, hauteur non bornée, volumes existants sans recalcul. Ces choix n'empêchent pas la préparation technique ; toute extension aux pièces des liens, hauteur géométrique, nombre d'occupants ou export CSV dérivé de `result.json` constitue une évolution explicite.

## 13. Références techniques consultées

- [Automation API APS](https://aps.autodesk.com/apis-and-services/automation-api)
- [Guide Autodesk : Rooms](https://help.autodesk.com/cloudhelp/2024/ENU/Revit-API/files/Revit_API_Developers_Guide/Discipline_Specific_Functionality/Architecture/Revit_API_Revit_API_Developers_Guide_Discipline_Specific_Functionality_Architecture_Rooms_html.html)
- [Référence Room, Revit 2026](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/75c9d2c7-a402-ea8b-9e7c-f8bc3510bbd5.htm)
- [Annonce Autodesk : moteurs 2027](https://blog.autodesk.io/automation-api-old-engine-removal-and-add-2027-engines/)
- [Annonce migration du moteur 2026 vers 2026.5/.NET 10](https://aps.autodesk.com/blog/revit-automation-engine-upgrading-revit-20265-and-net-10-september-21-2026)
- [JSON Schema draft 2020-12](https://json-schema.org/draft/2020-12)

Les annonces ne remplacent pas la vérification authentifiée des moteurs et un test réel des AppBundles.
