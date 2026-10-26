# Changelog

Toutes les évolutions notables d'EG_ROOM_TRACKER sont consignées dans ce fichier.

Le format s'inspire de [Keep a Changelog](https://keepachangelog.com/fr/1.1.0/). Les versions de l'extracteur ne sont pas encore publiées sur APS (version d'assembly `0.1.0`) : les entrées sont regroupées par version du contrat `result.json` et référencent les PR fusionnées dans `main` (liste vérifiée avec `gh pr list --state merged`).

## [Non publié]

### Ajouté

- **Journal dans le ZIP de sortie** : l'AppBundle copie son journal JSON Lines (mêmes lignes que sur la console, ASCII, secrets masqués,
  même plafond par pièce) dans `log.jsonl`, à la racine du ZIP de sortie. Fichier annexe : ni `result.json` ni l'enveloppe `generic` ne
  changent. Une erreur d'écriture ne fait jamais échouer l'extraction (avertissement console `LOG_FILE_UNAVAILABLE`). Voir
  [`docs/LOGS.md`](docs/LOGS.md). Non validé dans Design Automation.
- **Sortie `generic`** (`outputFormat: "generic"`) : écrit `result.generic.json`, l'enveloppe commune des résultats de la plateforme
  (`contractVersion` `1.0`, `serviceId` `pieces-roomtracker`, `models[]`, arbre `objects[]` de propriétés typées : `box`, `ref`, `file`,
  `group`). Une pièce est un objet racine ; portes, équipements et terminaux sont des objets rangés une fois sous leur première pièce, les
  autres pièces les pointent par `ref`. Le TRC reste un export à part, inchangé. Schéma `docs/schema/generic.schema.json`, exemple
  `docs/examples/generic.example.json`, voir [`docs/GENERIC.md`](docs/GENERIC.md). Non validé dans Design Automation.
- `summary.options.outputFormat` peut valoir `generic` (`result.schema.json`).

### Modifié

- **Sortie `generic` : `VOLUME_NOT_COMPUTED` en une information** : l'anomalie (info, portée document) n'est plus un objet « Anomalie » ;
  elle devient une seule information `{severity: info, code: VOLUME_NOT_COMPUTED, message: "N pièce(s) sans volume calculé…"}` dans
  `warnings[]` (jamais `partial`), comme `LINK_MISSING`. Même message dans `log.jsonl` (événement `VOLUME_NOT_COMPUTED`). `warnings[]` est
  plafonné à 20 éléments (informations gardées). Les autres anomalies document (ex. `LINK_NOT_LOADED`) restent des objets racines, après
  les objets « Niveau ». `result.json` (TRC) inchangé.
- **Sortie `generic` : éléments sous chaque local qui les contient** : une porte entre deux locaux, ou un élément contenu dans plusieurs
  locaux, est répété sous chacun (même `id`, contenu identique : objet partagé accepté par le validateur de la plateforme) au lieu
  d'être rangé sous le premier seul ; les `ref` `room`, `room_from`, `room_to` restent identiques et chaque local pointe tous ses
  éléments par `contains`. Les contrôles `GenericEnvelopeChecks` et `validate_examples.py` suivent la règle de la plateforme (id répété
  accepté seulement si identique).
- **Sortie `generic` : groupes dans chaque local** : les enfants d'un local sont les groupes « Portes », « Équipements » puis « Terminaux »
  (`Category` = `Group`, `categorie_libelle` = libellé du groupe ; un groupe vide est omis), puis les anomalies du local. Enfants d'un
  groupe triés par nom (casse ignorée) puis UniqueId. Terminaux dans un 3e groupe distinct. Autres formats inchangés.
