# Packaging Automation API (EGR-13)

Ce dossier contient les AppBundles et Activities Revit 2025, 2026 et 2027 : manifestes, définitions JSON, assemblage
et contrôle des ZIP, publication. **Aucun script n'appelle APS sans `--apply`**, et aucun secret n'est versionné.

```
src/Packaging/
├── bundle/
│   ├── EgRoomTracker.addin               manifeste Revit (DBApplication, assembly relative)
│   └── PackageContents.xml.template      SeriesMin/SeriesMax remplacés par R<année>
├── definitions/<2025|2026|2027>/
│   ├── appbundle.json                    id EgRoomTracker_R<année>, engine Autodesk.Revit+<année>
│   └── activity.json                     id ExtractRooms_R<année>, sans /i (D1), {{NICKNAME}}, {{APPBUNDLE_ALIAS}}
└── scripts/
    ├── build-appbundle.sh                build + assemblage + contrôle (bash, python3)
    ├── appbundle.py                      assemble / check (bibliothèque standard Python)
    ├── deploy.py                         publication test → prod, rollback, status (dry-run par défaut)
    └── tests/test_packaging.py           tests sans réseau : python3 -m unittest discover -s src/Packaging/scripts/tests
```

## 1. Construire les AppBundles

```bash
src/Packaging/scripts/build-appbundle.sh              # les trois versions
src/Packaging/scripts/build-appbundle.sh 2026         # une seule version
WITH_SYMBOLS=1 src/Packaging/scripts/build-appbundle.sh   # ajoute les .pdb EgRoomTracker.* (diagnostic)
```

Prérequis : SDK .NET (`global.json`) et `python3`. Le script tourne sous Linux, macOS et Windows (Git Bash ou WSL).
Pour chaque version, il compile `src/EntryPoint` en Release avec `-p:RevitVersion=<année>`, assemble
`artifacts/appbundles/EgRoomTracker_R<année>.zip`, puis contrôle le ZIP. Le ZIP est reproductible : entrées triées,
date fixe.

```
EgRoomTracker_R2026.zip
└── EgRoomTracker.bundle/
    ├── PackageContents.xml              RuntimeRequirements OS=Win64 Platform=Revit SeriesMin=SeriesMax=R2026
    └── Contents/
        ├── EgRoomTracker.addin
        ├── EgRoomTracker.dll            (point d'entrée, net8.0 pour 2025/2026, net10.0 pour 2027)
        ├── EgRoomTracker.Application.dll, .Contracts.dll, .Export.dll, .Logging.dll, .Revit.dll
        └── DesignAutomationBridge.dll   (NuGet Autodesk.Forge.DesignAutomation.Revit de la version)
```

### Contrôle du contenu (`appbundle.py check`)

Le contrôle échoue, avec la liste des écarts, dans les cas suivants :

- **fichier interdit** : `RevitAPI*.dll`, `RevitAddInUtility.dll`, `AdWindows.dll`, `*.deps.json`,
  `*.runtimeconfig.json`, `settings.json`, `.env`, `*.pfx`, `*.pem` ou `*.key` ;
- **fichier non autorisé** : tout fichier hors de la liste ci-dessus (seuls les `.pdb` de nos assemblies sont
  facultatifs) ;
- **fichier requis manquant** ;
- **structure** : plus d'un dossier racine, chemin absolu ou `..` ;
- **`PackageContents.xml`** : séries différentes de `R<année>`, `ModuleName` différent de
  `./Contents/EgRoomTracker.addin`, ou gabarit non remplacé ;
- **`.addin`** : `Type` différent de `DBApplication`, `Assembly` non relative ou introuvable, `FullClassName`
  incorrect, `AddInId` qui n'est pas un GUID, ou `UseRevitContext=False` ;
- **framework** : l'assembly d'entrée n'est pas compilée pour le framework de la version (protège contre un
  mélange de sorties de build).

Utilisation seule : `python3 src/Packaging/scripts/appbundle.py check artifacts/appbundles/EgRoomTracker_R2026.zip --revit-version 2026`.

## 2. Définitions

| Élément | Valeur |
| --- | --- |
| AppBundle | `EgRoomTracker_R<année>`, engine `Autodesk.Revit+<année>` |
| Activity | `ExtractRooms_R<année>`, même engine, `appbundles: ["{{NICKNAME}}.EgRoomTracker_R<année>+{{APPBUNDLE_ALIAS}}"]` |
| Ligne de commande | `$(engine.path)\\revitcoreconsole.exe /al "$(appbundles[EgRoomTracker_R<année>].path)"` : **sans `/i`**, l'add-in ouvre lui-même le RVT résolu (D1) |
| `inputZip` | `get`, `zip: true`, `localName: input`, requis : ZIP de la maquette (docs/INPUTS.md) |
| `settings` | `get`, `localName: settings.json`, requis |
| `result` | `put`, `localName: result.json`, requis (voir D6) |
| Alias | `test` puis `prod`, pour les AppBundles comme pour les Activities |

Les noms locaux sont ceux de `src/EntryPoint/ActivityContract.cs`, ce que vérifie un test. Une version d'Activity
référence l'AppBundle par son alias : l'Activity `+test` utilise l'AppBundle `+test`, et l'Activity `+prod`
utilise l'AppBundle `+prod`.

Exemple de WorkItem, non exécuté ici :

```json
{
  "activityId": "<APS_NICKNAME>.ExtractRooms_R2026+test",
  "arguments": {
    "inputZip": { "url": "<URL signée du ZIP>", "verb": "get" },
    "settings": { "url": "<URL signée de settings.json>", "verb": "get" },
    "result":   { "url": "<URL signée de destination>", "verb": "put" }
  }
}
```

## 3. Publier (porteur du projet, avec un jeton temporaire)

