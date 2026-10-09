# WorkItemRunner (EGR-20)

WorkItemRunner est l'outil développeur WPF qui lance l'extraction EG_ROOM_TRACKER sur l'Automation API à partir d'un
ZIP de maquette et d'un `settings.json`. Il téléverse les entrées dans OSS, soumet le WorkItem, suit son statut, puis
récupère le rapport APS et `result.json`. Le résultat est affiché (synthèse, locaux, anomalies) et validé contre
[`docs/schema/result.schema.json`](../../docs/schema/result.schema.json). Chaque lancement est conservé dans un
historique local exportable. L'outil s'appuie sur la bibliothèque
[`tools/EgRoomTracker.Aps`](../EgRoomTracker.Aps/README.md).

| Projet | Cible | Contenu |
| --- | --- | --- |
| `tools/EgRoomTracker.WorkItemRunner` | `net8.0-windows`, WPF | Vues (XAML) et services de dialogue. Aucune logique |
| `tools/EgRoomTracker.WorkItemRunner.Core` | `net8.0` | ViewModel (MVVM sans type WPF), arguments du WorkItem, contrôles des entrées, lecture de `result.json`, historique |
| `tools/EgRoomTracker.WorkItemRunner.Tests` | `net8.0` | Tests xUnit sur une Automation API et un OSS simulés en mémoire, sans réseau |

L'application se compile sous Linux et macOS (`EnableWindowsTargeting`), mais **elle ne se lance que sous Windows**.

## Lancer sous Windows

Prérequis : le SDK .NET indiqué dans `global.json` (.NET 10, qui compile les cibles .NET 8) et le runtime .NET 8
Desktop. Aucun Revit n'est nécessaire. L'Activity visée doit déjà être publiée avec l'alias choisi (DaManager ou
`deploy.py`, voir [`docs/PUBLISHING.md`](../../docs/PUBLISHING.md)).

```powershell
# Identifiants : variables de la session uniquement, jamais dans un fichier versionné
$env:APS_CLIENT_ID     = "<client id>"
$env:APS_CLIENT_SECRET = "<client secret>"      # ou, à défaut : $env:APS_TOKEN = "<jeton 2-legged code:all data:read data:write data:create bucket:create bucket:read>"
$env:APS_NICKNAME      = "<nickname>"
# Facultatif
$env:APS_DA_BASE_URL   = "https://developer.api.autodesk.com/da/us-east/v3"
$env:APS_BUCKET        = "<bucket OSS>"          # défaut : egr-workitems-<nickname> ; les clés de bucket sont globales chez APS
$env:EGR_REPO_ROOT     = "C:\src\EG_ROOM_TRACKER" # sinon recherché à partir du dossier de l'exécutable
$env:EGR_RUNNER_DATA   = "D:\egr-runs"           # historique ; défaut : %LOCALAPPDATA%\EgRoomTracker\WorkItemRunner

dotnet run --project tools\EgRoomTracker.WorkItemRunner\EgRoomTracker.WorkItemRunner.csproj
# ou
dotnet build tools\EgRoomTracker.WorkItemRunner\EgRoomTracker.WorkItemRunner.csproj -c Release
tools\EgRoomTracker.WorkItemRunner\bin\Release\net8.0-windows\EgRoomTracker.WorkItemRunner.exe
```

Lancé depuis le dépôt, WorkItemRunner trouve seul la racine (le dossier qui contient `EgRoomTracker.sln` et
`src/Packaging/definitions`). Il y lit les définitions d'Activity, le schéma de `result.json` et l'exemple de
`settings.json`. En fin de session, fermer l'application et supprimer les variables :
`Remove-Item Env:APS_CLIENT_SECRET, Env:APS_TOKEN`.

## Fonctions

