# Tableau Trello proposé : EG_ROOM_TRACKER — Design Automation for Revit

État : suivi transféré aux issues GitHub. Voir [le backlog GitHub](BACKLOG.md). Ce document conserve la préparation initiale.

Listes : Cadrage → À faire → En cours → À vérifier → Terminé. Liste complémentaire : Bloqué.

Labels : Architecture, Revit API, APS, Logs, Export, Qualité, Packaging. Priorités P0 (fondation), P1 (MVP), P2 (option). Les cartes suivantes sont prêtes à créer, sans assignation ni échéance inventée.

## EGR-01 — Valider le contrat d'extraction
Liste : Cadrage | Priorité : P0 | Labels : Architecture, Revit API

Fixer les choix métier décrits dans les spécifications : document principal, phases, hauteur non bornée, occupation textuelle, finitions textuelles et mode de volume.

Acceptation : choix documentés ; champs JSON et unités approuvés ; extensions clairement identifiées.

## EGR-02 — Valider les moteurs et SDK 2025–2027
Liste : À faire | Priorité : P0 | Labels : APS, Packaging

Interroger les moteurs APS, consigner leurs IDs et runtimes effectifs ; construire un bundle minimal pour chaque version.

Acceptation : un WorkItem sans dialogue réussit sur chaque moteur ; versions/API/runtime consignés.

## EGR-03 — Créer la solution organisée par services
Liste : À faire | Priorité : P0 | Labels : Architecture

Créer EntryPoint, Application, Revit, Contracts, Export et Logging. Point d'entrée mince, dépendances explicites, DTO indépendants de Revit.

Acceptation : responsabilités conformes aux spécifications ; compilation pour chaque cible ; aucun accès UI ni appel Revit parallèle.

Dépendances : EGR-02.

## EGR-04 — Implémenter le service de logs
Liste : À faire | Priorité : P0 | Labels : Logs

Console et fichier JSONL ; niveaux, codes, corrélation, contexte pièce et exceptions ; flush de terminaison.

Acceptation : succès, avertissement et exception visibles ; aucun secret ; rapport APS exploitable même en échec.

Dépendances : EGR-03.

## EGR-05 — Définir et valider les entrées ZIP/configuration
Liste : À faire | Priorité : P0 | Labels : APS, Architecture

Configurer décompression, chemins locaux, RVT principal et settings.json. Refuser chemins sortants, configuration invalide et RVT ambigu.

Acceptation : ZIP à RVT unique accepté ; plusieurs RVT nécessitent un chemin ; erreurs explicites.

Dépendances : EGR-03, EGR-04.

## EGR-06 — Gérer le document Revit en environnement Automation
Liste : À faire | Priorité : P0 | Labels : APS, Revit API

Valider ouverture par Activity/contexte SDK, propriété de fermeture, maquettes collaboratives et liens. Aucun dialogue.

Acceptation : document accessible dans un WorkItem réel ; liens manquants signalés ; source non sauvegardée ; fichier incompatible en échec.

Dépendances : EGR-02, EGR-05.

## EGR-07 — Collecter toutes les pièces
Liste : À faire | Priorité : P1 | Labels : Revit API

Collecter les Room du document principal, toutes phases par défaut, variantes et pièces non placées incluses.

Acceptation : nombre conforme au jeu de référence ; aucun espace MEP, surface ou tag ; aucun filtre de vue active.

Dépendances : EGR-06.

## EGR-08 — Lire identité, nom, numéro, niveau et occupation
Liste : À faire | Priorité : P1 | Labels : Revit API

Lire paramètres natifs, ElementId, UniqueId, phase et variante ; conserver chaînes et zéros initiaux.

Acceptation : nom sans concaténation du numéro ; ID entier préservé ; occupation textuelle ; valeurs absentes explicitement représentées.

Dépendances : EGR-07.

## EGR-09 — Extraire quantités et convertir les unités
Liste : À faire | Priorité : P1 | Labels : Revit API, Qualité

Surface m², hauteur non bornée m et volume m³ via UnitUtils ; état du calcul de volume.

Acceptation : nombres JSON indépendants de la langue ; volume non calculé null ; écarts dans les tolérances convenues.

Dépendances : EGR-07.

## EGR-10 — Extraire les finitions et mappings partagés
Liste : À faire | Priorité : P1 | Labels : Revit API

Sol, murs, plafond, plinthes depuis les paramètres de pièce ; mappings facultatifs par GUID avec provenance.

Acceptation : champs renseignés/vides/absents distingués ; résultat identique en Revit français et anglais ; aucune déduction géométrique.

Dépendances : EGR-08.

## EGR-11 — Ajouter les indicateurs qualité et la résilience
Liste : À faire | Priorité : P1 | Labels : Qualité, Logs

Signaler non-placement, surface nulle, doublons de numéro et erreurs par pièce ; distinguer échecs globaux et avertissements.

Acceptation : aucune pièce perdue silencieusement ; une erreur locale laisse les autres pièces exploitables ; statut APS cohérent.

Dépendances : EGR-04, EGR-08, EGR-09, EGR-10.

## EGR-12 — Exporter JSON, CSV et bilan
Liste : À faire | Priorité : P1 | Labels : Export

Schéma versionné, ordre déterministe, UTF-8, unités explicites, protection CSV contre les formules ; finalisation atomique locale.

Acceptation : sorties lisibles et complètes, métadonnées du traitement présentes ; aucune sortie partielle déclarée valide.

Dépendances : EGR-11.

## EGR-13 — Préparer les trois AppBundles et Activities
Liste : À faire | Priorité : P1 | Labels : APS, Packaging

Assembler manifestes, dépendances applicatives, commandes, paramètres entrée/sortie, versions et alias de test.

Acceptation : ZIP vérifiés ; trois WorkItems réels extraient les données et livrent les résultats ; références Revit non distribuées inutilement.

Dépendances : EGR-02, EGR-12.

## EGR-14 — Valider la recette sur Revit 2025, 2026 et 2027
Liste : À faire | Priorité : P1 | Labels : Qualité, APS

Constituer les maquettes de référence et couvrir les cas définis dans les spécifications ; comparer exports et propriétés Revit.

Acceptation : matrice trois versions signée, cas d'échec vérifiés, source inchangée, aucun dialogue, résultats/logs récupérables ; performances mesurées.

Dépendances : EGR-13.

## EGR-15 — Documenter et automatiser la publication
Liste : À faire | Priorité : P1 | Labels : Packaging, APS

Guide de build, configuration, publication, lancement de test, lecture des logs et retour de version ; pipeline et gestion des secrets.

Acceptation : un développeur peut reproduire le build et le test ; alias de production promu après recette ; rollback documenté.

Dépendances : EGR-14.

## EGR-16 — Ajouter le recalcul facultatif des volumes
Liste : À faire | Priorité : P2 | Labels : Revit API, Qualité

Implémenter volumeMode=compute avec transaction, régénération et absence de sauvegarde du document source.

Acceptation : réglage et calcul traçables ; volumes vérifiés sur trois moteurs ; mode preserve conserve son comportement.

Dépendances : EGR-09, EGR-14.

