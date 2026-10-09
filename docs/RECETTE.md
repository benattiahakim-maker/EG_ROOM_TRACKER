# Recette : maquettes de référence, résultats attendus et comparaison (EGR-18 / EGR-14)

Ce document décrit le jeu de recette de la section 8 des [spécifications](SPECIFICATIONS.md). Il a été préparé sans
Revit (EGR-18) et sera exécuté sur les engines 2025, 2026 et 2027 (EGR-14).

| Élément | Emplacement |
| --- | --- |
| Manifeste des cas (maquette, contenu du ZIP, settings, attendu, années, chemins « à confirmer ») | [`docs/recette/cases.json`](recette/cases.json) |
| `settings.json` de chaque cas | [`docs/recette/settings/`](recette/settings/) |
| `result.json` attendus (gabarits, identifiants provisoires) | [`docs/recette/expected/`](recette/expected/) |
| Fichier de paramètres partagés (GUID fixes) | [`docs/recette/EGR_Recette_SharedParameters.txt`](recette/EGR_Recette_SharedParameters.txt) |
| Construction des maquettes dans Revit, pas à pas | [`docs/recette/FICHE_MAQUETTES.md`](recette/FICHE_MAQUETTES.md) |
| Outil de comparaison et ses tests | [`tools/recette/compare_result.py`](../tools/recette/compare_result.py), [`tools/recette/tests/`](../tools/recette/tests/) |
| Tests .NET du jeu (schéma, contrats, contrôle qualité, messages) | [`src/Tests/Recette/RecetteFixturesTests.cs`](../src/Tests/Recette/RecetteFixturesTests.cs) |

Les maquettes `.rvt` ne sont pas versionnées dans le dépôt. Il faut les construire avec la fiche, une fois par année
(ou en 2025 puis les mettre à niveau, voir la fiche §0), et les conserver dans l'espace partagé du projet.

## 1. Principe

1. Les résultats attendus décrivent ce que **le code actuel de `main`** doit produire (schéma `1.2`, messages,
   ordre des anomalies, indicateurs, statut). Les tests .NET le vérifient sans Revit (§6).
2. Les identifiants Revit (`elementId`, `uniqueId` des pièces, niveaux, phases et variantes) ne sont connus qu'une
   fois les maquettes construites. Les gabarits portent donc des identifiants provisoires : `9000101`…`9000113` et
   `EGR18-P01`… pour les pièces, `9000001/2` pour les niveaux, `9000011/12` pour les phases, `9000021/22` pour les
   variantes. Au premier WorkItem de chaque année, `compare_result.py bind` recopie **uniquement ces identifiants**
   depuis le résultat réel. Il produit `docs/recette/expected/<année>/<cas>.json`, que le porteur relit puis
   versionne. Les comparaisons suivantes se font strictement par `elementId`.
3. Les comportements Revit que la documentation ne confirme pas sont listés en « chemins à confirmer » (`pending`)
   dans le manifeste. Un écart sur ces chemins est affiché `À CONFIRMER` sans faire échouer. Une fois le point
   tranché (§5), on corrige l'attendu ou le code, puis on retire le chemin du manifeste.

## 2. Matrice des cas

Toutes les maquettes et tous les ZIP sont construits pour chaque année (2025, 2026, 2027), sauf R09 (voir la note).
Le `summary.revit.version` attendu est celui de l'engine (posé par `bind --year`).

