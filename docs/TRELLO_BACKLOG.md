# Backlog EG_ROOM_TRACKER — Design Automation for Revit

État : cartes suivies sur le [tableau Trello EG_ROOM_TRACKER](https://trello.com/b/dsFoEv4N/egroomtracker) et en miroir dans les issues GitHub ([correspondance](BACKLOG.md)). Ce document est la référence textuelle des cartes, alignée sur les spécifications v0.3. Chaque carte indique son état au 9 octobre 2026 (`main` = 9205e2c, vérifié avec `gh`) ; le tableau récapitulatif est dans [BACKLOG.md](BACKLOG.md). La ligne « Liste » reprend la liste d'origine de la carte ; l'état réel fait foi.

Listes : Cadrage → À faire → En cours → À vérifier → Terminé. Liste complémentaire : Bloqué.

Labels : Architecture, Revit API, APS, Logs, Export, Qualité, Packaging. Priorités P0 (fondation), P1 (MVP), P2 (option) ; sur Trello, couleur rouge = P0, orange = P1, jaune = P2. Aucune assignation ni échéance inventée.

## EGR-01 — Valider le contrat d'extraction
Liste : Cadrage | Priorité : P0 | Labels : Architecture, Revit API
État : En attente — issue #1 ouverte ; aucune PR de code. Bloquant : approbation du propriétaire (contrat, D3, D7).

Fixer les choix métier décrits dans les spécifications : document principal, phases, hauteur non bornée, occupation textuelle, finitions textuelles et mode de volume. Approuver le contrat `result.json` (schéma et exemple) et trancher les décisions ouvertes D3 et D7.

Acceptation : choix documentés ; champs JSON, unités et schéma `result.json` approuvés ; décisions D3 (choix métier) et D7 (précision numérique) tranchées ; extensions clairement identifiées.

## EGR-02 — Valider les moteurs et SDK 2025–2027
Liste : À faire | Priorité : P0 | Labels : APS, Packaging
État : En attente — issue #2 ouverte ; aucune PR. Bloquant : accès APS (moteurs, runtimes, WorkItem minimal).

Interroger les moteurs APS, consigner leurs IDs et runtimes effectifs ; construire un bundle minimal pour chaque version.

Acceptation : un WorkItem sans dialogue réussit sur chaque moteur ; versions/API/runtime consignés.

## EGR-03 — Créer la solution organisée par services
Liste : À faire | Priorité : P0 | Labels : Architecture
État : Terminé — issue #3 fermée ; PR #20, #22.

Créer EntryPoint, Application, Revit, Contracts, Export et Logging. Point d'entrée mince, dépendances explicites, DTO indépendants de Revit.

Acceptation : responsabilités conformes aux spécifications ; compilation pour chaque cible ; aucun accès UI ni appel Revit parallèle.

Dépendances : EGR-02.

## EGR-04 — Implémenter le service de logs
Liste : À faire | Priorité : P0 | Labels : Logs
État : Terminé — issue #4 fermée ; PR #21, #22 ; D8 tranchée (console uniquement).

Logs JSON Lines sur la console uniquement, récupérés via le rapport APS ; niveaux, codes, corrélation, contexte pièce et exceptions ; flush de terminaison. Aucun fichier de log dans le résultat ; trancher la décision D8 (fichier de débogage facultatif).

Acceptation : succès, avertissement et exception visibles dans le rapport APS ; aucun secret ; rapport APS exploitable même en échec ; décision D8 tranchée.

Dépendances : EGR-03.

## EGR-05 — Définir et valider les entrées ZIP/configuration
Liste : À faire | Priorité : P0 | Labels : APS, Architecture
État : Terminé — issue #5 fermée ; PR #27, #32 ; D4 tranchée.

Configurer décompression, chemins locaux, RVT principal et settings.json conforme à la section 2 et à l'exemple de la section 10.1. Refuser chemins sortants, configuration invalide et RVT ambigu. Trancher la décision D4 (arborescence des liens).

Acceptation : ZIP à RVT unique accepté ; plusieurs RVT nécessitent un chemin ; erreurs explicites avec les codes de la section 9.2 ; arborescence des liens documentée (D4).

Dépendances : EGR-03, EGR-04.

## EGR-06 — Gérer le document Revit en environnement Automation
Liste : À faire | Priorité : P0 | Labels : APS, Revit API
État : Terminé — issue #6 fermée ; PR #23, #26 ; D1 et D2 tranchées. Validation dans un WorkItem réel en EGR-14.

Valider ouverture par Activity/contexte SDK, propriété de fermeture, maquettes collaboratives et liens. Aucun dialogue. Trancher les décisions D1 (stratégie d'ouverture) et D2 (politique des avertissements Revit).

Acceptation : document accessible dans un WorkItem réel ; liens manquants signalés (LINK_MISSING) ; source non sauvegardée ; fichier incompatible en échec (REVIT_VERSION_INCOMPATIBLE) ; décisions D1 et D2 documentées dans les spécifications.

Dépendances : EGR-02, EGR-05.

## EGR-07 — Collecter toutes les pièces
Liste : À faire | Priorité : P1 | Labels : Revit API
État : Terminé — issue #7 fermée ; PR #24, #31. Comptage sur le jeu de référence en EGR-18.

Collecter les Room du document principal, toutes phases par défaut, variantes et pièces non placées incluses.

Acceptation : nombre conforme au jeu de référence (EGR-18) ; aucun espace MEP, surface ou tag ; aucun filtre de vue active.

Dépendances : EGR-06, EGR-18.

## EGR-08 — Lire identité, nom, numéro, niveau et occupation
Liste : À faire | Priorité : P1 | Labels : Revit API
État : Terminé — issue #8 fermée ; PR #28.

Lire paramètres natifs, ElementId, UniqueId, phase et variante ; conserver chaînes et zéros initiaux.

Acceptation : nom sans concaténation du numéro ; ID entier préservé ; occupation textuelle ; valeurs absentes explicitement représentées.

Dépendances : EGR-07.

## EGR-09 — Extraire quantités et convertir les unités
Liste : À faire | Priorité : P1 | Labels : Revit API, Qualité
État : Terminé — issue #9 fermée ; PR #29. Tolérances D5 sur le jeu de référence (EGR-18).

Surface m², hauteur non bornée m et volume m³ via UnitUtils ; état du calcul de volume (volumeStatus).

Acceptation : nombres JSON indépendants de la langue ; volume non calculé null avec volumeStatus notComputed ; écarts dans les tolérances fixées sur le jeu de référence (D5).

Dépendances : EGR-07, EGR-18.

## EGR-10 — Extraire les finitions et mappings partagés
Liste : À faire | Priorité : P1 | Labels : Revit API
État : Terminé — issue #10 fermée ; PR #33, #37 (schéma 1.2).

Sol, murs, plafond, plinthes depuis les paramètres de pièce ; mappings facultatifs par GUID avec provenance.

Acceptation : champs renseignés/vides/absents distingués selon la section 3 ; résultat identique en Revit français et anglais sur le jeu de référence ; aucune déduction géométrique.

Dépendances : EGR-08, EGR-18.

## EGR-11 — Ajouter les indicateurs qualité et la résilience
Liste : À faire | Priorité : P1 | Labels : Qualité, Logs
État : Terminé — issue #11 fermée ; PR #38 (et #31 pour `ZERO_AREA`).

Calculer les indicateurs et anomalies de la section 9.1 (non-placement, surface nulle, doublons, paramètre absent, niveau introuvable, erreurs par pièce) ; distinguer échecs globaux (9.2) et avertissements ; statut final selon 9.3.

Acceptation : codes et sévérités conformes à la section 9 ; aucune pièce perdue silencieusement ; une erreur locale laisse les autres pièces exploitables ; statut final et résultat APS cohérents.

Dépendances : EGR-04, EGR-08, EGR-09, EGR-10, EGR-18.

## EGR-12 — Exporter le résultat JSON unique
Liste : À faire | Priorité : P1 | Labels : Export
État : Terminé — issue #12 fermée ; PR #30.

Produire `result.json` (summary, rooms, anomalies) conforme à `docs/schema/result.schema.json` : schéma versionné, ordre déterministe, UTF-8, unités explicites ; finalisation atomique locale ; `result.json` en échec si possible. CSV, summary.json et execution.jsonl retirés du MVP.

Acceptation : `result.json` valide contre le schéma en succès, avertissement et échec ; métadonnées du traitement présentes dans summary ; aucune sortie partielle déclarée valide ; schéma et exemple de documentation tenus à jour.

Dépendances : EGR-11.

## EGR-13 — Préparer les trois AppBundles et Activities
Liste : À faire | Priorité : P1 | Labels : APS, Packaging
État : Code livré, issue #13 ouverte ; PR #34. Reste : trois WorkItems réels et test D6. Bloquant : Revit/APS.

Assembler manifestes, dépendances applicatives, commandes, paramètres d'entrée et paramètre de sortie `result.json`, versions et alias de test. Trancher la décision D6 (sortie en échec).

Acceptation : ZIP vérifiés ; trois WorkItems réels extraient les données et livrent `result.json` ; comportement en échec documenté (D6) ; références Revit non distribuées inutilement.

Dépendances : EGR-02, EGR-12.

## EGR-14 — Valider la recette sur Revit 2025, 2026 et 2027
Liste : À faire | Priorité : P1 | Labels : Qualité, APS
État : En attente — issue #14 ouverte ; aucune PR. Bloquant : Revit/APS et EGR-18.

Exécuter la recette sur les maquettes de référence (EGR-18) et couvrir les cas de la section 8 ; comparer `result.json` aux résultats attendus et aux propriétés Revit. Trancher la décision D9 (objectifs de performance).

Acceptation : matrice trois versions signée, cas d'échec vérifiés, source inchangée, aucun dialogue, résultats/logs récupérables ; `result.json` valide contre le schéma sur tous les cas ; performances mesurées (D9).

Dépendances : EGR-13, EGR-17, EGR-18.

## EGR-15 — Documenter et automatiser la publication
Liste : À faire | Priorité : P1 | Labels : Packaging, APS
État : Documentation et CI livrées, issue #15 ouverte ; PR #39. Reste : promotion `prod` après recette (EGR-14).

Guide de build, configuration, publication, lancement de test, lecture des logs et retour de version ; pipeline et gestion des secrets.

Acceptation : un développeur peut reproduire le build et le test ; alias de production promu après recette ; rollback documenté.

Dépendances : EGR-14.

## EGR-16 — Ajouter le recalcul facultatif des volumes
Liste : À faire | Priorité : P2 | Labels : Revit API, Qualité
État : Terminé — issue #16 fermée ; PR #40 (schéma 1.3). Volumes à vérifier sur trois moteurs en EGR-14.

Implémenter volumeMode=compute avec transaction, régénération et absence de sauvegarde du document source.

Acceptation : réglage et calcul traçables ; volumes vérifiés sur trois moteurs ; mode preserve conserve son comportement.

Dépendances : EGR-09, EGR-14.

## EGR-17 — Tester unitairement le cœur indépendant de Revit
Liste : À faire | Priorité : P1 | Labels : Qualité, Architecture
État : Code livré, issue #17 ouverte ; PR #25, #39 (tests lancés par la CI). Clôture à décider par le propriétaire.

Projet de tests exécutable hors Revit sur le cœur : validation de settings.json, conversions d'unités, logique des indicateurs qualité et statuts (section 9), sérialisation de `result.json` et validation contre le schéma. Intégré au pipeline.

Acceptation : tests exécutables sans Revit ni APS ; cas nominaux et limites couverts : null/zéro/vide, doublons, non placée, configuration invalide ; `result.json` produit par les tests valide contre le schéma ; tests lancés par le pipeline.

Dépendances : EGR-03.

## EGR-18 — Constituer les maquettes de référence
Liste : À faire | Priorité : P0 | Labels : Qualité, Revit API
État : En attente — issue #18 ouverte ; aucune PR. Bloquant : Revit et EGR-01.

Constituer tôt, pour Revit 2025, 2026 et 2027, les maquettes couvrant les cas de la section 8, avec les ZIP d'entrée, settings.json et `result.json` attendus. Trancher la décision D5 (tolérances).

Acceptation : un jeu par version couvrant chaque cas de la section 8, y compris ZIP ambigu et RVT incompatible ; résultats attendus rédigés et valides contre le schéma ; tolérances confirmées (D5) ; jeu versionné et accessible à l'équipe.

Dépendances : EGR-01.

## EGR-19 — Outil WPF de gestion de l'application APS (bundles, activities, alias)
Liste : À faire | Priorité : P1 | Labels : APS, Packaging | Issue : #35
État : En cours — issue #35 ouverte ; PR #41 ouverte (bibliothèque `tools/EgRoomTracker.Aps`), non fusionnée. Dépend d'EGR-13.

Outil développeur C# (.NET, WPF, MVVM) pour gérer l'application EG_ROOM_TRACKER sur l'Automation API, en remplacement de l'usage manuel du script de publication : connexion OAuth 2-legged (`code:all`) par `APS_CLIENT_ID` / `APS_CLIENT_SECRET` lus dans l'environnement (`APS_TOKEN` en secours, `APS_NICKNAME`) ; vérification des moteurs ; liste des AppBundles, Activities, versions et alias 2025/2026/2027 ; publication d'un bundle produit par `build-appbundle.sh` ; création/mise à jour des Activities depuis `src/Packaging/definitions` ; alias test/prod, promotion et retour arrière ; journal des opérations. Architecture : bibliothèque `tools/EgRoomTracker.Aps` (client APS testable, sans WPF, réutilisée par EGR-20) et application `tools/EgRoomTracker.DaManager`.

Acceptation : bibliothèque APS couverte par des tests unitaires (HTTP simulé) ; application qui compile (net8.0-windows), lancement documenté ; chaque écriture affiche le plan et demande confirmation ; aucun secret affiché, journalisé ni écrit sur disque ; section « Vérification doc » (aps-docs).

Dépendances : EGR-13.

## EGR-20 — Outil WPF de lancement des WorkItems et lecture des résultats
Liste : À faire | Priorité : P1 | Labels : APS, Qualité | Issue : #36
État : À faire — issue #36 ouverte ; aucune PR. Dépend d'EGR-19 et d'EGR-13.

Outil développeur C# (.NET, WPF, MVVM) `tools/EgRoomTracker.WorkItemRunner`, réutilisant `tools/EgRoomTracker.Aps` : choix de la version Revit et de l'alias (test/prod) ; sélection d'un ZIP de maquette et d'un `settings.json` ; upload OSS et URL signées, soumission du WorkItem, suivi du statut ; téléchargement et affichage de `result.json` (bilan, pièces, anomalies, validation contre le schéma) et du rapport APS ; historique des lancements, export des résultats.

Acceptation : logique testable hors WPF, tests unitaires avec HTTP simulé ; compile (net8.0-windows), lancement documenté ; aucun secret ni URL signée affiché ou journalisé ; sert de support à la recette EGR-14 (dont le test D6).

Dépendances : EGR-19, EGR-13.