| Fonction | Écritures APS | Détail |
| --- | --- | --- |
| **Configuration** : version de Revit (2025, 2026, 2027), alias `test` ou `prod`, nickname, bucket | aucune | L'Activity `<nickname>.ExtractRooms_R<année>+<alias>` doit exister ; son alias est vérifié avant le plan |
| **Entrées** : ZIP de maquette et éditeur de `settings.json` (ouvrir un fichier, charger l'exemple) | aucune | Contrôles ci-dessous ; `settings.json` est validé en direct par `SettingsReader`, comme dans l'add-in |
| **Lancer** : plan, confirmation, bucket (transient), téléversements, URL signées, `POST /workitems` | oui, facturé | Les arguments sont construits à partir des `parameters` de `src/Packaging/definitions/<année>/activity.json` |
| **Suivi** : interrogation de `GET /workitems/{id}` (intervalle réglable) | aucune | **Arrêter le suivi** laisse le WorkItem tourner côté APS |
| **Annuler le WorkItem** : `DELETE /workitems/{id}` | oui | Confirmation renforcée : il faut saisir `CONFIRMER` |
| **Résultats** : rapport APS, `result.json` (synthèse, locaux, anomalies, écarts au schéma) | aucune | Une sortie absente est notée, sans erreur (cas D6 de `src/Packaging/README.md`) |
| **Historique** : un dossier par lancement, export ZIP | aucune | Voir ci-dessous |

**Plan et confirmation** : avant tout téléversement et toute soumission, WorkItemRunner construit le plan à partir de
lectures seules. Ce plan donne l'Activity et sa version, le bucket, chaque entrée (fichier, taille, clé d'objet), chaque
sortie et les avertissements des contrôles. Il est affiché dans une fenêtre modale, et rien n'est écrit sans un clic
sur « Confirmer ». Le plan et la décision sont aussi inscrits au journal.

### Contrôles des entrées

| Contrôle | Effet |
| --- | --- |
| ZIP absent, illisible, vide ou de plus de 5 Gio | bloquant |
| Aucun `.rvt` dans le ZIP | bloquant |
| Plusieurs `.rvt` (préciser `mainModelPath`), chemin de plus de 150 caractères, noms non ASCII sans fichier `.codepage` | avertissement (voir [`docs/INPUTS.md`](../../docs/INPUTS.md)) |
| `settings.json` vide | bloquant |
| `settings.json` refusé par `SettingsReader` | avertissement seulement : le cas D6 envoie volontairement un fichier invalide pour vérifier la réponse du moteur |

### Historique

Chaque lancement est rangé dans `<EGR_RUNNER_DATA>/runs/<date>-<id>/` :

- `run.json` : version, alias, Activity, bucket, identifiant et statut du WorkItem, statistiques, sorties présentes ou
  absentes, statut de `result.json` et nombre d'écarts au schéma ;
- `settings.json` : copie de l'entrée envoyée ;
- `report.txt` : rapport APS (valable 24 h côté APS) ;
- `result.json` : sortie téléchargée.

**Exporter** produit un ZIP de ce dossier, à joindre à un relevé de recette. Le journal donne en fin de lancement la
ligne de relevé D6 : `Relevé : WorkItem <statut>, result.json présent (status …)` ou `… result.json absent.`.

## Limites connues

- Les URL signées d'OSS expirent après 60 minutes au plus. Un WorkItem plus long ne pourrait plus écrire
  `result.json` ; le plan le rappelle.
- Les clés de finalisation des sorties (`uploadKey`) restent en mémoire : si l'application est fermée pendant un
  WorkItem, les sorties de ce lancement ne sont pas récupérées.
- Le bucket est créé en politique `transient` : les objets sont supprimés par APS après 24 heures.

## Secrets

Le secret client, le jeton et les URL signées (entrées, sorties, rapport) ne sont jamais affichés. Ils ne figurent ni
dans le journal, ni dans le plan, ni dans l'historique. Chaque entrée du journal passe par `SecretRedactor`, et l'en-tête
affiche seulement la *source* des identifiants (`APS_CLIENT_ID / APS_CLIENT_SECRET` ou `APS_TOKEN`). Les URL S3 et le
rapport sont lus sans en-tête `Authorization`.

## Tests

```bash
dotnet test tools/EgRoomTracker.WorkItemRunner.Tests/EgRoomTracker.WorkItemRunner.Tests.csproj
```

Le ViewModel est testé contre `FakeAps`, une Automation API et un OSS en mémoire dont le « moteur » écrit
`result.json` sur l'URL signée de sortie. Le faux dépôt reprend les vraies définitions d'Activity, le schéma et les
exemples. Les tests couvrent les cas suivants :

- arguments du WorkItem conformes aux paramètres de l'Activity, paramètre requis manquant ou inconnu refusé ;
- contrôles du ZIP et de `settings.json` ;
- plan refusé, donc aucune écriture ;
- lancement complet : téléversements, soumission, rapport, `result.json` valide, historique et export ;
- WorkItem en échec sans `result.json` (relevé D6) et `result.json` non conforme au schéma ;
- arrêt du suivi et annulation confirmée du WorkItem ;
- alias absent ou ZIP refusé, arrêt avant le plan ;
- aucune URL signée ni aucun jeton dans le journal, le plan ou l'historique.
