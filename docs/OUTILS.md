# Premier déploiement avec les outils (guide du porteur)

Ce guide s'adresse au porteur du projet. Il décrit pas à pas un premier déploiement sous Windows :

1. créer l'application APS ;
2. définir les variables d'environnement ;
3. construire les AppBundles ;
4. publier en test avec **DaManager** ;
5. lancer un premier WorkItem avec **WorkItemRunner**, y compris « Récupérer ».

Il ne remplace pas [`PUBLISHING.md`](PUBLISHING.md), la référence du parcours de publication (recette, promotion,
retour arrière, quotas), ni [`BUILD.md`](BUILD.md), qui couvre le build et les tests. Les deux outils font ce que fait
`src/Packaging/scripts/deploy.py`, avec une interface et une confirmation avant chaque écriture. Leur documentation
détaillée se trouve dans [`tools/EgRoomTracker.DaManager/README.md`](../tools/EgRoomTracker.DaManager/README.md) et
[`tools/EgRoomTracker.WorkItemRunner/README.md`](../tools/EgRoomTracker.WorkItemRunner/README.md).

> Toutes les commandes se lancent depuis la racine du dépôt. Celles en `powershell` passent dans PowerShell, celles en
> `bash` dans Git Bash, installé avec Git pour Windows.

## 0. Prérequis sur le poste

| Élément | Pourquoi |
| --- | --- |
| Windows 10 ou 11 | DaManager et WorkItemRunner sont des applications WPF. Ils ne se lancent que sous Windows |
| SDK .NET 10 (version fixée par `global.json`) et runtime **.NET 8 Desktop** | build du dépôt, exécution des outils (`net8.0-windows`) |
| Python 3.10 ou plus récent | scripts de packaging (`build-appbundle.sh`, `appbundle.py`) et tests |
| Git pour Windows (avec Git Bash) | clone du dépôt, exécution de `build-appbundle.sh` |
| Compte Autodesk Platform Services (APS) | publication et WorkItems, facturés en crédits APS |
| Dossier OneDrive partagé du projet (recommandé) | journal de publication commun |

Revit n'est **pas** nécessaire : le build utilise des assemblies de référence et l'exécution se fait sur les engines
APS.

## 1. Application APS et nickname (une seule fois)

