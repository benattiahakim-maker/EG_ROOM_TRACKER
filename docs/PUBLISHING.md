# Publication, recette et retour arrière (EGR-15)

Ce guide décrit le parcours complet : build local, tests, AppBundles, publication sur l'Automation API (Design
Automation), recette, promotion en production et retour arrière. Les détails techniques du packaging (contenu des
ZIP, définitions d'AppBundle et d'Activity, algorithme de `deploy.py`) sont dans
[`src/Packaging/README.md`](../src/Packaging/README.md) ; ceux du build .NET dans [`BUILD.md`](BUILD.md).

```text
build + tests (local ou CI) ─► ZIP contrôlés ─► deploy.py test ─► WorkItems de recette (EGR-14)
                                                                       │ OK               │ KO
                                                                       ▼                  ▼
                                                             deploy.py promote     corriger, republier en test
                                                                       │
                                                       incident en prod ─► deploy.py rollback
```

## 1. Prérequis et secrets

| Besoin | Build / tests / ZIP | Publication |
| --- | --- | --- |
| SDK .NET 10 (`global.json`) + runtime .NET 8 | oui | oui (ZIP) |
| Python 3.10+ (bibliothèque standard) | oui (packaging) | oui |
| `jsonschema` (pip) | seulement `docs/schema/validate_examples.py` | non |
| Revit, compte APS | **non** | compte APS du porteur |
| `APS_TOKEN`, `APS_NICKNAME` | non | oui, avec `--apply` |

Règles de secrets :

- **Aucun secret dans le dépôt** : ni jeton, ni Client ID/Secret, ni URL signée. La CI n'en reçoit aucun et ne publie
  rien.
- `APS_TOKEN` est un jeton 2-legged temporaire (scope `code:all`, exigé par les endpoints AppBundles/Activities),
  obtenu par le porteur juste avant la publication, exporté dans le shell courant puis supprimé (`unset APS_TOKEN`).
  Ne pas le mettre dans un fichier `.env` versionné ni dans l'historique du shell partagé.
- `deploy.py` n'affiche jamais le jeton et ne journalise pas les champs `formData` du téléversement S3.

Variables lues par `deploy.py` :

| Variable | Rôle | Défaut |
| --- | --- | --- |
| `APS_TOKEN` | jeton 2-legged `code:all` | obligatoire avec `--apply` |
| `APS_NICKNAME` | nickname de l'application APS (préfixe des identifiants complets) | obligatoire avec `--apply` |
| `APS_DA_BASE_URL` | URL de l'API | `https://developer.api.autodesk.com/da/us-east/v3` |
| `REVIT_VERSIONS` | versions publiées | `2025 2026 2027` |
| `APPBUNDLE_DIR` | dossier des ZIP | `artifacts/appbundles` |

## 2. Build et tests locaux

Depuis la racine du dépôt (Windows, Linux ou macOS ; Revit inutile) :

```bash
dotnet build EgRoomTracker.sln -c Release                          # cœur + EntryPoint Revit 2027
dotnet test src/Tests/EgRoomTracker.Tests.csproj -c Release        # tests .NET du cœur (EGR-17)
python3 -m unittest discover -s src/Packaging/scripts/tests -v     # tests packaging, sans réseau
python3 -m pip install "jsonschema==4.26.0"                        # une fois (ou dans un venv)
python3 docs/schema/validate_examples.py                           # exemples conformes au schéma
```

Tous doivent réussir avant toute publication. Les mêmes commandes tournent en CI (section 9).

## 3. AppBundles : build et contrôle

```bash
src/Packaging/scripts/build-appbundle.sh                 # 2025, 2026 et 2027
src/Packaging/scripts/build-appbundle.sh 2026            # une seule version
python3 src/Packaging/scripts/appbundle.py check artifacts/appbundles/EgRoomTracker_R2026.zip --revit 2026
```