- **Sortie `generic` : niveaux à la racine** : l'arbre commence par un objet « Niveau » par niveau Revit ayant au moins un local exporté,
  trié par altitude croissante (propriété `altitude`, en m ; altitude inconnue après les autres, puis « Sans niveau » en dernier). Les locaux
  sont les enfants du niveau, en tri naturel du numéro (2 avant 10, casse ignorée, numéro vide en dernier, départage par ElementId).
  `Category` = `Level`, `categorie_libelle` = `Niveau`. Autres formats inchangés. Documentation dans la PR suivante de la série.
- **Journal : `roomsExported` exact** : en sortie `generic`, `roomsExported` (événement `EXTRACTION_SUMMARY`) compte les pièces présentes dans le
  résultat, sans celles des options de conception secondaires exclues ; `roomsCollected` garde le total lu. Même noms, schéma inchangé.
- **Liens : nom de fichier seul** : `details.linkPath` (et `linkName`) de `LINK_MISSING` / `LINK_NOT_LOADED` ne gardent que le nom du fichier,
  jamais un chemin de la machine d'origine (`result.json` et sortie `generic`) ; le message d'information `LINK_MISSING` ne contient que des noms.
- **Sortie `generic` : `LINK_MISSING` sans objet** : les liens Revit absents ne produisent plus d'objet « Anomalie » (`anomaly-N`) ;
  ils ne sont dits que par l'information unique `LINK_MISSING` de `warnings[]`. Le résultat reste `done` ; les autres anomalies sont inchangées.
- **Sortie `generic` : pas d'image pour les pièces exclues** : une pièce d'option de conception secondaire exclue
  (`includeSecondaryDesignOptions` : `false`) n'est plus photographiée : aucun fichier dans `Images/`, aucune propriété `image`, et
  `summary.images` ne compte que les pièces conservées. L'image d'une pièce conservée ne change pas. Pas de nouveau réglage.
- **Sortie `generic` : options de conception** : par défaut, seules les pièces hors option et de l'option principale sont exportées ;
  les pièces des options secondaires sont exclues avec une information chiffrée `DESIGN_OPTIONS_EXCLUDED`. Nouveau réglage
  `includeSecondaryDesignOptions` (`false` par défaut, `true` = tout exporter). Nouvelle propriété `option_conception` sur chaque pièce ;
  `variante` nettoyée (repère `<principal>` et chevrons retirés, espaces normalisés). Sans effet sur `result.json` ni le TRC.
  Voir [`docs/GENERIC.md`](docs/GENERIC.md) et [`docs/INPUTS.md`](docs/INPUTS.md).
- **Sortie `generic` : `warnings[]` accepte des informations** : un élément est un texte (avertissement) ou un objet
  `{ severity: "info"|"warning", message, code? }`. `LINK_MISSING` devient une seule information
  `{ severity: "info", code: "LINK_MISSING", message: "N lien(s) absent(s) : a, b, c" }` qui nomme tous les liens (avant, seul le premier
  était nommé) et ne rend plus l'enveloppe `partial`. Les objets d'anomalie restent. `contractVersion` reste `1.0` ; schéma
  `generic.schema.json` mis à jour. Demande la validation côté plateforme et l'affichage côté site. Voir [`docs/GENERIC.md`](docs/GENERIC.md).