| Id | Maquette (ZIP) | settings | Résultat attendu | Années |
| --- | --- | --- | --- | --- |
| R01 | `EGR_Ref.rvt` | 8 paramètres partagés (texte, entier, nombre, longueur, surface, volume, oui/non, angle), toutes phases, `preserve` | `succeededWithWarnings` ; 13 pièces, 5 avec indicateur ; `UNSUPPORTED_PARAMETER_TYPE` (angle), `VOLUME_NOT_COMPUTED` (13) ; `UNPLACED` + `DUPLICATE_NUMBER` sur Réserve, `DUPLICATE_NUMBER` sur Bureau d'étude et les deux variantes, `ZERO_AREA` sur Couloir | 2025-2027 |
| R02 | `EGR_Ref_Volumes.rvt` (volumes activés) | aucun paramètre partagé | `succeededWithWarnings` ; volumes = surface × 2,70 ; Réserve `null`/`notAvailable` ; Couloir `0`/`computed` ; pas d'anomalie document | 2025-2027 |
| R03 | `EGR_Ref.rvt` | `phaseIds` = phase Existant | `succeeded` ; 1 pièce (Archives existantes, n° 001, sans doublon) ; `VOLUME_NOT_COMPUTED` (1) | 2025-2027 |
| R04 | `EGR_Ref.rvt` | `phaseIds` = `999999999` | `failed`, `CONFIG_INVALID` étape `collection`, `source` renseignée | 2025-2027 |
| R05 | `Projet/EGR_Liens.rvt` + `Projet/Liens/EGR_Lien_A.rvt` + `Projet/Liens/EGR_Lien_C.rvt` | `mainModelPath` | `succeededWithWarnings` ; Hall ; `LINK_MISSING` (Lien B absent du ZIP), `LINK_NOT_LOADED` (Lien C déchargé, `Unloaded`), `VOLUME_NOT_COMPUTED` | 2025-2027 |
| R06 | `EGR_Ref.rvt` | GUID inconnu du document + paramètre lié aux murs seulement | `succeededWithWarnings` ; `SHARED_PARAMETER_NOT_FOUND` (inconnu), `MISSING_PARAMETER` (murSeulement) sur les 13 pièces | 2025-2027 |
| R07 | `EGR_Collab.rvt` (central, 2 worksets) | défaut | `succeeded` ; `isWorkshared: true` ; 2 pièces, dont celle du workset fermé | 2025-2027 |
| R08 | `EGR_Ref.rvt` + `EGR_Ref_Volumes.rvt` à la racine | sans `mainModelPath` | `failed`, `RVT_AMBIGUOUS` étape `input`, `source: null`, options par défaut | 2025-2027 |
| R09 | `EGR_Futur.rvt` (enregistré avec l'année + 1) | défaut | `failed`, `REVIT_VERSION_INCOMPATIBLE` étape `document`, `source: null` | 2025, 2026 |
| R10 | `EGR_Ref.rvt` | `schemaVersion: "2.0"` | `failed`, `CONFIG_SCHEMA_UNSUPPORTED` étape `configuration` ; sert aussi au test D6 | 2025-2027 |

Notes :

- R03 : avant l'exécution, remplacer `9000011` dans `settings/R03.json` par l'`ElementId` réel de la phase Existant
  (affiché par `bind` sur R01). Cette valeur est propre à chaque année : garder un fichier par année au besoin.
- R09 : un RVT 2027 sur l'engine 2026, un RVT 2026 sur l'engine 2025. Le message attendu cite 2027/2026, mais les
  messages ne sont pas comparés par défaut. Sans objet en 2027 tant qu'aucune version postérieure n'existe.
- R01-R03 et R05-R07 admettent un `REVIT_WARNING_ON_OPEN` supplémentaire (`allowExtraAnomalies`) : à l'ouverture,
  Revit peut émettre des avertissements que l'on ne sait pas prévoir. Ils sont affichés en `NOTE` et doivent être
  relus.

### Pièces de `EGR_Ref.rvt`

Surfaces exactes par construction (lignes de séparation de pièce, voir la fiche). Limite supérieure : le niveau de
la pièce, avec un décalage de 2,70 m (hauteur non bornée 2,70 m). Les champs non cités sont vides (`null`).

| # | Nom | Numéro | Surface (m²) | Niveau / phase / variante | Occupation | Finitions sol / mur / plafond / plinthe | Paramètres partagés |
| --- | --- | --- | --- | --- | --- | --- | --- |
| P01 | Bureau d'étude | 001 | 20 | R+0 / Nouvelle construction | Bureau | Parquet chêne / Peinture blanche / Faux plafond 60×60 / Plinthe bois | texte `A-001`, entier 42, nombre 3,5, longueur 1,25 m, surface 12,5 m², volume 2,4 m³, oui/non coché |
| P02 | Salle de réunion « Été » | 0010 | 12 | idem | Réunion | Moquette / Enduit à la chaux / Dalles acoustiques / Plinthe carrelage | texte `Œuvre-ÀÉÎÕÜ-ç-ß`, toutes les valeurs numériques à 0, oui/non décoché |
| P03 | Local technique | 002 | 6 | idem | (vide) | Résine / **`""`** (saisi puis effacé) / Brut / jamais saisi | texte `""` (saisi puis effacé) |
| P04 | Réserve | 001 | non placée (0) | idem | Stockage | — | texte `R-001` |
| P05 | Couloir | 003 | non fermée (0) | idem | Circulation | — | — |
| P06 | Archives | 1 | 4 | idem | Archives | — | — |
| P07 | Dégagement | (vide) | 3 | idem | — | — | — |
| P08 | Placard | (vide) | 1 | idem | — | — | — |
| P09 | Sans attributs | 004 | 5 | idem | — | — | — |
| P10 | Archives existantes | 001 | 9 | R+0 / **Existant** | — | — | — |
| P11 | Bureau variante A | 010 | 7,5 | R+0 / Nouvelle construction / **Option A** (principale) | — | — | — |
| P12 | Bureau variante B | 010 | 7,5 | idem / **Option B** | — | — | — |
| P13 | Combles aménagés | 101 | 20 | **R+1** / Nouvelle construction | — | — | — |

Doublons attendus (même phase, numéro identique, numéros vides ignorés) : `001` (P01, P04 non placée), `010` (P11,
P12, variantes exclusives : voir la question ouverte Q1). P10 (`001`, phase Existant) n'est pas un doublon. P07/P08
(numéros vides) ne le sont pas non plus.

## 3. Couverture de la section 8

| Cas de la section 8 | Couvert par |
| --- | --- |
| Pièces renseignées | R01 P01, P02 ; R02 |
| Accents / zéros initiaux | P02 (nom, texte partagé, finitions), P01 `001`, P02 `0010` vs P06 `1` ; noms accentués des ZIP (R05 « Liens », fiche §6) |
| Champs vides (null vs `""`) | P03 (finition murale et texte partagé saisis puis effacés, plinthe jamais saisie), P09 (aucun attribut), P02 (valeurs numériques à 0 ≠ null) |
| Non placées / surface nulle | P04 (non placée : `UNPLACED`, surface 0, pas de `ZERO_AREA`), P05 (non fermée : `ZERO_AREA`) |
| Doublons de numéro | P01/P04 ; P11/P12 ; numéros vides P07/P08 non comptés ; P10 hors phase |
| Plusieurs phases et variantes | P10 (Existant), R03 (filtre de phase), R04 (phase inconnue), P11/P12 (variantes) |
| Calcul de volume désactivé / activé | R01 (`notComputed` + `VOLUME_NOT_COMPUTED`) / R02 (`computed`, `notAvailable` pour la non placée) |
| Finitions | P01, P02, P03 |
| Paramètres partagés de chaque type de stockage | R01 : texte (String), entier (Integer), nombre / longueur / surface / volume (Double, mesurables), oui/non (Integer), angle (Double non pris en charge : `UNSUPPORTED_PARAMETER_TYPE`) ; R06 : GUID inconnu, paramètre absent de la pièce |
| Liens manquants / non chargés | R05 (`LINK_MISSING`, `LINK_NOT_LOADED`) |
| Maquette collaborative | R07 |
| ZIP ambigu | R08 |
| RVT incompatible | R09 |
| Échec en configuration, comportement de sortie en échec (D6) | R10 |

Cas non reproductibles par une maquette, couverts par les tests unitaires : `LEVEL_NOT_FOUND`, `EXTRACTION_ERROR`,
`MISSING_PARAMETER` d'un paramètre natif, `DOCUMENT_CORRUPTED`, `EXPORT_FAILED`, `INPUT_NOT_FOUND`, `volumeMode:
compute` refusé. Le stockage `ElementId` d'un paramètre partagé n'existe pas dans la boîte de dialogue « Paramètres
partagés » (les types Matériau/URL/Devise non plus ne sont pas testés) : hors jeu.

## 4. Déroulement (EGR-14)

Pour chaque année :

1. Construire les maquettes ([fiche](recette/FICHE_MAQUETTES.md)) et les ZIP exactement comme le champ `zip` du
   manifeste (chemins relatifs à la racine du ZIP).
2. Publier en test (`deploy.py --apply test`, [PUBLISHING.md](PUBLISHING.md) §5), puis lancer un WorkItem par cas
   sur `<nickname>.ExtractRooms_R<année>+test`, avec le ZIP et `docs/recette/settings/<cas>.json`. Télécharger
   `result.json` et le rapport (valable 24 h).
3. Première exécution de l'année : lier les gabarits, puis relire le journal (pièces appariées, phases, variantes).

   ```bash
   python3 tools/recette/compare_result.py bind docs/recette/expected/R01.json resultats/2026/R01.json \
       --year 2026 -o docs/recette/expected/2026/R01.json
   ```

   `bind` échoue si une pièce du gabarit est introuvable ou ambiguë (nom, numéro, phase, variante), ou s'il reste
   un identifiant provisoire. Les valeurs attendues ne sont jamais recopiées depuis le résultat réel.
4. Comparer :

   ```bash
   python3 tools/recette/compare_result.py compare docs/recette/expected/2026/R01.json resultats/2026/R01.json --case R01
   ```

   `--case` applique le manifeste (champs ignorés, chemins à confirmer, anomalies admises). Code de sortie : 0
   conforme, 1 écart(s), 2 erreur d'usage ou de fichier. Sortie : une ligne par écart (`ÉCART`), par point à
   confirmer (`À CONFIRMER`) ou par information (`NOTE`), puis le bilan.
5. Reporter le résultat dans la matrice signée d'EGR-14 (cas × année : conforme / écart / à confirmer, identifiant
   du WorkItem, temps d'exécution et taille pour D9), en ajoutant les contrôles manuels : source inchangée, aucun
   dialogue, log récupérable.

### Règles du comparateur

| Règle | Défaut | Option |
| --- | --- | --- |
| Appariement des pièces | `elementId` | `--match-by uniqueId` |
| Tolérances D5 | 0,001 m (`unboundedHeightM`), 0,001 m² (`areaM2`), 0,001 m³ (`volumeM3`), 0,001 (valeurs numériques des paramètres partagés) | `--tol-length`, `--tol-area`, `--tol-volume`, `--tol-shared`, ou `tolerances` du manifeste |
| Autres nombres (compteurs, entiers) | exacts | — |
| Types | `null` ≠ `""` ≠ `0` ; 20 = 20,0 | — |
| Champs ignorés | `summary.correlationId`, `extractorVersion`, `revit.build`, `apsEngine`, `startedAtUtc`, `completedAtUtc` | `--ignore`, `--no-default-ignores` |
| Anomalies | multiensemble (code, portée, `elementId`, discriminant `linkName`/`key`/`parameter`/`number`), ordre ignoré, `details.elementIds` sans ordre, messages ignorés | `--compare-messages`, `--allow-extra-anomaly` |
| `qualityFlags` | sans ordre | — |
| Ordre des pièces | vérifié (tri par `elementId`) | — |
| Erreurs globales | code et étape | `--compare-messages` |

Choix d'un script Python plutôt que d'un helper `dotnet test` : la comparaison se fait sur le poste du porteur,
à partir de fichiers téléchargés, sans SDK .NET ni dépendance (bibliothèque standard uniquement). C'est cohérent
avec la CI et l'outillage Python existants. Il est placé dans `tools/recette`, hors de `src/Packaging/scripts`. La
cohérence des attendus avec le code C# est vérifiée de son côté par les tests .NET (§6).

## 5. Points « à confirmer en EGR-18 » et comment la recette les tranche

Recensement : commentaires et documents du dépôt (`LECTURE.md`, `COLLECTE.md`, `DOCUMENT.md`, `INPUTS.md`,
`BUILD.md`, `src/Packaging/README.md`, `SPECIFICATIONS.md` §8 et §11, tests en attente), et sections « Vérification
doc » des PR #29, #31, #33, #37 et #39.

| # | Point | Source | Comment la recette le confirme |
| --- | --- | --- | --- |
| C1 | `Parameter.HasValue` vs chaîne vide : une chaîne saisie puis effacée est-elle `""` ou « sans valeur » (`null`) ? | LECTURE.md « Limite connue », PR #31 | P03 `finishes.wall` et `sharedParameters.texte` (pending R01/R02). Si Revit rend `null`, documenter la limite comme définitive et passer l'attendu à `null`. |
| C2 | `Room.Location` null pour une pièce non placée ; surface, niveau et hauteur d'une non placée | PR #29/#31, Guide API « Rooms » | P04 : `UNPLACED` attendu (bloquant). Surface 0, niveau R+0, hauteur 2,70 en pending. |
| C3 | Pièce non fermée : `Location` non null, surface 0, volume 0 ou erreur ? | aide « Room Is Not Enclosed » | P05 : `ZERO_AREA` attendu ; hauteur (R01) et volume (R02) en pending. |
| C4 | Types mesurables : longueur/surface/volume convertis en m/m²/m³, nombre sans unité, angle refusé | PR #33/#37 | R01 P01 (1,25 / 12,5 / 2,4 / 3,5) et P02 (0) ; `UNSUPPORTED_PARAMETER_TYPE` pour l'angle. |
| C5 | Format de `ForgeTypeId.TypeId` (ex. `autodesk.spec.aec:angle-2.0.0`) | PR #37, sources non officielles | Détail `typeId` de R01 en pending : relever la valeur réelle par année et mettre l'attendu à jour. |
| C6 | Oui/non : stockage entier 1/0 ; valeur d'un oui/non jamais touché (`HasValue`) | PR #33 | P01 = 1, P02 = 0 (bloquants) ; autres pièces en pending (`null` attendu). |
| C7 | Paramètre lié à une autre catégorie : absent de la pièce → `MISSING_PARAMETER` ; GUID inconnu → `SHARED_PARAMETER_NOT_FOUND` | PR #33 | R06. |
| C8 | `ROOM_PHASE_ID` et nom de phase ; filtre `phaseIds` | PR #31, COLLECTE.md | R01 (phases des pièces), R03, R04. |
| C9 | Pièces des variantes secondaires présentes dans le collecteur ; nom de variante (avec ou sans « (primary) ») | COLLECTE.md | P11/P12 : présence bloquante, `designOption.name` en pending. |
| C10 | Nombre de pièces conforme sur les trois engines | COLLECTE.md, TRELLO EGR-07 | `counts` et liste des pièces de chaque cas. |
| C11 | `ROOM_FINISH_*` et occupation : paramètres texte | PR #31 | P01-P03. |
| C12 | Numéro vide accepté par Revit (sinon renumérotation automatique) | — | P07/P08 `number` en pending. |
| C13 | Calcul des volumes : désactivé → `notComputed` et `VOLUME_NOT_COMPUTED` ; activé → `computed` | Guide API « Rooms », `AreaVolumeSettings.ComputeVolumes` | R01 / R02. Volume de la non placée (`notAvailable`) et de la non fermée (pending). |
| C14 | Tolérances D5 (0,001 m, m², m³) | SPECIFICATIONS §8, D5 ; LECTURE.md | R02 : surfaces et volumes exacts par construction (rectangles cotés, hauteur 2,70). Un écart > 0,001 indique soit une erreur de construction, soit une conversion à revoir. Relever l'écart maximal observé par année pour fixer D5. |
| C15 | D7, précision de sérialisation (valeur brute convertie) | SPECIFICATIONS D7, UnitConversionTests | Relever dans R02 la représentation réelle (ex. `20.000000000000004`) : elle reste dans la tolérance. |
| C16 | Liens : nom (`linkName`), chemin (`linkPath`), statut `Unloaded` ; liens DWG ignorés | INPUTS.md, PR #29 | R05 : présence des deux anomalies bloquante ; détails en pending. |
| C17 | Résolution des liens en chemin relatif après décompression | DOCUMENT.md, INPUTS.md | R05 : le Lien A (présent) ne doit produire aucune anomalie. |
| C18 | Noms accentués et sensibilité à la casse dans le ZIP | INPUTS.md | Fiche §6 : ZIP UTF-8 avec dossier `Liens` ; variante facultative R05 avec un lien accentué. |
| C19 | Maquette collaborative : détachement avec conservation des worksets, tous les worksets ouverts, aucune écriture du central | DOCUMENT.md, D1 | R07 : `isWorkshared: true`, les deux pièces exportées (y compris celle du workset fermé), source inchangée. |
| C20 | `REVIT_WARNING_ON_OPEN` : avertissements réels à l'ouverture | DOCUMENT.md, D2 | Anomalie admise et affichée en `NOTE` : relever les codes rencontrés. |
| C21 | D6 : `result.json` téléversé quand l'add-in met `Succeeded = false` ? | src/Packaging/README.md §D6 | R10 (et R04, R08, R09) : relever le statut du WorkItem et la présence de `result.json`. |
| C22 | `DesignAutomationBridge.dll` dans le bundle, exclusion des `*.deps.json`, `localName: input` décompressé | src/Packaging/README.md, BUILD.md | Tout WorkItem réussi (R01) le confirme. Un échec de chargement ou d'entrée apparaît dans le rapport. |
| C23 | Engine 2027 disponible ; chargement net8 sur l'engine 2026.5 / .NET 10 | Revit.props, src/Packaging/README.md | Exécution de la colonne 2026 et 2027. |
| C24 | Format du fichier de paramètres partagés (mots-clés `DATATYPE` historiques) accepté par 2025-2027 | Guide API « Shared Parameters » | Fiche §2 : si Revit refuse le fichier, créer les paramètres à la main avec les mêmes noms et types, puis adapter les GUID dans les settings (voir la fiche). |
| C25 | Stabilité des `ElementId` / `UniqueId` entre années (mise à niveau d'un même RVT) | — | Comparer les fichiers liés 2025/2026/2027 si les maquettes sont mises à niveau plutôt que reconstruites. |
| C26 | D9 : performance | SPECIFICATIONS D9 | Relever la durée du WorkItem et la taille des maquettes (R01, R07) ; maquette représentative du métier à prévoir en plus. |

## 6. Tests automatiques du jeu

- `src/Tests/Recette/RecetteFixturesTests.cs` (`dotnet test`) vérifie, pour chaque attendu :
  - qu'il est conforme à `docs/schema/result.schema.json` et en `schemaVersion` courant ;
  - qu'il fait l'aller-retour contrats → `ResultExportService` sans perte ;
  - qu'il est reproduit par le vrai `QualityCheckService` (indicateurs, anomalies, ordre, messages, statut,
    compteurs) à partir des sorties de lecture ;
  - que les anomalies sont celles des fabriques de production (`DocumentAnomalies`, `SharedParameterAnomalies`,
    `RoomReadBuilder`) ;
  - que les erreurs globales ont les messages du code (`SettingsReader`, `PhaseSelection`, `MainModelResolver`,
    `RevitVersionCompatibility`) ;
  - que chaque `settings.json` est lu comme attendu et désigne le bon RVT dans l'arborescence du ZIP.

  Si le code change de comportement, ces tests échouent : il faut alors mettre à jour l'attendu avec le code.
- `tools/recette/tests` (`python3 -m unittest discover -s tools/recette/tests`) teste le comparateur et `bind`, et
  vérifie la cohérence du manifeste (fichiers présents, GUID déclarés dans le fichier de paramètres partagés).
- `docs/schema/validate_examples.py` valide aussi les attendus (avec le paquet `jsonschema`).

## 7. Questions ouvertes

- **Q1** : deux pièces de variantes exclusives (P11/P12, même numéro) sont signalées `DUPLICATE_NUMBER` par le code
  actuel. Faut-il exclure les variantes non principales du contrôle des doublons ? L'attendu suit le code actuel.
- **Q2** : `designOption.name` de la variante principale. Revit affiche le suffixe « (primary) » (en anglais) dans
  l'interface. Faut-il le normaliser s'il apparaît dans `Element.Name` ?
- **Q3** : identifiants par année. Faut-il reconstruire les maquettes dans chaque version, ou mettre à niveau celles
  de 2025 ? La mise à niveau garde les identifiants (à confirmer, C25) et simplifie la relecture.
