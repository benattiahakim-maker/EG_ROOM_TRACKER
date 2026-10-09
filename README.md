# EG_ROOM_TRACKER

Composant C# pour Autodesk Platform Services — Design Automation for Revit 2025, 2026 et 2027.

Extraction des pièces d’une maquette RVT reçue dans un ZIP : identité, nom, numéro, surface, hauteur non bornée, niveau, occupation, volume et finitions. Le résultat est retourné dans un fichier JSON unique, `result.json` (bilan, pièces, anomalies).

## Documentation

- [Spécifications techniques](docs/SPECIFICATIONS.md) (v0.2)
- [Schéma JSON du résultat](docs/schema/result.schema.json)
- [Exemple de configuration](docs/examples/settings.example.json) et [exemple de résultat](docs/examples/result.example.json), données illustratives
- [Backlog et issues GitHub](docs/BACKLOG.md) — [détail des cartes](docs/TRELLO_BACKLOG.md) — [tableau Trello](https://trello.com/b/dsFoEv4N/egroomtracker)

Validation de l’exemple contre le schéma : `pip install jsonschema && python docs/schema/validate_examples.py`.

## État

Phase de spécification. Le composant Revit et les AppBundles ne sont pas encore implémentés. Les 18 cartes sont disponibles dans les issues GitHub et sur Trello.

## Principes

Exécution sans interface utilisateur, architecture par services, contrat de sortie JSON unique et versionné, logs structurés sur la console et responsabilités séparées.