- **Sortie `generic` : format** : nombres avec unité arrondis à 3 décimales, coordonnées de `box` à 1 décimale, paramètres Revit à
  12 chiffres significatifs (bruit de virgule flottante retiré, enveloppe plus petite) ; nouvelle propriété `categorie_libelle`
  (`Pièce`, `Porte`, `Équipement CVC`…) à côté de `Category`, qui garde sa valeur technique. La `box` reste en mm sans `unit` (la plateforme
  n'accepte `unit` que sur un nombre). Voir [`docs/GENERIC.md`](docs/GENERIC.md).
- **Design Automation : `outputFormat` vaut `generic` par défaut** quand `settings.json` ne le précise pas (absent ou `null`). La plateforme
  n'envoie que `schemaVersion` et `mainModelPath` et attend `result.generic.json` (sinon `RESULT_MISSING`). Un `outputFormat` explicite est
  toujours respecté ; `SettingsReader` sans option, les tests et l'outil local gardent `contract`. Voir [`docs/GENERIC.md`](docs/GENERIC.md).
- **Format TRC : portes, équipements et terminaux deviennent des objets BIM** (`trc.bimobject`) avec leurs propres propriétés, au lieu
  d'être listés sur la pièce (`b:porte_*`, `b:equipements_*` retirés de la pièce). Chaque objet porte `Category` (`Space`, `Door`,
  `<discipline> equipement`, `<discipline> terminal`) ; chaque élément porte `ListeSpaces`, une seule valeur : la liste JSON des `pk` des
  `trc.bimobject` des pièces qui le contiennent (une porte entre deux pièces est un seul objet, avec deux pièces dans la liste). Identification de l'élément (`a:identification data_id revit`, `_categorie`,
  `_famille`, `_type`, `_repere`, `_discipline`, `_lien`), passage libre des portes (`b:porte_largeur`, `b:porte_hauteur`) et paramètres de
  l'instance (`c:parametres revit_…`). Voir [`docs/TRC.md`](docs/TRC.md). Rupture pour les lecteurs du fichier TRC existant.
- WorkItemRunner : le tableau des résultats devient un **arbre** (pièce ▸ portes, équipements, terminaux), pour `TRCValidationDict.json`
  (rattachement par `ListeSpaces`, ancien `Space` encore lu) comme pour `result.json` (colonne `objet`) ; le fichier TRC est affiché en priorité ;
  flèche ▶ visible, double-clic sur une pièce, éléments sur fond gris ; tri des colonnes désactivé pour garder l'ordre de l'arbre.
- WorkItemRunner : le fichier TRC s'affiche en **arbre de tableaux** (maquette Figma) : tableau des pièces ; une pièce dépliée montre ses
  paramètres puis un sous-tableau par catégorie avec ses propres colonnes (Portes, Équipements CVC, Terminaux électricité…) ; chaque ligne se
  déplie sur ses paramètres Revit ; « Tout déplier / replier » et filtres par catégorie et pour les paramètres.
