# EG_ROOM_TRACKER — Backlog GitHub

Les 20 cartes sont suivies dans les issues GitHub et sur le [tableau Trello](https://trello.com/b/dsFoEv4N/egroomtracker). Détail des cartes : [TRELLO_BACKLOG.md](TRELLO_BACKLOG.md).

État vérifié avec `gh` le 9 octobre 2026 (`main` = 9205e2c). « Issue » = état GitHub de l'issue ; « PR » = PR fusionnées dans `main`.

| Carte | Issue | Priorité | Issue GitHub | État | PR fusionnées | Reste / bloquant |
| --- | --- | --- | --- | --- | --- | --- |
| EGR-01 | [#1 — Valider le contrat d'extraction](https://github.com/benattiahakim-maker/EG_ROOM_TRACKER/issues/1) | P0 | ouverte | En attente | — (spec v0.2 #19, v0.3) | Approbation du propriétaire : contrat, D3, D7 |
| EGR-02 | [#2 — Valider les moteurs et SDK 2025–2027](https://github.com/benattiahakim-maker/EG_ROOM_TRACKER/issues/2) | P0 | ouverte | En attente | — | Accès APS : moteurs, runtimes, WorkItem minimal par moteur |
| EGR-03 | [#3 — Créer la solution organisée par services](https://github.com/benattiahakim-maker/EG_ROOM_TRACKER/issues/3) | P0 | fermée | Terminé | #20, #22 | — |
| EGR-04 | [#4 — Implémenter le service de logs](https://github.com/benattiahakim-maker/EG_ROOM_TRACKER/issues/4) | P0 | fermée | Terminé | #21, #22 | Visibilité dans le rapport APS à constater en EGR-14 |
| EGR-05 | [#5 — Définir et valider les entrées ZIP/configuration](https://github.com/benattiahakim-maker/EG_ROOM_TRACKER/issues/5) | P0 | fermée | Terminé | #27, #32 | — |
| EGR-06 | [#6 — Gérer le document Revit en environnement Automation](https://github.com/benattiahakim-maker/EG_ROOM_TRACKER/issues/6) | P0 | fermée | Terminé | #23, #26 | Validation dans un WorkItem réel (EGR-14) |
| EGR-07 | [#7 — Collecter toutes les pièces](https://github.com/benattiahakim-maker/EG_ROOM_TRACKER/issues/7) | P1 | fermée | Terminé | #24, #31 | Comptage sur le jeu de référence (EGR-18) |
| EGR-08 | [#8 — Lire identité, nom, numéro, niveau et occupation](https://github.com/benattiahakim-maker/EG_ROOM_TRACKER/issues/8) | P1 | fermée | Terminé | #28 | — |
| EGR-09 | [#9 — Extraire quantités et convertir les unités](https://github.com/benattiahakim-maker/EG_ROOM_TRACKER/issues/9) | P1 | fermée | Terminé | #29 | Tolérances D5 sur le jeu de référence (EGR-18) |
| EGR-10 | [#10 — Extraire les finitions et mappings partagés](https://github.com/benattiahakim-maker/EG_ROOM_TRACKER/issues/10) | P1 | fermée | Terminé | #33, #37 | Revit FR/EN sur le jeu de référence (EGR-18) |
| EGR-11 | [#11 — Ajouter les indicateurs qualité et la résilience](https://github.com/benattiahakim-maker/EG_ROOM_TRACKER/issues/11) | P1 | fermée | Terminé | #38 (et #31 pour `ZERO_AREA`) | — |
| EGR-12 | [#12 — Exporter le résultat JSON unique](https://github.com/benattiahakim-maker/EG_ROOM_TRACKER/issues/12) | P1 | fermée | Terminé | #30 | — |
| EGR-13 | [#13 — Préparer les trois AppBundles et Activities](https://github.com/benattiahakim-maker/EG_ROOM_TRACKER/issues/13) | P1 | ouverte | Code livré, validation APS en attente | #34 | Trois WorkItems réels, D6 (sortie en échec) : Revit/APS |
| EGR-14 | [#14 — Valider la recette sur Revit 2025, 2026 et 2027](https://github.com/benattiahakim-maker/EG_ROOM_TRACKER/issues/14) | P1 | ouverte | En attente | — | Revit/APS, EGR-18 ; D6, D9, mode `compute` sur trois moteurs |
| EGR-15 | [#15 — Documenter et automatiser la publication](https://github.com/benattiahakim-maker/EG_ROOM_TRACKER/issues/15) | P1 | ouverte | Documentation et CI livrées | #39 | Promotion de l'alias `prod` après recette (EGR-14) |
| EGR-16 | [#16 — Ajouter le recalcul facultatif des volumes](https://github.com/benattiahakim-maker/EG_ROOM_TRACKER/issues/16) | P2 | fermée | Terminé | #40 (refus provisoire #31, retiré par #40) | Volumes vérifiés sur trois moteurs (EGR-14) |
| EGR-17 | [#17 — Tester unitairement le cœur indépendant de Revit](https://github.com/benattiahakim-maker/EG_ROOM_TRACKER/issues/17) | P1 | ouverte | Code livré, tests lancés par la CI | #25, #39 | Clôture de l'issue à décider par le propriétaire |
| EGR-18 | [#18 — Constituer les maquettes de référence](https://github.com/benattiahakim-maker/EG_ROOM_TRACKER/issues/18) | P0 | ouverte | En attente | — | Revit, EGR-01 ; D5 |
| EGR-19 | [#35 — Outil WPF de gestion de l'application APS (bundles, activities, alias)](https://github.com/benattiahakim-maker/EG_ROOM_TRACKER/issues/35) | P1 | ouverte | À faire | — | Dépend d'EGR-13 |
| EGR-20 | [#36 — Outil WPF de lancement des WorkItems et lecture des résultats](https://github.com/benattiahakim-maker/EG_ROOM_TRACKER/issues/36) | P1 | ouverte | À faire | — | Dépend d'EGR-19 et d'EGR-13 |

Autres PR fusionnées : #19 (spécifications v0.2).
