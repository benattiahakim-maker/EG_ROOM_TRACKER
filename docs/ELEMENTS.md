# Paramètres complets, portes, équipements et terminaux (schéma 1.4)

Implémentation : `src/Revit` (`DoorService`, `EquipmentService`, `TerminalService`, `ParameterDumper`), orchestration dans
`src/Application/RoomExtractionService.cs`. Tests : `src/Tests/Application/RoomElementsTests.cs`, exécutés sans Revit.
**Non encore validé sur un vrai Revit** : à confirmer en recette (EGR-14).

## Options de `settings.json`

| Clé | Valeurs | Effet |
| --- | --- | --- |
| `roomParameters` | `"none"` (défaut), `"all"` | `all` : chaque pièce reçoit `parameters`, et chaque élément rattaché aussi |
| `elements` | tableau parmi `"doors"`, `"equipment"`, `"terminals"` (défaut : aucun) | chaque famille demandée donne un tableau, éventuellement vide, sur **toutes** les pièces exportées |

Sans ces options, `result.json` est identique à celui du schéma 1.3, hors `schemaVersion`.

## `rooms[].parametres` (`roomParameters: "all"`)

Tous les paramètres de la pièce (natifs, partagés, de projet) sortent **à plat**, `{ nom : valeur }`, comme `surface_m2` ou `volume_m3` : un
paramètre, une propriété, donc **une colonne** dans le tableau de résultats de WorkItemRunner.

```json
"parameters": {
  "Commentaires": "Salle serveurs",
  "Hauteur limite": 2.44,
  "Occupation": "Bureau",
  "SGP-DI-Ouvrage": "OA1"
}
```

| Stockage Revit | Valeur exportée |
| --- | --- |
| texte | le texte |
| entier | le nombre entier |
| ElementId | l'identifiant en chaîne |
| double : longueur, surface, volume, nombre | converti en SI (m, m², m³), sans arrondi |
| double : autre grandeur (angle, pourcentage…) | la valeur affichée par Revit (texte) |
| sans valeur | `null` |

Les clés sont triées par nom (sortie déterministe) ; un nom en double est suffixé « (2) », « (3) ». Un paramètre illisible est omis. Les noms
sont ceux du moteur : en français avec `/l FRA`. Une pièce dont la lecture complète échoue garde tous les champs du contrat (événement
`ROOM_PARAMETERS_FAILED`). Pour retrouver le GUID d'un paramètre partagé (réglage `sharedParameters`), chercher son nom ici puis le lire dans
Revit : le GUID n'est pas exporté.

## Éléments rattachés aux pièces (`elements`)

| Famille | Catégories Revit par défaut | Rattachement |
| --- | --- | --- |
| `doors` | `OST_Doors` | `FromRoom` (relation `from`) et `ToRoom` (relation `to`) pour la phase de création de la porte |
| `equipment` | `OST_MechanicalEquipment`, `OST_ElectricalEquipment`, `OST_PlumbingEquipment`, `OST_SpecialityEquipment`, `OST_FireProtection` | `FamilyInstance.Room`, à défaut `GetRoomAtPoint` ; relation `in` |
| `terminals` | CVC : `OST_DuctTerminal`, `OST_MechanicalControlDevices` ; plomberie : `OST_PlumbingFixtures`, `OST_Sprinklers` ; électricité : `OST_ElectricalFixtures`, `OST_LightingFixtures`, `OST_LightingDevices`, `OST_CommunicationDevices`, `OST_DataDevices`, `OST_TelephoneDevices`, `OST_SecurityDevices`, `OST_FireAlarmDevices`, `OST_NurseCallDevices` | idem ; relation `in` |

Le champ `category` de chaque élément indique sa catégorie Revit.

### Disciplines (`discipline`)

Chaque équipement et chaque terminal porte une `discipline` déduite de sa catégorie (`ElementDisciplines`), pour ranger les listes
par section : `cvc`, `plomberie`, `electricite`, `autres`.

| Discipline | Catégories par défaut |
| --- | --- |
| `cvc` | `OST_MechanicalEquipment`, `OST_DuctTerminal`, `OST_MechanicalControlDevices` |
| `plomberie` | `OST_PlumbingEquipment`, `OST_PlumbingFixtures`, `OST_Sprinklers` |
| `electricite` | `OST_ElectricalEquipment`, `OST_ElectricalFixtures`, `OST_LightingFixtures`, `OST_LightingDevices`, `OST_CommunicationDevices`, `OST_DataDevices`, `OST_TelephoneDevices`, `OST_SecurityDevices`, `OST_FireAlarmDevices`, `OST_NurseCallDevices` |
| `autres` | `OST_SpecialityEquipment`, `OST_FireProtection`, et toute catégorie ajoutée par `elementCategories` |

Les terminaux suivent la même répartition que les équipements : ils ne forment pas une section à part. Les accessoires (vannes,
registres) ne sont pas collectés par défaut. Les portes n'ont pas de discipline.

### Modèles liés

