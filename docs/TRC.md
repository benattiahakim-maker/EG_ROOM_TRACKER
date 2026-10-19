# Sortie au format TRC (`outputFormat`)

Implémentation : `src/Application/Trc/TrcMapper.cs` (conversion, pure), `TrcExportService.cs` (écriture), orchestration dans
`src/Application/RoomExtractionService.cs`. Tests : `src/Tests/Application/TrcExportTests.cs`, sans Revit. Format établi d'après les sorties
réelles de l'application TRC (`EG_Automation_TRC`) et de la plateforme (`TRCValidationDict.json`).

## Réglage

| `outputFormat` | Fichiers du dossier `result/` |
| --- | --- |
| `contract` (défaut) | `result.json` : contrat versionné, bilan, statut et anomalies |
| `trc` | `TRCValidationDict.json` seul : **données seules**, sans statut ni conformité |
| `both` | les deux |

Avec `roomImages`, le dossier `Images/` accompagne dans tous les cas. Une extraction **en échec** écrit toujours `result.json` (sans pièces, il
n'y a pas de données TRC, et le fichier explique l'échec). Le journal de l'extracteur reste dans le rapport APS.

```text
result.zip
├── TRCValidationDict.json
├── Details_Annexes/        valeurs de plus de 255 caractères
└── Images/                 avec "roomImages": true
```

## Fiches

Liste JSON de fiches `{ "model", "pk", "fields" }`, dans l'ordre : tous les `trc.bimobject` (les pièces, puis les portes, équipements et
terminaux), toutes les `trc.bimobjectpropertyvalue`, puis `special.setcodefile`. Pas de spécification, d'exigence, de facette, de résultat de
conformité ni de `facetsetmatch`.

