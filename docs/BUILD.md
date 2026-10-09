# Build et tests

## Prérequis

- SDK .NET 10 (fixé par `global.json`, `rollForward: latestFeature`) ; il compile les cibles .NET 8 et .NET 10.
- Runtime .NET 8 pour exécuter les tests du cœur (`net8.0`).
- Aucun Revit ni identifiant APS nécessaire pour compiler et tester.

## Organisation (spécifications, section 4)

| Projet | Dossier | Cible | Revit |
| --- | --- | --- | --- |
| `EgRoomTracker.Contracts` | `src/Contracts` | `net8.0` | non — DTO, configuration typée, codes |
| `EgRoomTracker.Logging` | `src/Logging` | `net8.0` | non — `ILogService` |
| `EgRoomTracker.Export` | `src/Export` | `net8.0` | non — `IExportService` |
| `EgRoomTracker.Application` | `src/Application` | `net8.0` | non — `RoomExtractionService`, interfaces des services |
| `EgRoomTracker.Revit` | `src/Revit` | par version | oui — implémentations Revit API |
| `EgRoomTracker.EntryPoint` | `src/EntryPoint` | par version | oui — `AutomationApplication`, assembly `EgRoomTracker.dll` |
| `EgRoomTracker.Tests` | `src/Tests` | `net8.0` | non — tests unitaires hors Revit/APS |
| — | `src/Packaging` | — | manifestes et bundles (EGR-13) |

Dépendances : `Contracts` ← `Export` ← `Application` (← `Logging`) ← `Revit` ← `EntryPoint`. Le cœur ne référence jamais
Revit ; un test (`RevitIsolationTests`) le vérifie.

## Versions Revit

La propriété MSBuild `RevitVersion` (`2025`, `2026`, `2027` ; défaut `2027`) sélectionne, dans `build/Revit.props` et
`Directory.Packages.props`, le framework cible, les références Revit API et le pont Design Automation :

| RevitVersion | Framework | Revit API (référence) | Design Automation |
| --- | --- | --- | --- |
| 2025 | `net8.0-windows` | `Nice3point.Revit.Api.RevitAPI` 2025.4.60 | `Autodesk.Forge.DesignAutomation.Revit` 2025.0.1 |
| 2026 | `net8.0-windows` | `Nice3point.Revit.Api.RevitAPI` 2026.4.10 | `Autodesk.Forge.DesignAutomation.Revit` 2026.0.0 |
| 2027 | `net10.0-windows` | `Nice3point.Revit.Api.RevitAPI` 2027.3.0 | `Autodesk.Forge.DesignAutomation.Revit` 2027.0.0 |

Le moteur APS 2026 est passé à Revit 2026.5 / .NET 10 le 21 septembre 2026 ; la cible `net8.0-windows` y reste
chargeable, à confirmer en EGR-02/EGR-14. Les packages `Nice3point.Revit.Api.*` sont des assemblies de référence
publiées par la communauté ; pour compiler contre une installation Revit locale :
`-p:RevitApiDir="C:\Program Files\Autodesk\Revit 2026"`.

Sorties séparées : `src/<Projet>/bin/<RevitVersion>/<Configuration>/`, `obj/<RevitVersion>/`.

## Commandes

```bash
dotnet build EgRoomTracker.sln                      # cœur + Revit 2027
dotnet test EgRoomTracker.sln                       # tests du cœur
for v in 2025 2026 2027; do
  dotnet build src/EntryPoint/EgRoomTracker.EntryPoint.csproj -c Release -p:RevitVersion=$v
done
```

Les cibles Windows se compilent aussi sous Linux/macOS (`EnableWindowsTargeting`), car seules des assemblies de
référence sont utilisées. L'exécution réelle requiert Revit / Design Automation.