Le script compile `EgRoomTracker.EntryPoint` en Release pour chaque `RevitVersion`, assemble
`artifacts/appbundles/EgRoomTracker_R<année>.zip` (`EgRoomTracker.bundle/PackageContents.xml`,
`Contents/EgRoomTracker.addin`, DLL de l'application, `DesignAutomationBridge.dll`) puis le contrôle. Le contrôle
échoue si un fichier interdit est présent (`RevitAPI*.dll`, `*.deps.json` inutiles…) ou si un fichier requis manque,
si la série Revit du `PackageContents.xml` ou le framework de la DLL ne correspond pas. `deploy.py test` refait ce
contrôle avant toute écriture côté APS.

Publier de préférence les ZIP produits par la CI sur le commit à publier (artefact `appbundles-<sha>`), ou les
reconstruire localement sur ce même commit : `deploy.py` relit les définitions JSON du dépôt, ZIP et définitions
doivent donc venir du même commit.

## 4. Disponibilité des engines (à chaque publication)

Avant chaque publication, vérifier que les engines `Autodesk.Revit+2025`, `+2026` et `+2027` existent :

- automatiquement : `deploy.py --apply test` appelle `GET /engines` (toutes les pages) et s'arrête **avant toute
  écriture** si un engine manque ;
- en dry-run (sans appel réseau), cette vérification n'a pas lieu : consulter la page *Engines* et les notes de
  version APS, et la politique de cycle de vie des engines (versions retirées) ;
- état connu : la page *Engines* et la politique de cycle de vie listent Revit jusqu'à 2026 ; Revit 2027 est annoncé
  dans les notes de version APS. Si `Autodesk.Revit+2027` est absent, publier sans lui :
  `REVIT_VERSIONS="2025 2026" python3 src/Packaging/scripts/deploy.py --apply test`.

`promote`, `rollback` et `status` ne revérifient pas les engines (ils réutilisent des versions déjà publiées).

## 5. Publication en test

```bash
export APS_NICKNAME=<nickname>
python3 src/Packaging/scripts/deploy.py test             # plan (dry-run) : aucun appel, rien d'écrit
export APS_TOKEN=<jeton 2-legged code:all>
python3 src/Packaging/scripts/deploy.py --apply test     # publication réelle
```

Pour chaque version Revit : nouvelle version d'AppBundle (création si absent) + téléversement du ZIP, alias `test`
créé ou déplacé, nouvelle version d'Activity référençant l'AppBundle `+test`, alias `test` de l'Activity. Noter les
numéros de version affichés (journal de publication, section 8).

Identifiants à utiliser dans les WorkItems de recette : `<nickname>.ExtractRooms_R<année>+test`.

## 6. Recette (EGR-14)

Lancer les WorkItems de recette sur l'Activity `+test` de chaque version (protocole et maquettes : EGR-14). Pour
chaque WorkItem, relever :

- le statut (`GET workitems/:id`) : `success`, `failedInstructions`, `failedUpload`, etc. ; ce statut est conservé
  **3 jours** après la fin du WorkItem ;
- le rapport (`reportUrl`), valable **24 heures** à partir de sa première réception : le télécharger tout de suite.
  Il contient le log console JSON de l'add-in ([`LOGS.md`](LOGS.md)) ;
- `result.json` : conformité au schéma (`docs/schema/result.schema.json`), `summary.status`, anomalies.

Pour une demande de support Autodesk, garder les identifiants de WorkItems (moins de 30 jours) et leurs rapports.

**D6 reste à tester** pendant cette recette : APS téléverse-t-il `result.json` quand l'add-in met
`Succeeded = false`, et quel statut produit une sortie `required: true` absente ? La documentation APS ne le dit
pas. Protocole et cas à provoquer : [`src/Packaging/README.md`, section D6](../src/Packaging/README.md#d6--sortie-en-échec-à-tester)
et [`SPECIFICATIONS.md`](SPECIFICATIONS.md), section 11.

## 7. Promotion en production

Uniquement après une recette réussie, depuis **le même commit** que `test` :

```bash
python3 src/Packaging/scripts/deploy.py promote             # plan
python3 src/Packaging/scripts/deploy.py --apply promote
```

Pour chaque version : l'alias `prod` de l'AppBundle est pointé sur la version actuellement en `test` ; une nouvelle
version d'Activity, qui référence l'AppBundle `+prod`, est créée et reçoit l'alias `prod`. Le script affiche les
versions `prod` précédentes et les commandes de retour arrière correspondantes : **les recopier dans le journal**.
Lors de la première promotion, il n'existe pas de version antérieure : pas de retour arrière par alias possible.

Identifiants de production : `<nickname>.ExtractRooms_R<année>+prod`. Les clients qui utilisent `+prod` basculent
sans changer leurs WorkItems (un alias peut être réassigné à tout moment).

## 8. État, journal et retour arrière

État courant (lecture seule) :

```bash
python3 src/Packaging/scripts/deploy.py --apply status
```

Journal de publication recommandé (description de PR, release GitHub ou ticket), une ligne par version Revit et par
publication (`test` ou promotion `prod`) :

| Date | Commit | Revit | Alias | AppBundle test / prod | Activity test / prod | Version précédente de l'alias (AppBundle / Activity) |
| --- | --- | --- | --- | --- | --- | --- |

DaManager tient ce journal dans un fichier JSON lisible (une ligne ajoutée à chaque publication en test et à chaque
promotion), importe et exporte cette table en Markdown (l'ancienne table sans colonne « Alias » reste lisible) et
protège automatiquement au nettoyage toutes les versions qu'il cite. Le fichier est local par défaut et se choisit
avec `EGR_PUBLICATION_JOURNAL` ; un dossier OneDrive partagé est recommandé (voir
[`tools/EgRoomTracker.DaManager/README.md`](../tools/EgRoomTracker.DaManager/README.md)).

Retour arrière : un alias est repointé (`PATCH …/aliases/prod`) sur une version antérieure, qui reste disponible. Rien
n'est supprimé.

```bash
python3 src/Packaging/scripts/deploy.py rollback --kind activity --revit 2026 --to-version 5          # plan
python3 src/Packaging/scripts/deploy.py --apply rollback --kind activity  --revit 2026 --to-version 5
python3 src/Packaging/scripts/deploy.py --apply rollback --kind appbundle --revit 2026 --to-version 3
python3 src/Packaging/scripts/deploy.py --apply status                                               # contrôle
```

| Situation | Action |
| --- | --- |
| Régression après `promote` (cas général) | repointer **les deux** alias `prod` (Activity et AppBundle) sur les versions notées au journal |
| Seul le code a changé (définition d'Activity identique) | repointer l'AppBundle peut suffire, mais la résolution de l'alias d'AppBundle par une Activity n'est pas documentée : par prudence, repointer aussi l'Activity |
| Problème détecté en test | ne pas promouvoir ; corriger et republier (`test`) ; l'alias `test` peut aussi être repointé (`--alias test`) |
| Une seule version Revit touchée | agir seulement sur cette version (`--revit`) |

Le rollback n'est valable que tant que la version visée existe : ne pas supprimer de versions référencées dans le
journal.

### Quotas à surveiller

- 100 versions au total pour les AppBundles et 100 pour les Activities (comptés séparément), 100 alias ; au-delà,
  la création renvoie une erreur (`403 Maximum number of items exceeded`). Chaque `test` crée une version d'AppBundle
  et une d'Activity par version Revit, chaque `promote` une version d'Activity. `deploy.py` ne supprime rien : le
  nettoyage des anciennes versions (endpoint `DELETE …/versions/:version`) est manuel, en conservant les versions
  du journal.
- 100 appels par minute (`HTTP 429` au-delà) ; ZIP téléversé ≤ 100 Mo ; bundle décompressé ≤ 5 000 Mo (Revit).

## 9. Intégration continue

Workflow : [`.github/workflows/ci.yml`](../.github/workflows/ci.yml). Il s'exécute sur `push` vers `main`, sur les pull requests et à la demande. Un seul job `ubuntu-latest` :

1. SDK .NET de `global.json` + runtime .NET 8, Python 3.12, `jsonschema` 4.26.0 ;
2. `dotnet build EgRoomTracker.sln -c Release` ;
3. `dotnet test` (résultats `.trx` publiés en artefact, même en cas d'échec) ;
4. tests Python du packaging ;
5. `docs/schema/validate_examples.py` ;
6. `build-appbundle.sh 2025 2026 2027` (build + contrôle de chaque ZIP) ;
7. artefact `appbundles-<sha>` : les trois ZIP (conservés 14 jours).

**Pas de déploiement ni de secret en CI** : `permissions: contents: read`, `persist-credentials: false`, aucune
variable `APS_*`. La publication reste manuelle (sections 5 à 8).

Choix du runner Linux : les références Revit (`Nice3point.Revit.Api.*`) et le pont Design Automation
(`Autodesk.Forge.DesignAutomation.Revit`) sont des paquets publics de nuget.org, restaurables sur tout runner ; les
cibles `net*-windows` se compilent sous Linux grâce à `EnableWindowsTargeting` (seules des assemblies de référence
sont utilisées, rien n'est exécuté dans Revit) ; les scripts sont en bash et Python. Le runner Linux est plus rapide
au démarrage et moins coûteux en minutes qu'un runner Windows, sans rien apporter de moins pour ces étapes. Un
runner Windows ne deviendrait utile que pour exécuter du code Revit, ce qui est impossible sur un runner hébergé.

## 10. Liste de contrôle d'une publication

1. CI verte sur le commit à publier ; ZIP récupérés (artefact) ou reconstruits sur ce commit.
2. Engines vérifiés (section 4).
3. `deploy.py test` (plan), puis `--apply test` ; versions notées.
4. Recette EGR-14 sur `+test` (rapports téléchargés sous 24 h, D6 relevé tant qu'il est ouvert).
5. `deploy.py promote` (plan), puis `--apply promote` ; versions précédentes notées.
6. `--apply status` ; `unset APS_TOKEN`.