| Modèle | Champs |
| --- | --- |
| `trc.bimobject` | `bim_id` = UniqueId de la pièce ou de l'élément, `code_file` |
| `trc.bimobjectpropertyvalue` | `latest_value` et `original_value` (identiques), `bim_object_id` (`pk` de l'objet), `bim_property_code_property` |
| `special.setcodefile` | `file_name` = `{code_file}.rvt`, `code_file` |

`code_file` : nom du RVT sans extension, `_` remplacés par `-`, `-détaché` retiré (`<GUID>£nom` pour un fichier de la plateforme).
Les **`pk` sont stables** : GUID déterministe de `code_file`, de l'objet, du code et du rang, donc identiques d'un lancement à l'autre
(TRC les tirait au hasard).

## Objets BIM et rattachement

Avec `elements`, chaque **porte**, **équipement** et **terminal** est un `trc.bimobject` à part entière, avec ses propres propriétés ; la pièce
ne porte plus aucune propriété de porte ou d'équipement. Le lien se fait comme `ParentGlobalId` dans les fichiers TRC de la plateforme :

- `Category` sur chaque objet : `Space` (pièce), `Door` (porte), `<discipline> equipement` ou `<discipline> terminal` pour les équipements et
  terminaux (`cvc equipement`, `plomberie terminal`, `electricite equipement`, `autres terminal`…) ;
- `Space` sur chaque élément : le `bim_id` (UniqueId) de la pièce qui le contient. C'est une **liste** : une fiche par pièce, sous le même
  code. Une porte entre deux pièces est **un seul objet** avec deux valeurs `Space`.

Un élément sans pièce n'est pas exporté (anomalie `ELEMENT_WITHOUT_ROOM` dans `result.json`).

## Propriétés d'une pièce (codes en minuscules, `{section}_{propriété}`, section préfixée `a:` ou `b:` pour le tri)

| Code | Valeur | Condition |
| --- | --- | --- |
| `Category` | `Space` | toujours |
| `a:identification data_ouvrage` | paramètre `SGP-DI-Ouvrage` | si renseigné |
| `a:identification data_building` | `A` (constante, comme TRC) | toujours |
| `a:identification data_storey` | nom du niveau | si présent |
| `a:identification data_classification` | paramètre `SGP-DI-Typologie_Espace` | si renseigné |
| `a:identification data_numero` | paramètre `SGP-DI-Local_Code` | si renseigné |
| `bim_object_username` | `{niveau}_{nom}_{numéro}` sans espaces, suffixé `_1`, `_2`… en cas de doublon | toujours |
| `b:dimensions_surface` | surface, 2 décimales | si présente |
| `b:dimensions_volume` | volume en m³, 2 décimales | si le volume est calculé (`volumeMode: compute` ou réglage du modèle) |
| `b:dimensions_hauteur` | hauteur **effective** (mesurée, à défaut renseignée), 2 décimales | avec `roomHeights` |
| `b:dimensions_hauteur mesuree` | hauteur minimale mesurée par rayons | si un obstacle existe |
| `b:dimensions_hauteur renseignee` | `ROOM_HEIGHT` | si renseignée |
| `b:dimensions_hauteur non limitee` | hauteur non limitée | avec `roomHeights` |
| `b:dimensions_hauteur element limitant` | `Catégorie (modèle ou lien)` | si un obstacle existe |
| `c:parametres revit_<nom>` | un code par paramètre de la pièce, nom en minuscules (`c:parametres revit_hauteur limite`) ; nombres sur 4 décimales au plus, sans zéros inutiles | avec `roomParameters: "all"` ; paramètre sans valeur ou texte vide : non écrit |

## Propriétés d'une porte, d'un équipement ou d'un terminal

| Code | Valeur | Condition |
| --- | --- | --- |
| `Category` | `Door`, `<discipline> equipement` ou `<discipline> terminal` | toujours |
| `Space` | `bim_id` de la pièce, une fiche par pièce | toujours |
| `a:identification data_storey` | nom du niveau de l'instance | si présent |
| `a:identification data_numero` | numéro de porte (`DOOR_NUMBER`) | portes, si renseigné |
| `a:identification data_repere` | repère (`ALL_MODEL_MARK`) | si renseigné |
| `a:identification data_id revit` | ElementId (du modèle lié pour un élément de lien) | toujours |
| `a:identification data_categorie` | catégorie Revit (`Portes`, `Equipement de génie climatique`…) | toujours |
| `a:identification data_famille`, `a:identification data_type` | famille et type | si présents |
| `a:identification data_discipline` | `cvc`, `plomberie`, `electricite` ou `autres` | équipements et terminaux |
| `a:identification data_lien` | nom du modèle lié d'origine | élément d'un lien |
| `bim_object_username` | `{niveau}_{famille}_{ElementId}` sans espaces, unique parmi tous les objets | toujours |
| `b:porte_largeur` | passage libre, `0.90m` (avec `m`, comme TRC) | portes ; omis si inconnu |
| `b:porte_hauteur` | passage libre, `2.04` (sans unité, comme TRC) | portes ; omis si inconnu |
| `c:parametres revit_<nom>` | paramètres de l'instance, mêmes règles que pour la pièce | avec `roomParameters: "all"` |

Règles : **jamais de zéro** pour une dimension inconnue (fiche omise) ; un nombre s'écrit en mètres, point décimal, 2 décimales ;
une valeur de plus de 255 caractères devient « Voir détail en annexe » et son texte complet est dans `Details_Annexes/` (même démarche
que TRC). Les noms de catégories Revit sont ceux du moteur : l'Activity est lancée en **français** (`/l FRA`), comme celle de TRC.

Les propriétés qui dépendent d'une exigence (`b:dimensions_dimensions`, `b:equipements_maintenance`, `b:reseaux_traversant`, résistance au
feu…) ne sont pas produites : il n'y a pas de valeur cible dans l'extraction.

## Réglages à activer pour chaque donnée

`roomHeights` (hauteurs), `elements` (objets portes, équipements, terminaux), `roomImages` (images), `roomParameters: "all"` (un code
`c:parametres revit_…` par paramètre de la pièce et des éléments ; ces codes doivent exister côté TRC). L'identification (`a:`) et la surface
n'ont besoin d'aucun réglage.

## WorkItemRunner

Il décompresse le ZIP et affiche `TRCValidationDict.json` en **arbre** : une ligne par pièce, dépliable (▸) pour voir les portes, équipements
et terminaux dont le `Space` la désigne (une porte entre deux pièces apparaît sous chacune). Une colonne par code (valeurs multiples réunies
par ` | `), sans statut ni anomalies. `result.json` s'affiche de la même façon, avec une colonne `objet` (pièce, porte, équipement cvc…).