- **Tous les paramètres** (`roomParameters: "all"`) : l'extracteur lit tous les paramètres d'occurrence (`Element.Parameters`, y compris ceux
  masqués dans la palette Propriétés, et non plus `GetOrderedParameters`) et, pour les portes, équipements et terminaux, **tous les paramètres
  de type** (`parametres_type` dans `result.json`, `d:parametres type_<nom>` dans le fichier TRC, après les paramètres d'occurrence). Un
  paramètre sans valeur est écrit vide dans le fichier TRC, au lieu d'être omis. Non validé dans Design Automation.
  Valeurs **telles qu'affichées par Revit** pour les paramètres ElementId (nom de la catégorie, du niveau, de la phase, de l'hôte… au lieu de
  l'identifiant) et pour les entiers à libellé (Oui / Non, listes de choix) ; longueurs, surfaces et volumes restent en SI. Un paramètre de
  même nom et de même valeur qu'un autre (les deux « Catégorie » de Revit) n'est exporté qu'une fois.
- WorkItemRunner (maquette Figma v2) : les paramètres ne sont plus dans une fiche Paramètre / Valeur mais **en colonnes** de chaque tableau,
  sous un en-tête à bandes Identification / Paramètres d'occurrence / Paramètres de type ; filtres Occurrence et Type ; défilement horizontal.
- Livrable : `TRCValidationDict.json` seul. L'exemple `docs/examples/settings.example.json` (bouton « Exemple » de WorkItemRunner) règle
  `"outputFormat": "trc"` et `elements` (portes, équipements, terminaux).
- **Clés du JSON en français** (contrat `result.json`, noms des champs de `rooms[]` et de leurs sous-objets) : `id_revit`, `unique_id`, `nom`,
  `numero`, `surface_m2`, `hauteur_non_limitee_m`, `niveau`, `occupation`, `volume_m3`, `statut_volume`, `finitions` (`sol`, `murs`, `plafond`,
  `plinthe`), `phase`, `variante`, `parametres_partages` (`guid`, `valeur`), `indicateurs_qualite`, `parametres`, `hauteurs` (`renseignee_m`,
  `mesuree_min_m`, `effective_m`, `source_effective`, `rayons`, `categorie_limitante`, `modele_limitant`, `par_categorie_m`), `portes`,
  `equipements`, `terminaux` (`categorie`, `famille`, `type`, `repere`, `numero`, `niveau`, `relation`, `discipline`, `nom_du_lien`,
  `largeur_m`, `hauteur_m`, `source_dimensions`, `parametres`). Le haut du fichier (`schemaVersion`, `summary`, `rooms`, `anomalies`), les
  `elementId` des anomalies et les codes du fichier TRC ne changent pas. Schéma, exemples, recette R01–R12 et comparateur migrés.
  WorkItemRunner : le tableau affiche **exactement les clés du JSON** (en-tête = chemin de la clé, ex. `finitions.sol`) ; plus de libellés
  séparés. Rupture pour les lecteurs de `result.json` existants.

### Ajouté

- Contrat `result.json` **1.4** (additif, rien ne change sans nouvelle option) : `settings.json` accepte `roomParameters` (`"none"` par défaut, ou `"all"`) et `elements` (sous-ensemble de `doors`, `equipment`, `terminals`). Avec `roomParameters: "all"`, chaque pièce reçoit `parameters` (tous ses paramètres : nom, paramètre natif, GUID, stockage, valeur typée, valeur affichée). Avec `elements`, chaque pièce reçoit un tableau par famille demandée : les portes via `FromRoom`/`ToRoom` (une porte entre deux pièces figure dans les deux, relation `from` ou `to`, largeur et hauteur en mètres), les équipements (mécaniques et électriques) et les terminaux d'air via `FamilyInstance.Room` puis `GetRoomAtPoint`. Services `DoorService`, `EquipmentService` et `TerminalService` (`src/Revit`), derrière `IRoomElementService`. Nouveaux codes document `ELEMENT_WITHOUT_ROOM` (info) et `ELEMENTS_COLLECTION_FAILED` (warning, l'extraction des pièces continue), événements de log `ELEMENTS_COLLECTED`, `ELEMENTS_COLLECTION_FAILED` et `ROOM_PARAMETERS_FAILED`. Voir [`docs/ELEMENTS.md`](docs/ELEMENTS.md). Portes : largeur et hauteur de **passage libre** mesurées sur la géométrie (lignes de coupe dans les axes de la porte, médiane, `dimensionSource`), sans dépendre des paramètres de la famille ; numéro de porte (`number`, `DOOR_NUMBER`). Catégories par défaut étendues à toutes les disciplines (équipements : mécanique, électricité, plomberie, spécialisés, protection incendie ; terminaux : CVC, plomberie, électricité courants forts et faibles) et remplaçables par `elementCategories` dans `settings.json`. Non validé sur un vrai Revit : à confirmer en recette (EGR-14).
- WorkItemRunner : le tableau des pièces génère ses colonnes d'après les clés de `rooms[]` du résultat (objets aplatis, références montrées par leur nom, tableaux d'éléments résumés) ; la dernière configuration (version Revit, alias, ZIP, `settings.json`) est rappelée au démarrage.

