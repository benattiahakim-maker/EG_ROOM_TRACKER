# Créer un nouveau service de la plateforme « platforme automation »

Guide écrit pour qu'un agent IA (ou une personne) puisse créer **seul** un nouveau service : un plugin Revit empaqueté en AppBundle APS
Design Automation, plus un service déclaré sur la plateforme. Il s'appuie sur ce dépôt (EG_ROOM_TRACKER, service RoomTracker) et, en lecture
seule, sur le backend `benattiahakim-maker/system-filter-backend` (`docs/frontend-contract.md`, `src/activity-contract.ts`).

Règles de ce guide :

- **Aucun secret** ici ni dans vos messages : pas de jeton, pas de `clientSecret`, pas de `clientId`, pas de nom d'owner APS (« nickname »).
  On écrit toujours `<owner>.<Activite>+<alias>`.
- **N'inventez rien.** Une étape qui n'est pas dans le code ou dans les docs est marquée **« à confirmer »** (liste en section 10).
- État des travaux relevé le **25 octobre 2026** (`gh pr list` des dépôts). Relire l'état des PR à la date de lecture.
- Le guide côté site (page Réglages, routes, affichage) est dans `docs/creer-un-service.md` du dépôt `CVCD-Systems-Inventory-Viewer`.

## 1. Vue d'ensemble

- **Deux projets APS autonomes** : un AppBundle et une Activity par service (systèmes : `revit-system-filter` ; RoomTracker : ce dépôt,
  AppBundle `EgRoomTracker_R<année>`, Activity `ExtractRooms_R<année>`). Aucun code partagé côté APS.
- **Deux services sur la même plateforme** : même backend (`system-filter-backend`) et même site (`CVCD-Systems-Inventory-Viewer`).
  Chaque service a sa propre ligne de configuration (identifiants APS, bucket, région, Activity, fichier résultat) et ses propres réglages.
  Le backend est générique : aucun code propre à un service.

Chemin d'une requête :

```text
site ── POST /services/{id}/jobs ─► backend
        (models[], mainModelPath, settings)   │ 1. ZIP d'entrée (modèles à plat) + settings.json
                                              │ 2. WorkItem APS sur l'Activity du service (<owner>.<Activite>+<alias>)
                                              │ 3. conversion Model Derivative (viewer 3D), en parallèle
                                              ▼
                     plugin (Revit, Design Automation) lit input/ + settings.json
                                              │ écrit result/ : result.generic.json, log.jsonl, Images/
                                              ▼
              output.zip ─► backend lit le fichier `resultFile`, remplit models[].urn, valide
site ◄── GET /services/{id}/jobs/{jobId}/envelope ──┘   (statut : GET …/jobs/{jobId})
```