Les équipements et terminaux sont aussi cherchés dans les **modèles liés chargés** (`linkedElements`, vrai par défaut ; liens imbriqués
jusqu'à 3 niveaux, chaque instance de lien étant une source distincte). Pour chaque élément d'un lien :

- sa boîte, son point d'insertion et ses solides sont ramenés dans le repère du modèle principal par la **transformation totale du
  lien** (`RevitLinkInstance.GetTotalTransform`) avant toute comparaison avec les pièces ;
- `nom_du_lien` donne le titre du modèle lié ; `id_revit` et `unique_id` sont ceux du **modèle lié** (les ElementId sont propres à chaque
  document) ;
- un lien non chargé est ignoré (anomalie `LINK_NOT_LOADED` déjà signalée à l'ouverture, `LINK_MISSING` s'il est absent du ZIP).

### Attribution à une pièce : une seule pièce par élément

La pièce est celle du **modèle principal** qui contient l'élément (`RoomLocator`) :

1. la pièce qui contient le point d'insertion (`GetRoomAtPoint`), puis le centre de la boîte englobante, puis un point bas de cette
   boîte (un élément posé au plafond a son origine au-dessus de la pièce) ;
2. à défaut, la pièce dont le **volume commun** avec le solide de l'élément est le plus grand (intersection avec `Room.ClosedShell`).

Pour un élément du modèle principal, `FamilyInstance.Room` (phase de création) passe d'abord. Un élément sans pièce est compté dans
`ELEMENT_WITHOUT_ROOM`. Différence avec l'application TRC, qui listait un élément dans **toutes** les pièces qu'il recoupe et ne
tenait pas compte de la transformation du lien.

### Choisir les catégories (`elementCategories`)

`settings.json` peut remplacer la liste d'une famille, sans reconstruire le bundle. Les noms sont ceux de `BuiltInCategory`
(préfixe `OST_`). Une famille absente garde sa liste par défaut ; un nom inconnu est ignoré et signalé par
`ELEMENTS_COLLECTION_FAILED` (warning, détail `unknownCategories`).

```json
"elements": ["equipment", "terminals"],
"elementCategories": {
  "equipment": ["OST_MechanicalEquipment", "OST_PipeAccessory", "OST_DuctAccessory"],
  "terminals": ["OST_DuctTerminal", "OST_PlumbingFixtures"]
}
```

Autres catégories courantes, hors défauts : `OST_DuctAccessory` (registres, clapets), `OST_PipeAccessory` (vannes),
`OST_DuctFitting` et `OST_PipeFitting` (raccords, très nombreux), `OST_CableTrayFitting`, `OST_ConduitFitting`,
`OST_GenericModel`, `OST_Furniture`. Seules les instances de famille sont collectées : les gaines, tuyaux, chemins de
câbles et conduits eux-mêmes (courbes MEP) ne le sont pas. Le journal `ELEMENTS_COLLECTED` liste les catégories parcourues.

### Portes : passage libre mesuré sur la géométrie

`largeur_m` et `hauteur_m` d'une porte sont son **passage libre**, mesuré sur la géométrie de l'instance sans lire les paramètres
de la famille (`DoorGeometry`, `OpeningMeasure`) :

- les solides de l'instance (dormant, vantaux, familles imbriquées) sont recoupés par des lignes de coupe tracées dans les
  axes de la porte (`HandOrientation`, `FacingOrientation`), donc sans dépendre de l'axe du mur ni d'un mur droit ;
- **largeur** : trois lignes horizontales à mi-hauteur, à trois profondeurs du mur. Sur chaque ligne, la largeur est la distance
  entre la 2<sup>e</sup> et l'avant-dernière face rencontrées, c'est-à-dire entre les faces intérieures du dormant, quel que soit
  le nombre de vantaux (sans dormant : première et dernière face) ;
- **hauteur** : six lignes verticales ; la hauteur va du bas du vantail à la sous-face du linteau (avant-dernière face) ;
- la valeur retenue est la **médiane** des lignes ; une mesure hors de 0,30 à 6 m (largeur) ou 0,50 à 5 m (hauteur) est écartée ;
- `source_dimensions` indique l'origine : `geometry`, ou `parameter` / `mixed` si, pour une dimension seulement, la géométrie
  est inexploitable et que `DOOR_WIDTH` / `DOOR_HEIGHT` servent de repli ; absent s'il n'y a aucune dimension ;
- la hauteur est mesurée depuis le bas de la porte (seuil), pas depuis le sol fini de la pièce.

Le numéro de la porte (`number`) est `DOOR_NUMBER`.

Une porte entre deux pièces figure dans les deux. Chaque élément porte : `elementId`, `uniqueId`, `category`, `family`,
`type`, `repere`, `niveau` (nom), `relation`, et pour les portes `numero`, `largeur_m`, `hauteur_m` (mètres) et `source_dimensions` ; `parametres` (à plat, comme ci-dessus) avec
`roomParameters: "all"`. Seules les instances du document principal sont parcourues (pas les liens).

## Anomalies

- `ELEMENT_WITHOUT_ROOM` (document, info) : éléments sans pièce exportée (hors phase filtrée, hors pièce) ou illisibles.
  Détails : `kind`, `count`, `elementIds` (50 au plus), `unreadable`.
- `ELEMENTS_COLLECTION_FAILED` (document, warning) : la collecte d'une famille a échoué (ou son service est absent). Les pièces
  sont exportées sans le tableau correspondant ; le statut passe à `succeededWithWarnings`.

## Étendre une famille

Les catégories par défaut sont dans la propriété `Categories` du service concerné (`EquipmentService`, `TerminalService`).
Une nouvelle famille : ajouter une valeur à `ElementKind` et à `ElementKinds`, un service dérivé de
`RoomElementServiceBase` ou `InRoomElementServiceBase`, l'enregistrer dans `CompositionRoot`, puis déclarer la
propriété dans le schéma (`docs/schema/result.schema.json`).