```bash
src/Packaging/scripts/build-appbundle.sh
export APS_NICKNAME=<nickname>            # nickname de l'application APS
python3 src/Packaging/scripts/deploy.py test             # plan (dry-run), aucun appel
export APS_TOKEN=<jeton 2-legged code:all>                # jamais dans un fichier versionné
python3 src/Packaging/scripts/deploy.py --apply test     # publication + alias test
# … WorkItems de test (EGR-14) …
python3 src/Packaging/scripts/deploy.py --apply promote  # alias prod
unset APS_TOKEN
```

Variables d'environnement : `APS_TOKEN` et `APS_NICKNAME` (obligatoires avec `--apply`), `APS_DA_BASE_URL` (défaut
`https://developer.api.autodesk.com/da/us-east/v3`), `REVIT_VERSIONS` (défaut `2025 2026 2027`), `APPBUNDLE_DIR`
(défaut `artifacts/appbundles`). Le jeton n'est jamais affiché. Les en-têtes de téléversement S3 (`formData`) ne
sont pas journalisés.

### Commande `test`, pour chaque version

1. `GET /engines` : l'engine doit exister. Cette vérification n'est faite qu'une fois, pour toutes les versions.
2. Le ZIP est contrôlé de nouveau ; un ZIP refusé arrête tout avant la moindre écriture.
3. AppBundle : `GET /appbundles/{id}/aliases`. En cas de 404, `POST /appbundles` ; sinon
   `POST /appbundles/{id}/versions` (sans `id`). Le ZIP est ensuite téléversé sur `endpointURL` (champs `formData`
   puis `file` en dernier), puis l'alias `test` est créé (`POST …/aliases`) ou déplacé (`PATCH …/aliases/test`).
4. Activity : même logique, avec `appbundles` → `+test`.

### Commande `promote`, pour chaque version

1. L'alias `prod` de l'AppBundle est pointé sur la version actuellement en `test`.
2. Une nouvelle version d'Activity est créée, avec `appbundles` → `+prod`, et l'alias `prod` de l'Activity est
   pointé dessus.
3. Les versions `prod` précédentes sont affichées, avec les commandes de retour arrière correspondantes.

La définition d'Activity promue est relue depuis le dépôt : lancer `promote` depuis le même commit que `test`.

### Retour arrière

Un alias est simplement repointé sur une version antérieure, qui reste disponible :

```bash
python3 src/Packaging/scripts/deploy.py --apply status        # alias actuels
python3 src/Packaging/scripts/deploy.py --apply rollback --kind appbundle --revit 2026 --to-version 3
python3 src/Packaging/scripts/deploy.py --apply rollback --kind activity  --revit 2026 --to-version 5
```

Sans `--apply`, la commande affiche le `PATCH` prévu. Il faut repointer les deux alias si la définition d'Activity
a changé. Si seul le code a changé, repointer l'AppBundle suffit, à condition que l'Activity `+prod` résolve l'alias
de l'AppBundle à l'exécution (voir les points ouverts).

## D6 — sortie en échec (à tester)

Comportement actuel :

1. En échec global, `result.json` est écrit avec `status: failed`.
2. Si l'écriture échoue (`EXPORT_FAILED`), une seule nouvelle tentative écrit un `result.json` minimal ; sinon, seul
   le log console subsiste.
3. `AutomationApplication` met ensuite `Succeeded = false`.

La documentation APS ne dit pas si les sorties sont téléversées dans ce cas, ni ce que produit un paramètre de sortie
`required: true` absent. Protocole pour EGR-14, sur chaque engine :

| Cas | Comment le provoquer | À relever |
| --- | --- | --- |
| Échec métier avec `result.json` écrit | `settings.json` avec `schemaVersion: "2.0"` | statut du WorkItem (`failedInstructions` ?), présence de `result.json` à l'URL de sortie, contenu du rapport |
| Succès | maquette de référence | `success`, `result.json` conforme au schéma |
| Sortie absente | ne pas pouvoir écrire `result.json` (test dédié, ex. build de diagnostic) | statut (`failedUpload` ?) avec `required: true`, puis avec `required: false` |

Selon les résultats, deux voies restent possibles : garder `Succeeded = false`, si APS téléverse quand même
`result.json` ; ou passer à `Succeeded = true` avec `status: failed` dans le fichier. Dans le second cas, il faut
documenter que le statut métier est dans le fichier et non dans celui du WorkItem.

## Points ouverts

- Engine `Autodesk.Revit+2027` : les notes de version APS annoncent Revit 2027 (avril 2026), mais le tableau des
  engines et la politique de cycle de vie s'arrêtent à 2026. `deploy.py --apply test` vérifie `GET /engines`
  avant toute écriture.
- La présence de `DesignAutomationBridge.dll` dans le bundle suit le tutoriel APS, qui cite le paquet NuGet comme
  dépendance de l'AppBundle. Son exemple d'arborescence ne le montre pas : c'est à confirmer au premier WorkItem.
- L'exclusion des `*.deps.json` suppose le chargement dans le contexte Revit par défaut (pas de `UseRevitContext`).
  La documentation ne dit pas si Revit lit un `deps.json` dans ce contexte.
- `localName: input` avec `zip: true`, sans `pathInZip` : la documentation décrit `localName` comme « file or
  folder », mais pas explicitement le dossier de décompression. C'est à valider en WorkItem (EGR-14).
- La ligne de commande reprend à l'identique celle du tutoriel APS (`\\` dans la valeur JSON) ; la documentation
  demande de ne pas la modifier.
- La résolution de l'alias d'AppBundle référencé par une Activity (à l'exécution ou à la création de la version)
  n'est pas documentée. Par prudence, `promote` crée une nouvelle version d'Activity.
