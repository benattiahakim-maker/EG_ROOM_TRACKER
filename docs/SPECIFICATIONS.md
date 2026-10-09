# EG_ROOM_TRACKER — Spécifications techniques

Version 0.1 — 9 octobre 2026 — Proposition de référence

## 1. Objectif et périmètre

Créer une application C# exécutée exclusivement dans Autodesk Platform Services (APS), **Design Automation for Revit**, pour extraire les pièces d'un document RVT reçu dans une archive ZIP. Livrer le code, les builds par version, les AppBundles et les définitions d'Activities nécessaires à leur exécution.

Versions cibles : Revit 2025, 2026 et 2027. Vérifier les moteurs réellement disponibles via l'API APS avant publication. Adapter et tester les dépendances Revit et le runtime .NET au moteur utilisé ; ne pas supposer un runtime identique pour les trois versions.

Hors périmètre : interface web, backend métier d'envoi des WorkItems, Viewer, modification ou sauvegarde du RVT source, synchronisation ACC, déduction géométrique des matériaux de finition, comparaison entre versions de maquettes.

## 2. Contrat d'entrée et de sortie

Entrée APS : archive ZIP de maquette et fichier `settings.json`. L'Activity déclare explicitement la décompression de l'entrée et les chemins locaux. Le chemin relatif du RVT principal est obligatoire si plusieurs RVT sont présents. Un ZIP avec un seul RVT peut être résolu automatiquement. Ne jamais sélectionner arbitrairement le premier RVT.

La stratégie d'ouverture retenue dans le prototype doit être cohérente avec l'Activity : RVT principal ouvert par RevitCoreConsole et fourni par `DesignAutomationData.RevitDoc`, ou ouverture explicite du RVT par l'application si nécessaire. Documenter une seule stratégie validée, son propriétaire de fermeture et son comportement pour les documents collaboratifs.

Paramètres : `schemaVersion`, `mainModelPath`, `phaseIds` facultatif (toutes les phases par défaut), `volumeMode` (`preserve` par défaut, `compute` facultatif), mapping facultatif de paramètres partagés par GUID. Les chemins restent confinés au répertoire d'entrée. Les liens présents dans le ZIP conservent une arborescence documentée ; les liens absents sont signalés. Le MVP collecte les pièces du document principal uniquement, sans parcourir les pièces des documents liés.

Sortie : archive de résultats comprenant `rooms.json`, `rooms.csv`, `summary.json` et `execution.jsonl`. Le JSON constitue le contrat de référence. Le CSV est UTF-8, échappe les cellules et protège contre l'interprétation des chaînes comme formules de tableur. Le séparateur et le format décimal sont documentés et constants.

`summary.json` contient : identifiant de corrélation, version de schéma, version de l'extracteur, version/build Revit, moteur APS lorsque disponible, nom/chemin relatif de la source, dates UTC, options appliquées, nombres de pièces collectées/exportées/en anomalie, statut final et codes d'erreur. Le rapport natif APS reste disponible via le WorkItem ; son téléchargement appartient au consommateur, hors bundle.

## 3. Données des pièces

Collecter les instances `Autodesk.Revit.DB.Architecture.Room`, catégorie `OST_Rooms`, sans filtre dépendant de la vue active. Ne pas confondre pièces, espaces MEP, surfaces ou étiquettes.

