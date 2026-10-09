# DaManager (EGR-19)

DaManager est l'outil développeur WPF qui gère l'application EG_ROOM_TRACKER sur l'Automation API. Il couvre les
engines, les AppBundles, les Activities, les versions et les alias : publication en test, promotion, retour arrière
et nettoyage des versions. Il suit le parcours de [`docs/PUBLISHING.md`](../../docs/PUBLISHING.md) et s'appuie sur la
bibliothèque [`tools/EgRoomTracker.Aps`](../EgRoomTracker.Aps/README.md), qui porte `deploy.py`.

| Projet | Cible | Contenu |
| --- | --- | --- |
| `tools/EgRoomTracker.DaManager` | `net8.0-windows`, WPF | Vues (XAML) et services de dialogue. Aucune logique |
| `tools/EgRoomTracker.DaManager.Core` | `net8.0` | ViewModels (MVVM sans type WPF), plans d'opérations, options, services APS |
| `tools/EgRoomTracker.DaManager.Tests` | `net8.0` | Tests xUnit sur une Automation API simulée en mémoire, sans réseau |

L'application se compile sous Linux et macOS (`EnableWindowsTargeting`), mais **elle ne se lance que sous Windows**.

## Lancer sous Windows

Prérequis : le SDK .NET indiqué dans `global.json` (.NET 10, qui compile les cibles .NET 8) et le runtime .NET 8
Desktop. Aucun Revit n'est nécessaire.

```powershell
# Identifiants : variables de la session uniquement, jamais dans un fichier versionné
$env:APS_CLIENT_ID     = "<client id>"
$env:APS_CLIENT_SECRET = "<client secret>"      # ou, à défaut : $env:APS_TOKEN = "<jeton 2-legged code:all>"
$env:APS_NICKNAME      = "<nickname>"
# Facultatif (mêmes variables que deploy.py)
$env:APS_DA_BASE_URL   = "https://developer.api.autodesk.com/da/us-east/v3"
$env:REVIT_VERSIONS    = "2025 2026 2027"      # versions cochées au démarrage
$env:APPBUNDLE_DIR     = "artifacts\appbundles" # ZIP produits par build-appbundle.sh ou par la CI
$env:EGR_REPO_ROOT     = "C:\src\EG_ROOM_TRACKER" # sinon recherché à partir du dossier de l'exécutable

dotnet run --project tools\EgRoomTracker.DaManager\EgRoomTracker.DaManager.csproj
# ou
dotnet build tools\EgRoomTracker.DaManager\EgRoomTracker.DaManager.csproj -c Release
tools\EgRoomTracker.DaManager\bin\Release\net8.0-windows\EgRoomTracker.DaManager.exe
```

Lancé depuis le dépôt, DaManager trouve seul la racine (le dossier qui contient `EgRoomTracker.sln` et
`src/Packaging/definitions`). En fin de session, fermer l'application et supprimer les variables :
`Remove-Item Env:APS_CLIENT_SECRET, Env:APS_TOKEN`.

## Fonctions

| Fonction | Écritures APS | Correspondance |
| --- | --- | --- |
| **Actualiser** : engines Revit, AppBundles et Activities du dépôt (versions, alias), liste complète de l'application, alertes sur les engines manquants et sur le quota | aucune | `deploy.py status`, PUBLISHING section 4 |
| **Publier en test** : ZIP contrôlés (`appbundle.py check` porté en C#), nouvelle version d'AppBundle et téléversement, alias `test`, nouvelle version d'Activity, alias `test` | oui | `deploy.py test`, PUBLISHING section 5 |
| **Promouvoir test → prod** : alias `prod` de l'AppBundle sur la version en `test`, nouvelle version d'Activity `+prod`, versions précédentes affichées pour le journal | oui | `deploy.py promote`, PUBLISHING section 7 |
| **Alias / retour arrière** : créer ou déplacer un alias de la ressource sélectionnée vers une version existante | oui | `deploy.py rollback`, PUBLISHING section 8 |
| **Nettoyage des versions** : voir ci-dessous | suppressions | PUBLISHING section 8, quotas |

**Plan et confirmation (#35)** : avant chaque écriture, DaManager construit le plan à partir de lectures seules. Ce
plan donne les requêtes prévues, les versions actuelles et suivantes, ainsi que le commit du dépôt, car ZIP et
définitions doivent venir du même commit. Il est affiché dans une fenêtre modale, et rien n'est écrit sans un clic sur
« Confirmer ». Le plan et la décision sont aussi inscrits au journal. Après chaque écriture, l'état est relu.

## Nettoyage des versions

APS limite chaque type à 100 versions (AppBundles et Activities séparément), et `deploy.py` ne supprime rien.
DaManager :

1. lit les versions et les alias de la ressource sélectionnée ;
2. conserve **toujours** les versions référencées par un alias (`test`, `prod` ou autre), les *N* plus récentes
   (*N* ≥ 1, 10 par défaut) et les versions protégées saisies (celles du journal de publication, nécessaires au
   retour arrière) ;
3. affiche la liste complète des versions à supprimer, avec la raison de conservation de chacune des autres ;
4. demande une confirmation explicite : il faut saisir `SUPPRIMER` dans la fenêtre ;
5. relit les alias juste avant de supprimer. Si une version du plan est devenue référencée entre-temps, rien n'est
   supprimé.

Une suppression est définitive : la version supprimée ne peut plus servir de retour arrière.

## Secrets

Le secret client, le jeton, les champs `formData` S3 et les URL signées ne sont jamais affichés. Ils ne figurent ni
dans le journal ni sur le disque. Chaque entrée du journal passe par `SecretRedactor`, et l'en-tête affiche seulement
la *source* des identifiants (`APS_CLIENT_ID / APS_CLIENT_SECRET` ou `APS_TOKEN`).

## Tests

```bash
dotnet test tools/EgRoomTracker.DaManager.Tests/EgRoomTracker.DaManager.Tests.csproj
```

Les ViewModels sont testés contre `FakeApsServer`, une Automation API en mémoire qui échoue si l'on tente de supprimer
une version référencée par un alias. Les tests couvrent les cas suivants :

- plan refusé, donc aucune écriture ;
- publication, promotion et retour arrière confirmés ;
- ZIP refusé, arrêt avant la confirmation ;
- nettoyage avec versions aliasées et protégées ;
- erreurs journalisées sans secret ;
- identifiants absents.
