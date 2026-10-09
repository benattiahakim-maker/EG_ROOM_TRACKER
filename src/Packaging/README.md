# Packaging

Emplacement réservé aux artefacts de packaging Design Automation : manifeste `PackageContents.xml`,
manifeste `.addin` (classe `EgRoomTracker.EntryPoint.AutomationApplication`, assembly `EgRoomTracker.dll`),
scripts d'assemblage des trois AppBundles (2025, 2026, 2027) et définitions d'Activities versionnées.

Contenu à produire en EGR-13 (#13) ; publication et pipeline en EGR-15 (#15).

Règles (spécifications, section 7) :

- un bundle par version, construit avec `-p:RevitVersion=<année>` ;
- assemblies Revit non embarquées (références de compilation uniquement) ;
- inclure uniquement les dépendances applicatives nécessaires (les assemblies du cœur `EgRoomTracker.*.dll`) ;
  présence de `DesignAutomationBridge.dll` dans le bundle à vérifier : une bibliothèque de classes ne copie pas
  les assemblies NuGet dans `bin/` par défaut ;
- aucun secret dans les bundles.