| Champ | Source et règle |
| --- | --- |
| `elementId` | `ElementId.Value`, exporté comme chaîne pour préserver l'identifiant entier |
| `uniqueId` | `Element.UniqueId`, accompagné de l'identité du document ; ne pas utiliser le numéro de pièce comme clé |
| `name` | Paramètre natif `ROOM_NAME` ; éviter un nom qui concatène nom et numéro |
| `number` | `Room.Number`, chaîne conservant les zéros initiaux |
| `areaM2` | `Room.Area`, conversion par `UnitUtils` vers m² |
| `unboundedHeightM` | `Room.UnboundedHeight`, conversion vers mètres ; hauteur non bornée, pas hauteur libre sous plafond |
| `level` | `Room.LevelId` puis niveau du document : ID, UniqueId, nom ; null si introuvable |
| `occupancy` | `ROOM_OCCUPANCY` : usage/occupation textuel, pas nombre d'occupants |
| `volumeM3` | `Room.Volume`, conversion vers m³ et état de calcul séparé |
| `finishes.floor` | `ROOM_FINISH_FLOOR` |
| `finishes.wall` | `ROOM_FINISH_WALL` |
| `finishes.ceiling` | `ROOM_FINISH_CEILING` |
| `finishes.base` | `ROOM_FINISH_BASE`, finition des plinthes |
| `phase` | ID et nom de phase de la pièce |
| `designOption` | ID et nom de variante lorsque applicable |
| `qualityFlags` | Liste de codes d'anomalie, avec détails exploitables |

Les finitions sont les textes renseignés sur la pièce, sans inférence depuis les murs, sols ou plafonds. Utiliser des identifiants de paramètres natifs et les GUID pour les paramètres partagés ; aucun mapping principal basé sur les libellés français/anglais. Ne pas employer `AsValueString()` pour sérialiser les quantités numériques.

Différencier paramètre absent, présent sans valeur, chaîne vide et valeur numérique nulle. Les nombres restent numériques dans le JSON ; arrondir uniquement la présentation CSV selon une précision documentée.

Conserver toutes les pièces, y compris non placées ou de surface nulle, avec indicateurs. Ne pas déduire une cause précise uniquement d'une surface nulle : distinguer `UNPLACED` si l'absence de placement est vérifiée, et `ZERO_AREA` autrement. Signaler les doublons de numéro, sans supprimer ni fusionner de pièces.

Volume : en mode `preserve`, lire le réglage du document ; lorsque le calcul est désactivé, exporter `volumeM3: null` et `volumeStatus: notComputed`, jamais présenter zéro comme un volume calculé. En mode `compute`, activer le calcul dans une transaction sur la copie de travail, régénérer et lire les résultats. Ne pas sauvegarder le RVT et consigner ce choix. Ce mode doit être validé sur les trois moteurs.

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
| `IExportService` | Écriture déterministe des JSON/CSV et publication locale des résultats |
| `ILogService` | Journal structuré console/fichier, niveaux et contexte |

Organisation proposée : `EntryPoint/`, `Application/`, `Revit/`, `Contracts/`, `Export/`, `Logging/`, `Packaging/`, `Tests/`. Réutiliser le cœur fonctionnel ; isoler les adaptations et références propres à chaque version. DTO sans objets Revit pour séparer lecture et sérialisation. Injection simple des dépendances ; éviter une abstraction sans responsabilité réelle.

Les appels Revit API restent sur le contexte d'exécution autorisé : aucun traitement des éléments/documents Revit via `Task.Run` ou accès parallèle. Aucune dépendance à `ActiveUIDocument`, vue active, session desktop ou interaction utilisateur.

## 5. Exécution sans interface et logs

Interdiction de `MessageBox`, `TaskDialog`, fenêtres, sélecteurs de fichiers et demandes de confirmation. Toute information passe par `ILogService`.

Format JSON Lines, niveaux `Info`, `Warning`, `Error` : `timestampUtc`, `level`, `eventCode`, `correlationId`, `stage`, `message`, `documentPath` relatif, `roomElementId` si pertinent, durée et exception détaillée si présente. Écrire les événements essentiels à la console pour le rapport APS et tous les événements dans le fichier de sortie. Flush en fin de traitement et dans le bloc de terminaison. Ne pas journaliser tokens, secrets ni URL signées.

Événements minimaux : début, configuration validée, document accessible, collecte terminée, anomalies, export terminé, échec et bilan final. Une pièce défectueuse ne doit pas produire des milliers de messages redondants : détail limité et compteur agrégé.

Les avertissements Revit à l'ouverture nécessitent une politique explicite, validée dans l'environnement Automation. Ne jamais supprimer aveuglément tous les avertissements ni continuer après une corruption bloquante.

