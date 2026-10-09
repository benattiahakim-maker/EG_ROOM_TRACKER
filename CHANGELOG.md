# Changelog

Toutes les évolutions notables d'EG_ROOM_TRACKER sont consignées dans ce fichier.

Le format s'inspire de [Keep a Changelog](https://keepachangelog.com/fr/1.1.0/). Les versions de l'extracteur ne sont pas encore publiées sur APS (version d'assembly `0.1.0`) : les entrées sont regroupées par version du contrat `result.json` et référencent les PR fusionnées dans `main` (liste vérifiée avec `gh pr list --state merged`).

## [Non publié]

### Ajouté

- Préparation de la recette EGR-18 / EGR-14 (#44) : matrice des maquettes de référence R01–R12 (construites en Revit 2025, mises à niveau pour 2026 et 2027 ; R09 non applicable en 2027), `settings.json` et `result.json` attendus (`docs/recette/`), fiche de construction des maquettes, comparateur `tools/recette/compare_result.py` (tolérances D5, champs volatils ignorés, `bind` des identifiants réels) et ses tests ; tests .NET de cohérence des attendus avec le code ; `validate_examples.py` valide aussi les attendus ([`docs/RECETTE.md`](docs/RECETTE.md)).
- Règle `DUPLICATE_NUMBER` par contexte (phase, variante) décidée en EGR-18 et reflétée dans les attendus de recette ; implémentation en attente (EGR Dev), tests de rejeu du contrôle qualité correspondants ignorés.

### Modifié

- Spécifications v0.3 : sections 9 et 10 alignées sur le code et le schéma 1.3 (codes, sévérités, détails, champs de `summary`, événements de log, variantes), statut des décisions D1 à D9 ; README, backlog (EGR-19 #35, EGR-20 #36, état des cartes) et ce CHANGELOG.

## Contrat `result.json` 1.3 — 9 octobre 2026

### Ajouté

- `volumeMode: compute` (EGR-16, #40) : calcul temporaire des volumes dans une transaction sur la copie de travail (`ComputeVolumes = true`, `Document.Regenerate()`), lecture des volumes avant `RollBack()`, RVT jamais sauvegardé ; échec d'activation → `notAvailable` et anomalie `VOLUME_COMPUTE_FAILED` (warning) ; échec de régénération → `DOCUMENT_CORRUPTED`.
- Schéma 1.3 : `summary.volumeComputation` (`documentComputeVolumes`, `computeApplied`, `transactionRolledBack`) et code document `VOLUME_COMPUTE_FAILED` (#40).
- Événements de log `VOLUME_COMPUTATION` et `VOLUME_COMPUTE_FAILED` (#40).
- Documentation de publication, recette, promotion et retour arrière (`docs/PUBLISHING.md`) et workflow CI GitHub : build, tests .NET et Python, validation des exemples, construction contrôlée des trois AppBundles, sans secret ni déploiement (EGR-15, #39 ; activé dans `.github/workflows/ci.yml` par a4accb0).

### Supprimé

- Refus provisoire de `volumeMode: compute` en `CONFIG_INVALID` (introduit par #31, retiré par #40).

## Contrat `result.json` 1.2 — 9 octobre 2026

### Ajouté

- Paramètres partagés numériques : longueur/surface/volume convertis en m/m²/m³, nombre sans unité tel quel, autres types en `null` avec l'anomalie document `UNSUPPORTED_PARAMETER_TYPE` (info, une fois par GUID) ; schéma 1.2 (#37).
- Indicateurs qualité et statut final (EGR-11, #38) : `QualityCheckService` (`ZERO_AREA`, `DUPLICATE_NUMBER` par numéro et phase, pièces sans phase groupées, `VOLUME_NOT_COMPUTED`), ordre des anomalies, statut 9.3 ; tests de spécification réactivés (`docs/QUALITE.md`).
- Packaging des AppBundles et Activities Revit 2025–2027 : manifestes, définitions par version (Activity sans `/i`), script d'assemblage et de contrôle des ZIP, script de publication en dry-run par défaut ; repli `result.json` minimal après `EXPORT_FAILED` ; D6 documentée « à tester » (EGR-13, #34).
- Finitions (`ROOM_FINISH_*`) et paramètres partagés lus par GUID, avec `SHARED_PARAMETER_NOT_FOUND` (EGR-10, #33).
- Export de `result.json` : sérialisation déterministe en mémoire, fichier temporaire puis renommage atomique, UTF-8 sans BOM, fins de ligne `\n` (EGR-12, #30).
- Quantités SI (`areaM2`, `unboundedHeightM`, `volumeM3` par `UnitUtils`, sans arrondi) et `volumeStatus` en mode `preserve` (EGR-09, #29).
- Identité, nom, numéro, niveau (`LEVEL_NOT_FOUND`), occupation, phase et variante des pièces (EGR-08, #28).
- Validation de `settings.json` et du ZIP, résolution confinée du RVT principal (`CONFIG_INVALID`, `CONFIG_SCHEMA_UNSUPPORTED`, `PATH_OUTSIDE_INPUT`, `RVT_NOT_FOUND`, `RVT_AMBIGUOUS`) ; D4 tranchée (EGR-05, #27) ; contraintes du ZIP sur les machines APS : 260 caractères, noms non ASCII (#32).

### Modifié

- Phases typées (`PhaseElementIds`), `ZERO_AREA` = surface nulle sans `UNPLACED`, suppression de `RoomDto.IsPlaced` (#31).

## Contrat `result.json` 1.1 — 9 octobre 2026

### Ajouté

- Liens Revit déchargés signalés par l'anomalie document `LINK_NOT_LOADED` (info) ; schéma 1.1 (EGR-06, #26).
- Tests unitaires du cœur indépendant de Revit : schéma, contrats, orchestration, unités, export (EGR-17, #25).
- Collecte de toutes les pièces (`OST_Rooms`), filtre de phase, compteurs de collecte (`ROOM_COLLECTION_DETAILS`) (EGR-07, #24).
- Accès au document en Automation : ouverture explicite, détachement des maquettes collaboratives, politique stricte des erreurs (`REVIT_WARNING_ON_OPEN`, `DOCUMENT_CORRUPTED`), liens manquants (`LINK_MISSING`), contrôle de version (`REVIT_VERSION_INCOMPATIBLE`), fermeture sans sauvegarde ; D1 et D2 tranchées (EGR-06, #23).

## Contrat `result.json` 1.0 — 9 octobre 2026

### Ajouté

- Suivi de revue EGR-03/04 : filet de sécurité par pièce (`EXTRACTION_ERROR`, `ROOM_EXTRACTION_FAILED`), lignes de log ASCII, masquage des secrets affiné, étape `logging` (#22).
- Service de logs JSON Lines sur la console avec corrélation, niveaux, limitation par code et masquage des secrets ; D8 tranchée : console uniquement (EGR-04, #21).
- Solution C# organisée par services (Contracts, Logging, Export, Application, Revit, EntryPoint, Tests), cibles par `RevitVersion` : 2025 et 2026 en `net8.0-windows`, 2027 en `net10.0-windows` (EGR-03, #20).
- Spécifications v0.2 : sortie unique `result.json`, schéma JSON 1.0 (draft 2020-12), exemples, codes et statuts, décisions ouvertes (#19).

## Versions du contrat `result.json`

| Version | Changement | PR |
| --- | --- | --- |
| 1.0 | Contrat initial : `summary`, `rooms`, `anomalies` | #19 |
| 1.1 | Code document `LINK_NOT_LOADED` | #26 |
| 1.2 | Code document `UNSUPPORTED_PARAMETER_TYPE` | #37 |
| 1.3 | `summary.volumeComputation` et code document `VOLUME_COMPUTE_FAILED` | #40 |
