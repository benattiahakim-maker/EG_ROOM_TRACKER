# Sortie ZIP et vues 3D des pièces

Implémentation : `src/Revit/RoomImageService.cs` (vues et export), `src/Application/TrcNaming.cs` (noms),
orchestration dans `src/Application/RoomExtractionService.cs`. Tests : `src/Tests/Application/RoomImagesTests.cs`, sans Revit.
**Non encore validé sur un vrai Revit ni dans Design Automation** : `Document.ExportImage` n'est pas garanti dans le moteur DA
(voir « Risque »).

## Sortie de l'Activity : un ZIP

Le paramètre de sortie `result` de l'Activity est le dossier `result/`, zippé par Design Automation (`zip: true`) :

```text
result.zip
├── result.json        contrat de sortie (docs/schema/result.schema.json)
└── Images/            seulement avec "roomImages": true
    └── <UniqueId>_<code_file>_<ElementId>_AdjustedCornerView_1.jpg
```

WorkItemRunner télécharge le ZIP, le décompresse dans le dossier du lancement (`result/`) et lit `result.json`. Les lancements
antérieurs (fichier `result.json` seul) restent lisibles.

## Réglages

| Clé | Défaut | Effet |
| --- | --- | --- |
| `roomImages` | `false` | `true` : une vue 3D par pièce placée, exportée dans `Images/` |
| `imageWidthPx` | `8000` | largeur en pixels, de 400 à 8000 (8000 : valeur de TRC, images de 1 Mo en moyenne ; une valeur plus basse allège le ZIP) |

Sans `roomImages`, rien ne change : pas de dossier `Images/`, ni dans `result.json` (`summary.images`, `summary.options.roomImages`).

## Nommage (codification TRC)

`{UniqueId de la pièce}_{code_file}_{ElementId de la pièce}_AdjustedCornerView_1.jpg`, par exemple
`4e29b322-…-0028f777_4bc8438f-…£64SDF-SOE-…-V5_2684791_AdjustedCornerView_1.jpg`. La plateforme retrouve l'image d'une pièce par
son `bim_id` (UniqueId), qui préfixe le nom : le JSON ne référence pas les images.

`code_file` : nom du RVT sans extension, `_` remplacés par `-`, suffixe `-détaché` retiré (`TrcNaming.CodeFile`). Le RVT du ZIP
d'entrée doit donc porter le nom attendu par TRC (`<GUID>£nom.rvt`). Le caractère `£` n'est pas ASCII : voir la section « Noms
de fichiers non ASCII » de `docs/INPUTS.md` (ZIP en UTF-8 ou fichier `.codepage`).

## Vue 3D d'une pièce (reprise de TRC)

Vue isométrique dont la caméra surplombe un coin de la pièce, section box autour de la pièce (valeurs de TRC reproduites), murs,
sols, toits, plafonds et modèles génériques rendus transparents et en grisé, annotations, niveaux et grilles masqués, style
d'affichage « Textures ». Les vues sont créées dans une transaction, puis exportées avec les réglages de TRC (`ExportImage` :
JPEG sans perte, 600 dpi, ajustée à la page en largeur, `imageWidthPx` pixels). Chaque image est ensuite **recadrée** comme
dans TRC (`CropWhitespace` : bords proches du blanc retirés, tolérance 10, réenregistrée en JPEG), par `ImageCropper`, qui lit
les pixels par blocs (`LockBits`) pour rester rapide à 8000 px. `System.Drawing.Common` est fournie par Revit et non
embarquée dans le bundle ; si elle n'est pas chargeable, l'image est conservée non recadrée (comportement de TRC en cas
d'échec). Le RVT est fermé sans sauvegarde.

## Anomalies et bilan

- `summary.images` : `requested`, `exported`, `failed`.
- `IMAGE_EXPORT_FAILED` (document, warning) : au moins une pièce sans image (détails `requested`, `exported`, `failed`, `reason`).
  L'extraction des pièces continue ; le statut passe à `succeededWithWarnings`.

## Risque

`Document.ExportImage` s'appuie sur le moteur graphique de Revit, dont la disponibilité n'est pas garantie dans Design
Automation. Si l'export échoue pour toutes les pièces, `IMAGE_EXPORT_FAILED` le dit (`exported: 0`) et le ZIP contient seulement
`result.json`. La parade serait de dessiner les images avec `System.Drawing`, comme les PNG de preuve de TRC. TRC exporte déjà
ses vues avec `ExportImage` et le résultat est fonctionnel : les réglages sont repris à l'identique.
