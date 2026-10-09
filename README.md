# EG_ROOM_TRACKER

Composant C# pour Autodesk Platform Services — Design Automation for Revit 2025, 2026 et 2027.

Extraction des pièces d’une maquette RVT reçue dans un ZIP : identité, nom, numéro, surface, hauteur non bornée, niveau, occupation, volume et finitions, paramètres partagés par GUID. Le résultat est retourné dans un fichier JSON unique, `result.json` (bilan, pièces, anomalies), contrat versionné **1.3**.

## État (9 octobre 2026)

**Implémenté et testé hors Revit** (cartes de code terminées, voir [CHANGELOG](CHANGELOG.md) et [backlog](docs/BACKLOG.md)) :

- solution organisée par services, logs JSON Lines sur la console (EGR-03, EGR-04) ;
- validation de `settings.json` et du ZIP, résolution confinée du RVT principal (EGR-05) ;
- ouverture du document en Automation : ouverture explicite, détachement des maquettes collaboratives, politique stricte des erreurs, liens manquants ou déchargés signalés, fermeture sans sauvegarde (EGR-06) ;
- collecte de toutes les pièces avec filtre de phase, lecture de l’identité, des quantités en unités SI, des finitions et des paramètres partagés (EGR-07 à EGR-10) ;
- indicateurs qualité, anomalies et statut final (EGR-11) ; export atomique et déterministe de `result.json` avec repli en échec (EGR-12) ;
- `volumeMode: compute` : calcul temporaire des volumes dans une transaction annulée (EGR-16) ;
- packaging des trois AppBundles et définitions d’Activities, script de publication en dry-run par défaut (EGR-13) ;
- documentation de publication et retour arrière, CI GitHub sur chaque PR : build, tests .NET et Python, validation des exemples, construction contrôlée des AppBundles, sans secret ni déploiement (EGR-15, EGR-17).

**Reste à valider sur APS / WorkItems réels** :

| Carte | Objet | Bloquant |
| --- | --- | --- |
| [EGR-02](https://github.com/benattiahakim-maker/EG_ROOM_TRACKER/issues/2) | Moteurs et runtimes effectifs 2025–2027, WorkItem minimal sans dialogue par moteur (dont la cible `net8.0-windows` sur le moteur 2026.5 / .NET 10) | Accès APS |
| [EGR-14](https://github.com/benattiahakim-maker/EG_ROOM_TRACKER/issues/14) | Recette sur les trois moteurs, D6 (sortie en échec), D9 (performances), validation du mode `compute` | Revit / APS, EGR-18 |
| [EGR-18](https://github.com/benattiahakim-maker/EG_ROOM_TRACKER/issues/18) | Maquettes de référence et résultats attendus, D5 (tolérances) | Revit, EGR-01 |
| [EGR-01](https://github.com/benattiahakim-maker/EG_ROOM_TRACKER/issues/1) | Approbation du contrat et des choix métier, D3 et D7 | Propriétaire |

Outils développeur à venir (P1) : [EGR-19](https://github.com/benattiahakim-maker/EG_ROOM_TRACKER/issues/35) (gestion de l’application APS, WPF) et [EGR-20](https://github.com/benattiahakim-maker/EG_ROOM_TRACKER/issues/36) (lancement des WorkItems et lecture des résultats, WPF).

## Build et tests

Prérequis : SDK .NET 10 (fixé par `global.json`), runtime .NET 8 pour les tests ; ni Revit ni identifiant APS. Les cibles Windows se compilent aussi sous Linux/macOS (assemblies de référence).

```bash
dotnet build EgRoomTracker.sln            # cœur + Revit 2027 (RevitVersion par défaut)
dotnet test EgRoomTracker.sln             # tests du cœur, sans Revit ni APS

# Point d'entrée par version Revit : 2025 → net8.0-windows, 2026 → net8.0-windows, 2027 → net10.0-windows
for v in 2025 2026 2027; do
  dotnet build src/EntryPoint/EgRoomTracker.EntryPoint.csproj -c Release -p:RevitVersion=$v
done

src/Packaging/scripts/build-appbundle.sh  # AppBundles contrôlés dans artifacts/appbundles/
python3 -m unittest discover -s src/Packaging/scripts/tests
pip install jsonschema && python docs/schema/validate_examples.py
```

Détails : [Build et tests](docs/BUILD.md) ; publication : [PUBLISHING.md](docs/PUBLISHING.md).

## Documentation

- [Spécifications techniques](docs/SPECIFICATIONS.md) (v0.3) et [CHANGELOG](CHANGELOG.md)
- [Build et tests](docs/BUILD.md)
- [Publication, recette, retour arrière et CI](docs/PUBLISHING.md) ; [packaging](src/Packaging/README.md)
- [Entrées ZIP et settings.json](docs/INPUTS.md)
- [Accès au document Revit](docs/DOCUMENT.md)
- [Collecte des pièces](docs/COLLECTE.md) ; [lecture des champs et volumes](docs/LECTURE.md) ; [qualité](docs/QUALITE.md)
- [Logs d’exécution](docs/LOGS.md)
- [Schéma JSON du résultat](docs/schema/result.schema.json)
- [Exemple de configuration](docs/examples/settings.example.json) et [exemple de résultat](docs/examples/result.example.json), données illustratives
- [Backlog et issues GitHub](docs/BACKLOG.md) — [détail des cartes](docs/TRELLO_BACKLOG.md) — [tableau Trello](https://trello.com/b/dsFoEv4N/egroomtracker)

## Principes

Exécution sans interface utilisateur, architecture par services, contrat de sortie JSON unique et versionné, logs structurés sur la console, RVT source jamais sauvegardé, responsabilités séparées.