## 6. Erreurs et cohérence des résultats

Configuration invalide, RVT ambigu/absent, document non ouvrable, version incompatible ou export impossible : échec global et code explicite. Propriété manquante : avertissement et valeur documentée. Erreur limitée à une pièce : exporter son identité et un indicateur `EXTRACTION_ERROR`, continuer les autres et terminer `succeededWithWarnings`. Ne jamais perdre une pièce silencieusement.

Écrire les exports via fichiers temporaires puis finalisation locale ; aucun résultat incomplet annoncé comme valide. Un échec peut empêcher APS d'envoyer l'archive de sortie : le log console et le rapport APS sont donc indispensables pour le diagnostic. Aucun appel réseau métier nécessaire dans le bundle.

## 7. Builds, AppBundles et Activities

Préparer trois artefacts explicitement versionnés et testés : 2025, 2026, 2027. Références API provenant de chaque SDK cible, non embarquées si fournies par le moteur. Inclure uniquement les dépendances applicatives nécessaires et les manifestes exigés par APS/Revit.

Les Activities déclarent le moteur, l'AppBundle, la commande RevitCoreConsole, l'entrée ZIP/configuration et l'archive de sortie. Valider les chemins après décompression dans un vrai WorkItem. Versionner bundles et Activities ; utiliser des alias de test puis de production avec procédure de retour à la version précédente.

Le pipeline compile, vérifie le contenu des ZIP, publie une version de test et exécute les tests APS. Secrets fournis par le système de déploiement, jamais dans les bundles. La disponibilité des moteurs et l'évolution des runtimes sont vérifiées lors de chaque publication.

## 8. Recette et critères de livraison

Jeu de maquettes de référence pour chaque année : pièces renseignées, accents/zéros initiaux, champs vides, non placées/surface nulle, plusieurs phases et variantes, calcul de volume désactivé/activé, finitions, paramètres partagés, liens manquants et maquette collaborative. Inclure ZIP ambigu et RVT incompatible.

Critères : toutes les pièces attendues sont représentées ; quantités conformes à Revit après conversion (tolérances proposées : 0,001 m, 0,001 m², 0,001 m³, à ajuster au jeu de référence) ; distinction null/zéro correcte ; aucun dialogue ; logs et résultats récupérables ; résultat APS correct en succès et en échec ; source inchangée ; exécution réussie sur les trois moteurs. Mesurer temps et taille sur une maquette représentative avant de fixer les objectifs de performance.

Livrables : solution C#, documentation de build et exécution, schéma JSON, exemples de configuration et résultats, trois bundles, définitions d'Activities, preuves de recette et procédure de publication/rollback.

## 9. Hypothèses métier à confirmer

Par défaut : pièces du document principal, toutes phases, finitions textuelles, occupation textuelle, hauteur non bornée, volumes existants sans recalcul. Ces choix n'empêchent pas la préparation technique ; toute extension aux pièces des liens, hauteur géométrique ou nombre d'occupants constitue une évolution explicite.

## 10. Références techniques consultées

- [Automation API APS](https://aps.autodesk.com/apis-and-services/automation-api)
- [Guide Autodesk : Rooms](https://help.autodesk.com/cloudhelp/2024/ENU/Revit-API/files/Revit_API_Developers_Guide/Discipline_Specific_Functionality/Architecture/Revit_API_Revit_API_Developers_Guide_Discipline_Specific_Functionality_Architecture_Rooms_html.html)
- [Référence Room, Revit 2026](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/75c9d2c7-a402-ea8b-9e7c-f8bc3510bbd5.htm)
- [Annonce Autodesk : moteurs 2027](https://blog.autodesk.io/automation-api-old-engine-removal-and-add-2027-engines/)
- [Annonce migration du moteur 2026 vers 2026.5/.NET 10](https://aps.autodesk.com/blog/revit-automation-engine-upgrading-revit-20265-and-net-10-september-21-2026)

Les annonces ne remplacent pas la vérification authentifiée des moteurs et un test réel des AppBundles.