1. Sur le portail APS ([aps.autodesk.com](https://aps.autodesk.com)), créer une application serveur à serveur
   (identifiants client, OAuth 2-legged) et lui donner accès à l'**Automation API** (Design Automation) et à la
   **Data Management API** (stockage OSS).
2. Noter le **Client ID** et le **Client Secret**. Le secret ne doit jamais aller dans le dépôt, dans un fichier
   versionné ou dans un message.
3. Choisir un **nickname** : un nom court, unique chez APS, qui préfixe tous les identifiants
   (`<nickname>.ExtractRooms_R2026+test`). Lettres, chiffres et `_` uniquement. Il ne peut être attribué **que tant
   que l'application n'a aucune donnée**, donc avant la première publication : c'est une opération unique. Il sera
   attribué avec DaManager, bouton **Définir le nickname…** (section 4, étape 2), une fois les variables définies
   (section 2) et le dépôt construit (section 3).

   Secours, sans DaManager : le même appel (`PATCH /forgeapps/me`, documentation de l'Automation API) en PowerShell,
   dans une session où `APS_CLIENT_ID` et `APS_CLIENT_SECRET` sont définis :

   ```powershell
   $basic = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes("$($env:APS_CLIENT_ID):$($env:APS_CLIENT_SECRET)"))
   $token = (Invoke-RestMethod -Method Post -Uri "https://developer.api.autodesk.com/authentication/v2/token" `
       -Headers @{ Authorization = "Basic $basic" } -ContentType "application/x-www-form-urlencoded" `
       -Body "grant_type=client_credentials&scope=code:all").access_token
   Invoke-RestMethod -Method Patch -Uri "https://developer.api.autodesk.com/da/us-east/v3/forgeapps/me" `
       -Headers @{ Authorization = "Bearer $token" } -ContentType "application/json" -Body '{"nickname":"<nickname>"}'
   Remove-Variable token, basic
   ```

   Une erreur `409 Conflict` signifie que ce nickname est déjà pris par une autre application, ou que l'application a
   déjà des données.

## 2. Variables d'environnement

Les outils et `deploy.py` lisent les variables suivantes. Ce sont les noms exacts utilisés par le code.

| Variable | Utilisée par | Rôle | Défaut |
| --- | --- | --- | --- |
| `APS_CLIENT_ID` | DaManager, WorkItemRunner | ID client APS (OAuth 2-legged) | — |
| `APS_CLIENT_SECRET` | DaManager, WorkItemRunner | secret client | — |
| `APS_TOKEN` | `deploy.py` (obligatoire), outils (à défaut des deux précédentes) | jeton 2-legged temporaire | — |
| `APS_NICKNAME` | tous | nickname de l'application (section 1) | — (obligatoire) |
| `APS_DA_BASE_URL` | tous | URL de l'Automation API | `https://developer.api.autodesk.com/da/us-east/v3` |
| `APS_BUCKET` | WorkItemRunner | bucket OSS des entrées et sorties des WorkItems ; **recommandé** | `egr-workitems-` suivi d'une empreinte de l'ID client |
| `EGR_PUBLICATION_JOURNAL` | DaManager | fichier du journal de publication ; **dossier OneDrive partagé recommandé** | `%LOCALAPPDATA%\EgRoomTracker\DaManager\publication-journal.json` |
| `EGR_REPO_ROOT` | DaManager, WorkItemRunner | racine du dépôt (définitions, schéma, exemples) | recherchée à partir du dossier de l'exécutable |
| `EGR_RUNNER_DATA` | WorkItemRunner | historique local des lancements | `%LOCALAPPDATA%\EgRoomTracker\WorkItemRunner` |
| `REVIT_VERSIONS` | DaManager, `deploy.py` | versions publiées | `2025 2026 2027` |
| `APPBUNDLE_DIR` | DaManager, `deploy.py` | dossier des ZIP d'AppBundles | `artifacts\appbundles` |

Les clés de bucket sont **globales** chez APS (toutes applications confondues). Elles s'écrivent avec
`[-_.a-z0-9]` et comptent de 3 à 128 caractères. Par exemple `egr-workitems-<nickname>`, si ce nom est libre.

**Valeurs non secrètes** : les enregistrer une fois pour toutes au niveau de l'utilisateur (prises en compte dans les
nouvelles fenêtres) :

```powershell
[Environment]::SetEnvironmentVariable("APS_NICKNAME", "<nickname>", "User")
[Environment]::SetEnvironmentVariable("APS_BUCKET", "egr-workitems-<nickname>", "User")
[Environment]::SetEnvironmentVariable("EGR_PUBLICATION_JOURNAL", "$env:OneDrive\EG_ROOM_TRACKER\publication-journal.json", "User")
[Environment]::SetEnvironmentVariable("EGR_REPO_ROOT", "C:\src\EG_ROOM_TRACKER", "User")
```

Adapter le chemin du journal au dossier OneDrive **partagé** du projet tel qu'il apparaît sur le poste (par exemple
`C:\Users\<nom>\OneDrive - <organisation>\EG_ROOM_TRACKER\`), le même pour tous ceux qui publient.

**Secrets** : seulement dans la session PowerShell courante, à chaque fois, puis supprimés en fin de session :

```powershell
$env:APS_CLIENT_ID     = "<client id>"
$env:APS_CLIENT_SECRET = "<client secret>"
# ... travail avec DaManager et WorkItemRunner (sections 4 et 5), lancés depuis cette fenêtre ...
Remove-Item Env:APS_CLIENT_SECRET, Env:APS_CLIENT_ID
```

Les outils n'affichent jamais le secret, le jeton ni les URL signées. Leur en-tête indique seulement la *source* des
identifiants.

## 3. Récupérer le dépôt, compiler, tester, construire les AppBundles

```powershell
git clone https://github.com/benattiahakim-maker/EG_ROOM_TRACKER.git C:\src\EG_ROOM_TRACKER
cd C:\src\EG_ROOM_TRACKER
dotnet build EgRoomTracker.sln -c Release
dotnet test EgRoomTracker.sln -c Release
```

Tous les tests doivent passer avant une publication (voir PUBLISHING section 2, qui ajoute les tests Python et la
validation des exemples).

Construction et contrôle des trois AppBundles, dans **Git Bash** :

```bash
src/Packaging/scripts/build-appbundle.sh            # 2025, 2026 et 2027 -> artifacts/appbundles/EgRoomTracker_R<année>.zip
src/Packaging/scripts/build-appbundle.sh 2026       # une seule version
```

Si `python3` n'existe pas sous Windows, indiquer l'interpréteur au script : `PYTHON=py src/Packaging/scripts/build-appbundle.sh`.

Les ZIP et les définitions d'Activity doivent venir **du même commit**. On peut aussi publier les ZIP produits par la
CI sur ce commit (artefact `appbundles-<sha>`), décompressés dans `artifacts\appbundles` (PUBLISHING section 3).

## 4. Publication en test avec DaManager

Depuis la fenêtre PowerShell où les secrets sont définis :

```powershell
dotnet run --project tools\EgRoomTracker.DaManager\EgRoomTracker.DaManager.csproj
```

1. **Vérifier l'en-tête** : la source des identifiants (`APS_CLIENT_ID / APS_CLIENT_SECRET (OAuth 2-legged)`), l'URL
   de l'API et le **commit** du dépôt.
2. **Configuration** : nickname, racine du dépôt, dossier des ZIP (`artifacts\appbundles`), versions Revit cochées.
   **Premier déploiement seulement** : saisir le nickname choisi (section 1) dans le champ Nickname et cliquer sur
   **Définir le nickname…**. Le plan montre le nickname actuel (« aucun nickname » pour une application neuve), le
   nickname cible, la requête `PATCH /forgeapps/me` et l'avertissement : opération unique, acceptée seulement tant que
   l'application n'a aucune donnée. Cliquer sur **Confirmer** ; DaManager relit ensuite le nickname. Si
   `APS_NICKNAME` n'était pas encore défini, le définir maintenant (section 2) pour les lancements suivants.
3. **Actualiser** : la liste des engines `Autodesk.Revit+2025/2026/2027` doit être complète. Si un engine manque,
   décocher cette version (PUBLISHING section 4). Au premier déploiement, toutes les ressources sont « inexistant ».
4. **Journal de publication** : vérifier que le champ « Fichier » pointe sur le dossier OneDrive partagé. Le
   fichier est partagé en « dernier écrit gagnant » : DaManager le relit juste avant d'écrire et fusionne les lignes,
   mais deux postes ne doivent pas publier en même temps, et il faut laisser OneDrive synchroniser entre deux
   publications (détails : PUBLISHING section 8).
5. **Publier en test** : DaManager contrôle les ZIP puis affiche le plan (création des AppBundles et des Activities,
   alias `test`). Le relire et cliquer sur **Confirmer**. Rien n'est écrit sans cette confirmation.
6. L'état est relu automatiquement. Le tableau doit montrer `EgRoomTracker_R<année>` et `ExtractRooms_R<année>` en
   `v1` avec l'alias `test → v1`. Une ligne `test` est ajoutée au journal de publication.

Équivalent en ligne de commande, avec un jeton `APS_TOKEN` (scope `code:all`) : `deploy.py test`, puis
`deploy.py --apply test` (PUBLISHING section 5).

## 5. Premier WorkItem avec WorkItemRunner

Il faut un ZIP de maquette, qui contient le `.rvt` (voir [`INPUTS.md`](INPUTS.md)). Pour la recette, utiliser les
maquettes de [`RECETTE.md`](RECETTE.md).

```powershell
dotnet run --project tools\EgRoomTracker.WorkItemRunner\EgRoomTracker.WorkItemRunner.csproj
```

1. **Configuration** : version Revit (par exemple 2026), alias **`test`**, nickname, bucket (`APS_BUCKET`).
2. **Entrées** : choisir le ZIP, puis **Exemple** pour charger `docs/examples/settings.example.json`, ou **Ouvrir…**.
   Les contrôles s'affichent aussitôt : un ZIP sans `.rvt` est refusé, et plusieurs `.rvt` donnent un avertissement
   (préciser `mainModelPath`).
3. **Lancer…** : le plan indique l'Activity `<nickname>.ExtractRooms_R2026+test` et sa version, le bucket, les objets
   envoyés et le rappel que **les URL signées expirent après 60 minutes**. Cliquer sur **Confirmer** : WorkItemRunner
   crée le bucket si besoin, téléverse le ZIP et `settings.json`, puis soumet le WorkItem (facturé).
4. **Suivi** : le statut passe de `pending` à `inprogress`, puis à `success` ou à un statut d'échec. À la fin,
   l'onglet **Résultat** affiche la synthèse, les locaux, les anomalies et les écarts au schéma. L'onglet
   **Rapport APS** affiche le rapport, et le **Journal** la ligne de relevé
   (`Relevé : WorkItem success, result.json présent (status …)`).
5. **Récupérer** : on n'est pas obligé d'attendre. Il suffit de cliquer sur **Arrêter le suivi**, ou de fermer
   l'application : le WorkItem continue chez APS. Plus tard, rouvrir WorkItemRunner, sélectionner le lancement dans
   l'**Historique** et cliquer sur **Récupérer**. L'outil relit le statut, puis télécharge le rapport et `result.json`
   par une URL signée de lecture demandée à l'instant. Si le WorkItem tourne encore, il met seulement le statut à
   jour : réessayer plus tard. Délais APS : statut conservé 3 jours, objets du bucket `transient` 24 heures.
6. **Exporter…** produit un ZIP du lancement (`run.json`, `settings.json`, rapport, `result.json`), à joindre au
   relevé de recette.

## 6. Ensuite

- **Recette** sur `+test` pour chaque version Revit : [`RECETTE.md`](RECETTE.md) et PUBLISHING section 6, y compris
  le cas D6.
- **Promotion** : dans DaManager, cliquer sur **Promouvoir test → prod**, depuis le même commit, après une recette
  réussie (PUBLISHING section 7). Une ligne `prod` est ajoutée au journal avec la version précédente, cible du retour
  arrière.
- **Retour arrière** : utiliser la carte « Alias / retour arrière » de DaManager, avec les versions du journal
  (PUBLISHING section 8).
- **Nettoyage des versions** (quota de 100 versions) : utiliser la carte « Nettoyage des versions ». Les versions
  aliasées ou citées par le journal ne sont jamais supprimées, et il faut saisir `SUPPRIMER` pour confirmer.

## 7. Problèmes fréquents

| Symptôme | Cause probable et action |
| --- | --- |
| « aucun identifiant » dans l'en-tête | variables non définies dans la fenêtre qui a lancé l'outil : les définir, puis relancer depuis cette fenêtre |
| `401` ou `403` | secret erroné ou API non activées pour l'application APS (section 1) |
| engine `Autodesk.Revit+2027` absent | décocher 2027 et publier les autres versions (PUBLISHING section 4) |
| « Activity … +test introuvable » dans WorkItemRunner | publier d'abord en test (section 4), avec le même nickname |
| `409` à la création du bucket | clé de bucket déjà prise par une autre application : choisir un autre `APS_BUCKET` |
| « Bucket … invalide » | clé hors de `[-_.a-z0-9]{3,128}` (majuscules, espaces…) |
| WorkItem en échec, `result.json` absent | lire l'onglet Rapport APS ; c'est aussi le comportement à relever pour D6 |
| `409` sur « Définir le nickname… » | nickname déjà pris par une autre application : en choisir un autre ; ou application qui a déjà des données : garder le nickname actuel (seul `DELETE /forgeapps/me`, qui efface tout, permettrait d'en changer) |
| Copie en conflit du journal dans OneDrive (`publication-journal-<poste>.json`) | deux postes ont écrit presque en même temps : DaManager « Importer… » cette copie (fusion sans doublon), puis la supprimer |
| Journal « illisible » dans DaManager | fichier modifié à la main ou corrompu : corriger ou restaurer la version OneDrive précédente. Le nettoyage est bloqué tant qu'il est illisible |