Le backend **n'extrait ni ne calcule rien** : il empaquette, lance l'Activity, relit `output.zip` et sert l'enveloppe écrite par le plugin
(`docs/frontend-contract.md`, « Contrat d'un traitement »).

## 2. Étapes côté APS (AppBundle, Activity, alias)

Détails de référence : [`PUBLISHING.md`](PUBLISHING.md), [`../src/Packaging/README.md`](../src/Packaging/README.md), [`BUILD.md`](BUILD.md),
[`OUTILS.md`](OUTILS.md) (DaManager, WorkItemRunner).

### 2.1 Structure de l'AppBundle

Un ZIP par version de Revit, produit par `src/Packaging/scripts/build-appbundle.sh` (build + assemblage + contrôle) :

```text
EgRoomTracker_R2026.zip
└── EgRoomTracker.bundle/
    ├── PackageContents.xml        RuntimeRequirements OS=Win64 Platform=Revit SeriesMin=SeriesMax=R2026
    └── Contents/
        ├── EgRoomTracker.addin    Type=DBApplication, assembly en chemin relatif
        ├── EgRoomTracker.dll (+ .Application, .Contracts, .Export, .Logging, .Revit)
        └── DesignAutomationBridge.dll   (NuGet Autodesk.Forge.DesignAutomation.Revit de la version)
```

- **Revit 2025, 2026, 2027** : cibles `net8.0-windows` (2025, 2026) et `net10.0-windows` (2027), choisies par `-p:RevitVersion=<année>`
  (`build/Revit.props`). **Compiler une fois par version Revit** : un ZIP, un engine `Autodesk.Revit+<année>`, une Activity par version.
- Le contrôle (`appbundle.py check`) refuse `RevitAPI*.dll`, `settings.json`, `.env`, clés, fichiers hors liste, série Revit ou framework
  qui ne correspondent pas à la version. Le ZIP est reproductible. Le ZIP téléversé doit rester ≤ 100 Mo.
- Le point d'entrée (`AutomationApplication`) répond à `DesignAutomationReadyEvent`, ouvre le RVT lui-même (sans `/i`, décision D1).
- Un nouveau plugin reprend cette organisation (cœur sans Revit, projet Revit, point d'entrée par version, packaging) ; le cœur ne
  référence jamais Revit (test `RevitIsolationTests`).

### 2.2 Création et publication

Les scripts sont en simulation (dry-run) par défaut ; `--apply` écrit réellement dans APS et demande `APS_NICKNAME` + `APS_TOKEN` (jeton
2-legged temporaire, **jamais dans un fichier ni dans un message**, supprimé après usage).

```bash
src/Packaging/scripts/build-appbundle.sh                       # ZIP contrôlés dans artifacts/appbundles/
python3 src/Packaging/scripts/deploy.py test                   # plan seulement
python3 src/Packaging/scripts/deploy.py --apply test           # AppBundle + Activity, alias « test »
```

Par version Revit, `deploy.py --apply test` : vérifie que l'engine existe, crée l'AppBundle (ou une nouvelle version), téléverse le ZIP,
crée ou déplace l'alias `test`, puis crée une nouvelle version d'Activity qui référence l'AppBundle `+test` et son alias `test`.
Les outils DaManager et WorkItemRunner ([`OUTILS.md`](OUTILS.md)) font la même chose avec une interface.

### 2.3 L'Activity : contrat fixe

Un seul comportement pour tous les services (`src/activity-contract.ts` du backend, `src/Packaging/definitions/<année>/activity.json`) :

| Paramètre | Sens | `localName` | Rôle |
| --- | --- | --- | --- |
| `inputZip` | entrée (`get`, `zip: true`) | `input` | ZIP d'entrée **décompressé** dans `input/` (modèles Revit à plat, noms NFC) |
| `settings` | entrée (`get`) | `settings.json` | `schemaVersion`, `mainModelPath`, puis les réglages de la requête |
| `result` | sortie (`put`, `zip: true`) | `result` | dossier `result/` zippé en `output.zip` : fichier résultat + `log.jsonl` (+ `Images/`) |

- Ligne de commande : `$(engine.path)\revitcoreconsole.exe /al "$(appbundles[<AppBundle>].path)"` (sans `/i`).
  Ce dépôt ajoute `/l FRA` (noms de paramètres en français) dans la définition 2026 — à reprendre ou non selon le service.
- Ces noms sont **fixés par le contrat, jamais administrables** : un nouveau plugin doit les reprendre tels quels, sinon la plateforme ne
  trouve ni le ZIP, ni `settings.json`, ni `output.zip`.
- Le plugin écrit dans `result/` (créé au démarrage) : le fichier résultat **à la racine**, `log.jsonl` à la racine aussi
  (`ActivityContract.cs`). Le rapport APS garde la sortie console. Le plugin ne doit écrire aucun secret dans le journal.
- Le plugin met `Succeeded = false` en cas d'échec global ; le comportement exact d'APS sur la sortie en échec (D6) est à confirmer
  (voir section 10).

### 2.4 Alias, versions et noms

- Identifiant complet d'une Activity : **`<owner>.<Activite>+<alias>`**, par exemple `<owner>.ExtractRooms_R2027+test`. Le backend le
  refuse s'il ne suit pas ce motif (`PUT /config` : 400). L'alias n'est **jamais écrit en dur** dans le code : il est dans la config du service.
- Alias utilisé pour tous les essais : **`test`**. `prod` n'est posé que par `deploy.py promote`, après recette, depuis le même commit.
- Une version d'AppBundle et une d'Activity sont créées à **chaque** publication en test ; quota **100 versions** par type et 100 alias
  (`403 Maximum number of items exceeded` au-delà). Le nettoyage (`DELETE …/versions/<n>`) est manuel : DaManager, carte « Nettoyage des
  versions », protège les versions aliasées et celles du journal de publication.
- Retour arrière : repointer l'alias (`deploy.py rollback`), jamais supprimer une version référencée.
- **Bundles de test** : les AppBundles et Activities créés pour un test (alias `test` et `$LATEST`) doivent être **supprimés en fin de
  projet** quand le service a sa propre application APS. Rien ne les supprime automatiquement.
- Région : les Activities sont publiées par région Design Automation (`us-east` par défaut, `APS_DA_BASE_URL` pour `deploy.py`) ; le
  backend lit `SF_APS_DA_REGION` (`us-east` ou `eu-west`).

### 2.5 Règles de test (coûts)

- Tout se teste sur l'alias **`test`**. Pas de test sur `prod`.
- **Chaque WorkItem est payant** (Design Automation) et chaque conversion Model Derivative aussi. **Un test payant pour une question
  précise**, jamais en boucle. Avant, tout ce qui peut se vérifier gratuitement l'est : tests .NET sans Revit, validation du schéma,
  simulation de la plateforme (section 9).
- Les rapports de WorkItem sont valables 24 h : les télécharger tout de suite ; le statut est gardé 3 jours.

## 3. Réglages du plugin (`settings.json`)

`settings.json` est écrit par la plateforme. **Réservées à la plateforme** (si la requête les envoie, le backend répond 400) :

| Clé | Valeur |
| --- | --- |
| `schemaVersion` | `"1.0"` (chaîne `majeure.mineure` ; majeure ≠ 1 → `CONFIG_SCHEMA_UNSUPPORTED`) |
| `mainModelPath` | nom exact de l'entrée du ZIP du modèle principal (le backend choisit le premier `.rvt` envoyé s'il n'est pas donné) |

Tout le reste vient de `body.settings` de la requête `POST /services/{id}/jobs` : un objet JSON libre (≤ 64 Kio, ≤ 10 niveaux), transmis
tel quel. **Le backend ne valide pas ces réglages : c'est le plugin qui valide les siens** (`SettingsReader`, erreur `CONFIG_INVALID`).

Où se règlent-ils ? **Pas dans l'admin du service, pas en dur dans le backend.** L'utilisateur les saisit sur la page **« Réglages »
du service** du site (route `/services/:serviceId/reglages`, `ServiceSettingsPage`). Cette page est **générique** : un objet JSON libre
(≤ 64 Kio, ≤ 10 niveaux ; `schemaVersion` et `mainModelPath` réservés) gardé dans le **navigateur** (un jeu par service, perdu si on vide
le stockage local) et **envoyé en `body.settings` à chaque import**. Le site ne connaît aucun schéma de réglages : ajouter un service ne
demande donc aucun code pour cette page (un formulaire généré à partir d'un schéma de réglages est « à venir » côté site).

Clés connues de `SettingsReader` (RoomTracker, `src/Application/Configuration/SettingsReader.cs`), dans l'ordre du code : `schemaVersion`, `mainModelPath`
(réservées à la plateforme, ci-dessus), `phaseIds`, `volumeMode`, `sharedParameters` (entrées `key` + `guid` seulement), `roomParameters`, `elements`,
`elementCategories`, `roomImages`, `imageWidthPx`, `roomHeights`, `heightStepM`, `linkedElements`, `includeSecondaryDesignOptions`, `outputFormat`.
Détail des clés qui ne sont pas des réservées :

| Clé | Valeurs | Défaut (clé absente ou `null`) | Erreur si invalide |
| --- | --- | --- | --- |
| `phaseIds` | tableau **non vide** de chaînes d'entiers positifs (`"86961"`), sans doublon | toutes les phases | `CONFIG_INVALID` |
| `volumeMode` | `"preserve"` ou `"compute"` (casse exacte) | `preserve` | `CONFIG_INVALID` |
| `sharedParameters` | tableau d'entrées `{ key, guid }` **seulement** : `key` non vide, unique ; `guid` au format GUID, non nul | aucun | `CONFIG_INVALID` |
| `roomParameters` | `"none"` ou `"all"` | `none` | `CONFIG_INVALID` |
| `elements` | tableau sans doublon de `"doors"`, `"equipment"`, `"terminals"` | aucun | `CONFIG_INVALID` |
| `elementCategories` | objet `{ famille : [OST_…] }` (familles `doors`, `equipment`, `terminals`), tableaux non vides sans doublon | catégories par défaut | `CONFIG_INVALID` |
| `roomImages` | `true` ou `false` | `false` | `CONFIG_INVALID` |
| `imageWidthPx` | entier de 400 à 8000 | 8000 | `CONFIG_INVALID` |
| `roomHeights` | `true` ou `false` | `false` | `CONFIG_INVALID` |
| `heightStepM` | nombre de 0,1 à 2 (mètres) | 0,5 | `CONFIG_INVALID` |
| `linkedElements` | `true` ou `false` | `true` | `CONFIG_INVALID` |
| `includeSecondaryDesignOptions` | `true` ou `false` : exporter aussi les pièces des options de conception secondaires (voir section 8) | `false` | `CONFIG_INVALID` |
| `outputFormat` | `"contract"`, `"trc"`, `"both"`, `"generic"` | **`generic` sur le chemin Design Automation** ; `contract` hors DA (outil local, tests) | `CONFIG_INVALID` |

Règles générales du lecteur :

- fichier présent, ≤ 1 Mio, UTF-8, JSON strict (pas de commentaire ni de virgule finale), racine objet, pas de clé en double ;
- en `schemaVersion` `1.0`, une **clé inconnue est refusée** (faute de frappe) ; en `1.x` avec x > 0, elle est ignorée ;
- `mainModelPath` : chemin relatif à `input/`, pas de `..`, pas de chemin absolu (`PATH_OUTSIDE_INPUT`), fichier `.rvt` existant
  (`RVT_NOT_FOUND`) ; plusieurs RVT sans `mainModelPath` → `RVT_AMBIGUOUS` ([`INPUTS.md`](INPUTS.md)) ;
- les messages d'erreur nomment la clé en cause et ne contiennent ni chemin absolu ni secret.

**`outputFormat`.** La plateforme n'envoie que `schemaVersion`, `mainModelPath` et les `settings` de la requête : elle **n'envoie pas
`outputFormat`**. Un plugin qui s'appuie sur ce champ doit donc avoir un **défaut `generic` quand il tourne dans Design Automation**
(`InputConfigurationService.ForDesignAutomation()`, appelé par `CompositionRoot`), sinon le fichier attendu manque (`RESULT_MISSING`,
section 8). Un nouveau plugin n'a besoin que d'un seul format : le sien, l'enveloppe commune.

Un nouveau plugin documente ses propres clés dans un tableau de ce type, valide chaque clé, refuse les inconnues en `1.0`, et ne lit jamais
`schemaVersion` ni `mainModelPath` comme des réglages.

## 4. Configuration du service côté plateforme (admin)

Un service = **une ligne de config admin** : `PUT /services/{id}/config` (admin) crée la ligne si l'identifiant est nouveau. L'identifiant
suit `^[a-z0-9](?:[a-z0-9-]{0,62}[a-z0-9])?$` (RoomTracker : `pieces-roomtracker`).

| Champ | Contenu | Remarque |
| --- | --- | --- |
| `name` | nom affiché | obligatoire pour un nouvel identifiant (sinon 400 `INPUT_INVALID`) |
| `description` | texte libre | facultatif |
| `status` | `available` ou `coming_soon` | `coming_soon` : 409 `SERVICE_UNAVAILABLE` sur les routes d'exécution |
| `clientId`, `clientSecret` | identifiants de l'application APS **du service** | **valeurs jamais écrites dans un doc ni un message** ; le secret est en écriture seule |
| `bucketKey`, `region` | bucket OSS et région (`US` ou `EMEA`) | la région suit la source du bucket |
| `activityId` | `<owner>.<Activite>+<alias>` | ex. `<owner>.ExtractRooms_R2027+test` ; le motif est vérifié |
| `resultFile` | **`result.generic.json`** | nom d'un `.json` à plat, 128 caractères max ; **obligatoire**, sans valeur par défaut : sinon 409 `RESULT_FILE_MISSING` à l'envoi |

- Chaque service a **ses propres identifiants APS** (jeton viewer, conversions, traitements) : deux services peuvent être sur deux
  applications APS différentes.
- Tout se saisit dans l'admin du site ; **jamais dans Railway** ni dans des variables d'environnement du backend (elles ne sont qu'un repli
  champ par champ). `resultFile` est figé dans chaque traitement à sa création.
- Au démarrage du backend en mode APS : `SF_APS_ENABLED=1` et `SF_JOBS_DIR`. Sans `SF_APS_ENABLED`, la plateforme est en **simulation**.
- Les **réglages du plugin ne sont pas dans cette config** : ils sont saisis par l'utilisateur sur la page « Réglages » du service (section 3).
- Nouvelle ligne de config = rien à coder dans le backend ni le site.

## 5. Format de sortie `result.generic.json`

Le plugin écrit une **enveloppe commune** dans `result/` (nom = `resultFile`). Référence détaillée : [`GENERIC.md`](GENERIC.md) et
[`schema/generic.schema.json`](schema/generic.schema.json) ; exemple : [`examples/generic.example.json`](examples/generic.example.json).
Source de vérité : `docs/frontend-contract.md` du backend (en cas d'écart, la plateforme gagne).

```json
{ "contractVersion": "1.0", "serviceId": "pieces-roomtracker", "status": "done", "warnings": [],
  "models": [ { "key": "main", "title": "Nom du fichier sans extension", "urn": "" } ],
  "objects": [ { "id": "room-123", "name": "001 Salle", "children": [], "properties": [] } ] }
```

- **Racine** : `contractVersion` (`1.0`, ne change pas), `serviceId` (la plateforme le remplace par celui de la route), `status`
  (`done` ou `partial` ; il n'y a **pas** de statut d'échec : un échec n'écrit pas de fichier résultat), `warnings[]`, `models[]`, `objects[]`.
- **`models[]`** : `key` (`main`, puis `link-1`, `link-2`…), `title`, `urn`. **Le plugin laisse `urn` vide** : la plateforme le remplit avec
  `conversion.urn` (clé `main` = maquette principale, sinon même nom de fichier, sinon l'unique restant), puis valide. Un `urn` déjà rempli
  n'est jamais changé ; sans conversion correspondante, il reste vide et la validation répond 502 `RESULT_INVALID`.
- **`objects[]`** : arbre `{ id, name, children[], properties[] }` ; `id` unique dans **tout** le fichier ; profondeur ≤ 64.
- **Propriété** : `{ key, label, type, value, unit?, group? }`.
  - `type` : `text`, `number` (fini ; `unit` facultatif), `boolean`, `date` (ISO 8601), `box` (`{min:[x,y,z], max:[x,y,z]}` en mm, repère du
    modèle principal), `ref` (`value` = `id` d'un objet de l'enveloppe, toujours résolu), `file` (`{name, mimeType, url}` ; `url` en
    `https://…` ou chemin relatif commençant par `/`, jamais `http:`).
  - Clés **uniques par objet**, sauf `ref` qui peut se **répéter avec la même clé** (une porte pointe deux pièces par `room` deux fois).
  - `group` : texte non vide, 120 caractères max, facultatif (libellé de regroupement à l'affichage).
  - `viewerId` : propriété réservée (`text`) = `UniqueId` Revit, pour retrouver l'objet dans le viewer ; `modelKey` = clé de `models[]`.
- **Pas de champ `kind` ni `category` sur l'objet** : un seul modèle générique « objet BIM + propriétés ». Le rôle d'un lien passe par la
  **clé** du `ref`.
- **Limites** : le backend ne contrôle que la **structure** (ids uniques, `ref` résolus, valeurs typées, forme de `file`). Aucune règle
  métier côté plateforme. `output.zip` : ≤ 20 000 entrées, ≤ 1 Gio. Les évolutions sont des **ajouts**, jamais un changement de sens.
- Une valeur inconnue est **omise**, jamais mise à zéro. Valeurs typées, jamais du texte arrondi.

## 6. Contrat d'avertissements — **EN PRODUCTION** (backend et site, au 25 octobre 2026)

> **État au 25 octobre 2026.** Backend : `system-filter-backend` #74 **fusionnée** (main `375a37a`). Site : `CVCD-Systems-Inventory-Viewer`
> #74 **fusionnée** (`8e0d22d`). Plugin RoomTracker : `EG_ROOM_TRACKER` #62 **fusionnée** (`c2e7bda`, `LINK_MISSING` en un seul `info`).
> Le contrat est donc accepté par le backend et affiché par le site.

Format :

- `warnings[]` contient, par élément, soit un **texte** (lu comme un `warning`), soit un **objet** `{ severity: 'info'|'warning', message, code? }`.
- `message` : texte non vide, 400 caractères au plus. `code` (facultatif) : lettres, chiffres, `-` et `_`, commence par une lettre ou un
  chiffre, **64 caractères au plus** (`LINK_MISSING` ou `lien-absent` sont valides ; espace, accent, chaîne vide refusés).
- **Un seul message `info` par type** (regrouper : « 3 liens absents : a, b, c » et non trois messages), **20 éléments au plus** dans
  `warnings[]` (au-delà : `RESULT_INVALID`, donc regrouper ou tronquer côté plugin).
- **`status: "partial"` seulement s'il y a au moins un `warning`.** Une enveloppe qui n'a que des `info` reste `done`. La plateforme ne
  réécrit ni ne contrôle cette cohérence : c'est le plugin qui décide.
- `contractVersion` **reste `1.0`** : changement additif.
- Un `info` s'affiche discrètement sur le site (ligne grise) ; un `warning` et « Résultat partiel » donnent le bandeau orange.

**Règle (reste valable pour tout nouveau plugin ou toute évolution du contrat) : un plugin n'émet pas d'objet dans `warnings[]` tant que le
backend ET le site ne les acceptent pas.** Sinon le backend refuse l'enveloppe (`RESULT_INVALID`). Ordre : validation backend → affichage
site → plugins. C'est fait pour ce contrat. Un plugin ancien qui n'écrit que des textes reste valide.
Exemples réels côté RoomTracker : `LINK_MISSING` (un `info` groupé) et `DESIGN_OPTIONS_EXCLUDED` (un `info` chiffré).

## 7. Règle d'or : aucun hack côté backend ou site

- **Tout se corrige dans le plugin ou le bundle** (ou la config du service). Pas de cas particulier « pour RoomTracker » dans le backend
  ni dans le site.
- Le backend est **générique** (`/services/:id/...`) et ne fait que **lire le JSON** du plugin, le valider en structure et le servir.
  Il ne calcule rien (ni pièces, ni liens, ni boîtes).
- Si l'API manque d'une fonction, on la **demande** au bot backend dans le salon du projet et on décrit le besoin ; on ne contourne pas.
- Si le plugin ne sait pas écrire quelque chose, on écrit moins (valeur omise) plutôt que d'inventer.

## 8. Pièges rencontrés

| Piège | Cause et correction | État |
| --- | --- | --- |
| `RESULT_MISSING` au premier test | Le plugin n'avait pas de `outputFormat` dans `settings.json` (la plateforme ne l'envoie pas) : il écrivait seulement `result.json`. Défaut `generic` sur le chemin Design Automation | corrigé, PR #61 (fusionnée) |
| Liens Revit absents (`LINK_MISSING`) | Un message par lien et résultat `partial` : bruyant et trompeur (un lien absent est normal : liens en chemin absolu, absents du ZIP). Maintenant : **un seul `info`** `{code: LINK_MISSING}` nommant tous les liens (liste coupée à 400 caractères) ; un projet sans ses liens est `done` ; plus aucun objet d'anomalie `LINK_MISSING` (`anomaly-N`), les autres anomalies restent des objets | **fait**, PR #62 (fusionnée, `c2e7bda`) ; objets `LINK_MISSING` supprimés : voir CHANGELOG |
| Options de conception secondaires | Les pièces des options secondaires sont dans le collecteur du document : elles sortent **en double** avec le principal (`COLLECTE.md` : pièces « dans toutes les variantes »). Maintenant : elles sont **exclues par défaut** (seules les pièces hors option et de l'option principale sortent), propriété `option_conception` sur chaque pièce, réglage booléen `includeSecondaryDesignOptions` (`false` par défaut), information `DESIGN_OPTIONS_EXCLUDED` | **fait**, PR #63 (fusionnée, `8b1dc52`). Images des pièces exclues : **corrigé**, PR #67 (fusionnée, `a8d8a83`) : ni fichier dans `Images/` ni propriété image en `generic` ; avec `contract`/`trc`/`both` elles restent avec leur image |
| Unités et arrondis | Flottants non arrondis (bruit numérique) et libellé de catégorie peu lisible. Maintenant : 3 décimales pour les nombres avec unité (`m`, `m²`, `m³`), 1 décimale pour la `box` (mm), 12 chiffres significatifs pour les paramètres Revit ; `categorie_libelle` lisible. **Règle (décision PM) : la `box` est toujours en mm, arrondie ; ne pas ajouter de `unit`** (le validateur du backend n'accepte `unit` que sur le type `number`, sinon `RESULT_INVALID`). Les paramètres Revit restent des nombres sans unité (unité inconnue du plugin). | **fait**, PR #65 (fusionnée, `edd0b98`) ; règle de la `box` : décision PM |
| `Category` : valeur technique ou libellé | `Category` est une propriété ordinaire dont la **valeur est technique** (`Space`, `Door`, `cvc equipement`…, comme le TRC) ; le `label` de la propriété est « Catégorie ». `categorie_libelle` (texte) donne le libellé lisible (`Pièce`, `Porte`, `Équipement CVC`…) ; la catégorie Revit est dans `categorie_revit`. Ne pas confondre valeur et affichage | **fait**, PR #65 (`edd0b98`) |
| Modèle lourd | Un modèle de **183 Mo** s'ouvre en **~16 s** dans le WorkItem (relevé d'un test, non mesuré dans ce dépôt) : pas besoin de supprimer des fonctions pour ça | relevé du Testeur, à confirmer |
| Sondage APS | Le backend interroge APS toutes les **10 s** par défaut (`simulation.ts`, `apsPollIntervalMs`) : un job court peut sembler durer jusqu'à 10 s de plus | relevé backend |
| Conversion Model Derivative | **Réutilisée** si le même ZIP et le même fichier principal ont déjà été convertis (clé par contenu `sha256` : un URN = une conversion) ; pas de coût de plus. Une annulation n'arrête pas une conversion déjà lancée | documenté (backend) |
| Anciens jobs | Un traitement fait avant le retrait du calcul backend peut ne pas avoir de `conversions[]` valides ; un résultat relu peut alors être `RESULT_INVALID` | à confirmer (le backend documente 410 `ENVELOPE_UNAVAILABLE` pour les anciens traitements sans enveloppe) |
| Journal | `log.jsonl` est copié **à la racine** du `output.zip` (à côté du fichier résultat et de `Images/`), en JSON Lines ASCII, secrets masqués. Une erreur d'écriture ne fait pas échouer l'extraction (`LOG_FILE_UNAVAILABLE`) | documenté, PR #60 ([`LOGS.md`](LOGS.md)) |
| Diagnostic d'un échec | En échec, `GET /services/{id}/jobs/{jobId}` porte `failure.diagnostic` : `output` (noms et tailles des fichiers de `output.zip`, 200 au plus) et `report` (rapport APS nettoyé, 8 Kio, texte brut). Absent d'un traitement réussi | documenté (backend, PR #73 fusionnée) |
| Extraction en échec | Pas de fichier résultat generic (seulement `result.json` avec `status: failed`) : la plateforme voit `RESULT_MISSING`/`RESULT_UNAVAILABLE` ; lire alors `failure.diagnostic` et `log.jsonl` | documenté ([`GENERIC.md`](GENERIC.md)) |
| Liens dans le ZIP | Les liens sont à des chemins **relatifs** ; `mainModelPath` est obligatoire dès qu'il y a plusieurs RVT ; chemins courts (Windows 260) ; noms non ASCII : UTF-8 ou fichier `.codepage` | documenté ([`INPUTS.md`](INPUTS.md), D4) |
| Quotas APS | 100 versions, 100 alias, 100 appels/min, bundle ≤ 100 Mo envoyé | documenté ([`PUBLISHING.md`](PUBLISHING.md)) |

## 9. Checklist « nouveau service », pas à pas

1. **Cadrer** : le service produit-il une enveloppe commune (objets + propriétés) ? Choisir l'identifiant du service (`^[a-z0-9-]+$`) et
   le nom du fichier résultat. Lire [`GENERIC.md`](GENERIC.md) et `docs/frontend-contract.md` (backend).
2. **Plugin** : cœur sans Revit + implémentation Revit + point d'entrée (`AutomationApplication`). Lire `input/`, `settings.json`
   (valider toutes les clés, section 3). Écrire `result/<resultFile>` et `result/log.jsonl`. Pas de secret dans les journaux.
3. **Sortie** : mapper vers l'enveloppe (section 5) : `urn` vide, `ref` pour les relations, `group` pour l'affichage, valeurs omises
   plutôt qu'inventées. Avertissements : textes seulement tant que le contrat de la section 6 n'est pas déployé.
4. **Tests gratuits** (sans Revit ni APS) : `dotnet build`, `dotnet test`, `python3 docs/schema/validate_examples.py` (exemples contre le
   schéma), tests du packaging. La CI du dépôt (`.github/workflows/ci.yml`) les rejoue et construit les trois AppBundles, sans secret.
5. **Bundle** : `build-appbundle.sh` pour 2025, 2026 et 2027 (ou seulement les versions voulues) ; contrôler les ZIP.
6. **Publier en test** (porteur du compte APS, jeton temporaire) : `deploy.py` en plan, puis `--apply test`. Noter les numéros de version
   (journal de publication). Alias **`test`** seulement. Ne rien nommer avec un owner dans un doc ou un message.
7. **Config admin du service** (section 4) : `name`, `description`, `status`, `clientId`/`clientSecret`/`bucketKey`/`region` (saisis
   dans l'admin par la personne qui les possède), `activityId` = `<owner>.<Activite>+test`, `resultFile` = nom du fichier résultat.
8. **Réglages côté site** : rien à coder. La page « Réglages » du service existe déjà (générique) ; l'utilisateur y saisit le JSON des réglages
   du plugin (tableau de la section 3), envoyé en `body.settings` à chaque import. Documenter les clés du plugin pour lui.
9. **Test gratuit en simulation** : plateforme lancée **sans** `SF_APS_ENABLED`. Elle fabrique un `output.zip` d'exemple avec le fichier
   `resultFile` ; vérifier que le site affiche l'arbre, le tableau et les avertissements (`warnings`, `failedWithResult`, `invalid`
   sont des scénarios de simulation). Aucun coût.
10. **Un test payant** (sur demande de la personne responsable, jamais en boucle), avec `SF_APS_ENABLED=1` et la config saisie dans l'admin,
    une vraie maquette `.rvt` pour **une question précise**. Vérifier dans l'ordre :
    - `output.zip` contient `log.jsonl` **et** le fichier résultat ; `log.jsonl` contient l’événement `EXTRACTION_SUMMARY` ;
    - le fichier résultat est valide (`contractVersion` 1.0, `status` attendu, `warnings` conformes à la section 6) ;
    - `GET …/envelope` répond 200, `models[].urn` est **rempli** par la plateforme ;
    - la conversion Model Derivative est en `success` et la **3D** s'ouvre dans le viewer (les `viewerId` retrouvent les objets) ;
    - en cas d'échec : `failure.diagnostic` (fichiers et rapport) puis `log.jsonl`.
11. **Corriger dans le plugin**, republier en `test`, relancer **un** test seulement si nécessaire. Pas de hack backend ni site.
12. **Promotion et fin** : recette terminée → `deploy.py promote` (alias `prod`, même commit) et journal ; en fin de projet, supprimer les
    bundles et Activities de test et faire pointer le service sur sa propre application APS ; aucune clé ni nickname dans les docs.

## 10. À confirmer

Points que ce guide ne peut pas affirmer avec le code et les docs des dépôts :

1. **D6** : ce qu'APS fait de la sortie quand le plugin met `Succeeded = false` (le fichier `result` est-il téléversé ? quel statut
   pour une sortie `required` absente ?). Non testé (`src/Packaging/README.md`, D6).
2. **Moteurs et runtimes** : disponibilité de `Autodesk.Revit+2027` et chargement de la cible `net8.0-windows` sur le moteur 2026
   (Revit 2026.5 / .NET 10) ; EGR-02 / EGR-14.
3. **`result` optionnel ou requis** : le contrat du backend (`activity-contract.ts`) marque `result` comme `optional`, la définition de ce
   dépôt le déclare `required: true`. Aucun effet constaté ; à aligner.
4. **Liens relatifs** : comment RevitCoreConsole résout les liens après décompression ; comportement avec noms accentués (D4).
5. **`pathInZip`** : le runbook du backend mentionne `pathInZip = mainModelPath` pour `inputZip` ; le plugin lit `mainModelPath` dans
   `settings.json`. Pas vérifié que les deux sont toujours identiques pour un ZIP à plat.
6. **Unité de la `box`** : réglé, décision PM. La `box` est toujours en mm, arrondie ; ne pas ajouter de `unit` (section 8).
7. **Images des pièces exclues** : réglé, PR #67 (`a8d8a83`).
8. **« En production »** : désigne le code fusionné sur `main` du backend et du site. Le déploiement réel (migrations, hébergement) n'est pas vérifié ici.
9. **Mesures** : 183 Mo en ~16 s (relevé de test) et anciens traitements sans `conversions[]` → `RESULT_INVALID` : non retrouvés dans le code
   ou les docs de ces dépôts.
10. **État des PR** (sections 6 et 8) : relevé du 25/10/2026, à relire avant de s'y fier.
11. **Modèle des quatre pages** : non décrit ici (démarre après le test RoomTracker). Le formulaire de réglages généré à partir d'un schéma est « à venir » côté site.