- Préparation de la recette EGR-18 / EGR-14 (#44) : matrice des maquettes de référence R01–R12 (construites en Revit 2025, mises à niveau pour 2026 et 2027 ; R09 non applicable en 2027), `settings.json` et `result.json` attendus (`docs/recette/`), fiche de construction des maquettes, comparateur `tools/recette/compare_result.py` (tolérances D5, champs volatils ignorés, `bind` des identifiants réels) et ses tests ; tests .NET de cohérence des attendus avec le code ; `validate_examples.py` valide aussi les attendus ([`docs/RECETTE.md`](docs/RECETTE.md)).
- Règle `DUPLICATE_NUMBER` par contexte (phase, variante) décidée en EGR-18 (seules les variantes différentes d'un même jeu d'options sont exclues ; jeux différents comparés) et reflétée dans les attendus de recette ; implémentée (#45) : modèle principal comparé à toute la phase, `elementIds` propre à chaque pièce ; jeu d'options illisible → détail `optionSetUnknown: true` et log `DUPLICATE_OPTION_SET_UNKNOWN`. Tests de rejeu de la recette réactivés (R01, R02, R06, R11).

- Sortie de l'Activity au format **ZIP** : le paramètre `result` est le dossier `result/` zippé (`zip: true`) et contient `result.json`
  et, avec le nouveau réglage `roomImages`, le dossier `Images/` : une **vue 3D par pièce** (méthode de l'application TRC :
  caméra de coin, section box, murs et sols transparents), en JPEG de largeur `imageWidthPx` (8000 par défaut, comme TRC), recadrée sur son contenu comme dans TRC, nommée selon la
  codification TRC `{UniqueId}_{code_file}_{ElementId}_AdjustedCornerView_1.jpg`. Bilan `summary.images`, anomalie `IMAGE_EXPORT_FAILED`
  (warning), événements `ROOM_IMAGES_EXPORTED` et `ROOM_IMAGES_FAILED`. WorkItemRunner décompresse la sortie. Voir
  [`docs/IMAGES.md`](docs/IMAGES.md). Non validé dans Design Automation (`ExportImage`).

- **Hauteurs des pièces** (`roomHeights`) : hauteur renseignée (`ROOM_HEIGHT`) et hauteur minimale **mesurée par lancer de rayons**
  séparées dans `rooms[].heights`, avec hauteur effective (mesurée, à défaut renseignée), objet limitant (catégorie et document, modèle
  principal ou lien) et minimum par catégorie. Méthode de `GetRoomMinHeightByRaytracing` de TRC, avec une seule vue 3D temporaire pour
  tout le document. Bilan `summary.heights`, anomalie `HEIGHT_MEASUREMENT_FAILED`. Voir [`docs/HEIGHTS.md`](docs/HEIGHTS.md). Non validé sur un
  vrai Revit.

- **Équipements et terminaux par discipline, avec les modèles liés** : chaque élément porte une `discipline` (`cvc`, `plomberie`,
  `electricite`, `autres`) d'après sa catégorie Revit, et les terminaux suivent la même répartition que les équipements. Les modèles
  liés sont parcourus (`linkedElements`, vrai par défaut) avec la **transformation totale du lien** appliquée (TRC la négligeait) ;
  `linkName` donne le modèle d'origine. **Une seule pièce par élément** : point d'insertion, sinon plus grand volume commun. Voir
  [`docs/ELEMENTS.md`](docs/ELEMENTS.md). Non validé sur un vrai Revit.

- **Sortie au format TRC** (`outputFormat`: `contract` par défaut, `trc` ou `both`) : `TRCValidationDict.json`, fiches `trc.bimobject` et
  `trc.bimobjectpropertyvalue` + `special.setcodefile`, **données seules** (ni spécification, ni statut, ni conformité), codes de l'application
  TRC (`b:porte_largeur`, `b:dimensions_hauteur`, `a:identification data_…`, `bim_object_username`…), nouveaux codes par discipline
  (`b:equipements cvc_quantite`…), **`pk` stables**, annexes au-delà de 255 caractères. WorkItemRunner affiche le fichier. Voir
  [`docs/TRC.md`](docs/TRC.md).
- **Activity en français** : la ligne de commande ajoute `/l FRA` (comme l'Activity de TRC) : noms de catégories et de paramètres en français.

- **Paramètres de la pièce à plat** : `rooms[].parameters` (et `parameters` des éléments) est maintenant un dictionnaire `{ nom : valeur }`, comme
  `areaM2` ou `volumeM3`, et non plus une liste d'objets (nom, GUID, type…) : chaque paramètre est une colonne du tableau de résultats. Valeurs
  typées, SI pour longueur, surface, volume et nombre, valeur affichée pour les autres grandeurs, tri par nom, doublons suffixés. Format TRC :
  `b:dimensions_volume` quand le volume est calculé. Format TRC avec `roomParameters: "all"` : un `trc.bimobjectpropertyvalue` par paramètre de la pièce,
  code `c:parametres revit_<nom en minuscules>` (section `C` : après `a:` et `b:`).

### Corrigé

- WorkItemRunner : le ZIP de maquette restait verrouillé après la vérification des entrées (flux jamais refermé), ce qui empêchait de le recréer tant que l'outil était ouvert et faisait échouer 11 tests sous Windows.

### Modifié

- Cohérence doc/code : `summary.correlationId` = identifiant du WorkItem (`DAS_WORKITEM_ID`, repli `run-<guid>`), `summary.apsEngine` = `EngineId` du fichier `<id>_job.das` (null sinon), sans changement de schéma ; exemple `result.json` aligné sur la version unique `0.1.0` de `Directory.Build.props` (test de non-régression) ; `DOCUMENT_UPGRADE_IN_MEMORY` et `LINKED_DOCUMENT_FAILURES` déplacés dans `LogEventCodes`.
- Spécifications v0.3 : sections 9 et 10 alignées sur le code et le schéma 1.3 (codes, sévérités, détails, champs de `summary`, événements de log, variantes), statut des décisions D1 à D9 ; README, backlog (EGR-19 #35, EGR-20 #36, état des cartes) et ce CHANGELOG.

## Contrat `result.json` 1.3 — 9 octobre 2026

### Ajouté

- `volumeMode: compute` (EGR-16, #40) : calcul temporaire des volumes dans une transaction sur la copie de travail (`ComputeVolumes = true`, `Document.Regenerate()`), lecture des volumes avant `RollBack()`, RVT jamais sauvegardé ; échec d'activation → `notAvailable` et anomalie `VOLUME_COMPUTE_FAILED` (warning) ; échec de régénération → `DOCUMENT_CORRUPTED`.
- Schéma 1.3 : `summary.volumeComputation` (`documentComputeVolumes`, `computeApplied`, `transactionRolledBack`) et code document `VOLUME_COMPUTE_FAILED` (#40).
- Événements de log `VOLUME_COMPUTATION` et `VOLUME_COMPUTE_FAILED` (#40).
- Documentation de publication, recette, promotion et retour arrière (`docs/PUBLISHING.md`) et workflow CI GitHub : build, tests .NET et Python, validation des exemples, construction contrôlée des trois AppBundles, sans secret ni déploiement (EGR-15, #39 ; activé dans `.github/workflows/ci.yml` par a4accb0).

### Supprimé

- Refus provisoire de `volumeMode: compute` en `CONFIG_INVALID` (introduit par #31, retiré par #40).

## Contrat `result.json` 1.2 — 9 octobre 2026

### Ajouté

- Paramètres partagés numériques : longueur/surface/volume convertis en m/m²/m³, nombre sans unité tel quel, autres types en `null` avec l'anomalie document `UNSUPPORTED_PARAMETER_TYPE` (info, une fois par GUID) ; schéma 1.2 (#37).
- Indicateurs qualité et statut final (EGR-11, #38) : `QualityCheckService` (`ZERO_AREA`, `DUPLICATE_NUMBER` par numéro et phase, pièces sans phase groupées, `VOLUME_NOT_COMPUTED`), ordre des anomalies, statut 9.3 ; tests de spécification réactivés (`docs/QUALITE.md`).
- Packaging des AppBundles et Activities Revit 2025–2027 : manifestes, définitions par version (Activity sans `/i`), script d'assemblage et de contrôle des ZIP, script de publication en dry-run par défaut ; repli `result.json` minimal après `EXPORT_FAILED` ; D6 documentée « à tester » (EGR-13, #34).
- Finitions (`ROOM_FINISH_*`) et paramètres partagés lus par GUID, avec `SHARED_PARAMETER_NOT_FOUND` (EGR-10, #33).
- Export de `result.json` : sérialisation déterministe en mémoire, fichier temporaire puis renommage atomique, UTF-8 sans BOM, fins de ligne `\n` (EGR-12, #30).
- Quantités SI (`areaM2`, `unboundedHeightM`, `volumeM3` par `UnitUtils`, sans arrondi) et `volumeStatus` en mode `preserve` (EGR-09, #29).
- Identité, nom, numéro, niveau (`LEVEL_NOT_FOUND`), occupation, phase et variante des pièces (EGR-08, #28).
- Validation de `settings.json` et du ZIP, résolution confinée du RVT principal (`CONFIG_INVALID`, `CONFIG_SCHEMA_UNSUPPORTED`, `PATH_OUTSIDE_INPUT`, `RVT_NOT_FOUND`, `RVT_AMBIGUOUS`) ; D4 tranchée (EGR-05, #27) ; contraintes du ZIP sur les machines APS : 260 caractères, noms non ASCII (#32).

### Modifié

- Phases typées (`PhaseElementIds`), `ZERO_AREA` = surface nulle sans `UNPLACED`, suppression de `RoomDto.IsPlaced` (#31).

## Contrat `result.json` 1.1 — 9 octobre 2026

### Ajouté

- Liens Revit déchargés signalés par l'anomalie document `LINK_NOT_LOADED` (info) ; schéma 1.1 (EGR-06, #26).
- Tests unitaires du cœur indépendant de Revit : schéma, contrats, orchestration, unités, export (EGR-17, #25).
- Collecte de toutes les pièces (`OST_Rooms`), filtre de phase, compteurs de collecte (`ROOM_COLLECTION_DETAILS`) (EGR-07, #24).
- Accès au document en Automation : ouverture explicite, détachement des maquettes collaboratives, politique stricte des erreurs (`REVIT_WARNING_ON_OPEN`, `DOCUMENT_CORRUPTED`), liens manquants (`LINK_MISSING`), contrôle de version (`REVIT_VERSION_INCOMPATIBLE`), fermeture sans sauvegarde ; D1 et D2 tranchées (EGR-06, #23).

## Contrat `result.json` 1.0 — 9 octobre 2026

### Ajouté

- Suivi de revue EGR-03/04 : filet de sécurité par pièce (`EXTRACTION_ERROR`, `ROOM_EXTRACTION_FAILED`), lignes de log ASCII, masquage des secrets affiné, étape `logging` (#22).
- Service de logs JSON Lines sur la console avec corrélation, niveaux, limitation par code et masquage des secrets ; D8 tranchée : console uniquement (EGR-04, #21).
- Solution C# organisée par services (Contracts, Logging, Export, Application, Revit, EntryPoint, Tests), cibles par `RevitVersion` : 2025 et 2026 en `net8.0-windows`, 2027 en `net10.0-windows` (EGR-03, #20).
- Spécifications v0.2 : sortie unique `result.json`, schéma JSON 1.0 (draft 2020-12), exemples, codes et statuts, décisions ouvertes (#19).

## Versions du contrat `result.json`

| Version | Changement | PR |
| --- | --- | --- |
| 1.0 | Contrat initial : `summary`, `rooms`, `anomalies` | #19 |
| 1.1 | Code document `LINK_NOT_LOADED` | #26 |
| 1.2 | Code document `UNSUPPORTED_PARAMETER_TYPE` | #37 |
| 1.3 | `summary.volumeComputation` et code document `VOLUME_COMPUTE_FAILED` | #40 |
