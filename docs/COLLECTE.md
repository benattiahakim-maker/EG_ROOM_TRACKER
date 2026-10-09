# Collecte des pièces (EGR-07)

Implémentation : `EgRoomTracker.Revit.RoomCollectionService` ; filtre de phases indépendant de Revit :
`EgRoomTracker.Application.Revit.PhaseSelection`.

## Règles (spécifications, sections 2 et 3)

- **Périmètre** : document principal uniquement ; les pièces des documents liés ne sont pas parcourues.
- **Sélection** : `FilteredElementCollector(document)` sur le document entier, **sans vue** (aucun filtre de vue
  active), catégorie `OST_Rooms`, instances uniquement, puis contrôle de type `Room`. Espaces MEP (`OST_MEPSpaces`),
  surfaces (`OST_Areas`) et étiquettes (`OST_RoomTags`) sont exclus.
- **Inclus** : pièces non placées (`Location` nulle), de surface nulle, dans un groupe et dans toutes les variantes
  (`DesignOption`). Aucune pièce n'est supprimée ni fusionnée ; les indicateurs qualité relèvent d'EGR-11.
- **Phases** : `phaseIds` null ou absent = toutes les phases. Sinon, seules les pièces dont `ROOM_PHASE_ID`
  appartient à la liste sont conservées.
  - Tableau vide : `CONFIG_INVALID` (étape `configuration`).
  - Phase absente du document : échec global `CONFIG_INVALID` (étape `collection`), identifiants inconnus listés
    dans le message ; jamais un résultat vide présenté comme réussi.
  - `PhaseSelection.Parse` n'est qu'un contrôle défensif : la validation de `settings.json`, y compris le format
    entier des `phaseIds`, relève d'EGR-05.
- **Identité** : `CollectedRoom(ElementId.Value, UniqueId)`, triée par `elementId` croissant (l'orchestration
  retrie aussi les pièces par `elementId` numérique après le contrôle qualité) ; elle permet d'exporter
  une pièce en `EXTRACTION_ERROR` même si la lecture de ses paramètres échoue.
- Exécution séquentielle sur le thread de l'événement Design Automation.

## Journal

`ROOM_COLLECTION_DETAILS` : nombre de pièces, non placées, en variante, exclues par phase, filtre appliqué.

## À valider sur le jeu de référence (EGR-18) et dans un WorkItem réel (EGR-14)

- Nombre de pièces conforme au jeu de référence, sur les trois moteurs.
- Présence des pièces des variantes secondaires dans un collecteur de document (comportement attendu de l'API).
