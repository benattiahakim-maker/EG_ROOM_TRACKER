# Packaging

Emplacement réservé aux artefacts de packaging Design Automation : manifeste `PackageContents.xml`,
manifeste `.addin` (classe `EgRoomTracker.EntryPoint.AutomationApplication`, assembly `EgRoomTracker.dll`),
scripts d'assemblage des trois AppBundles (2025, 2026, 2027) et définitions d'Activities versionnées.

Contenu à produire en EGR-13 (#13) ; publication et pipeline en EGR-15 (#15).

Règles (spécifications, section 7) :

- un bundle par version, construit avec `-p:RevitVersion=<année>` ;
- assemblies Revit non embarquées (références de compilation uniquement) ;
- inclure uniquement les dépendances applicatives nécessaires (les assemblies du cœur `EgRoomTracker.*.dll`) ;
  `DesignAutomationBridge.dll` est copié dans `bin/<RevitVersion>/<Configuration>/` (`CopyLocalLockFileAssemblies`) ;
  son inclusion dans le bundle est à confirmer en EGR-13 ;
- aucun secret dans les bundles.
