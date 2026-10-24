# Sortie `generic` : enveloppe commune des résultats (`outputFormat`)

Cette sortie écrit **`result.generic.json`**, au format « Enveloppe commune des résultats » de la plateforme, avec les « Ajouts RoomTracker »
(plusieurs `ref` de même clé, `group`, type `file`). Le TRC reste un export à part et ne change pas ([`TRC.md`](TRC.md)).

Source de vérité : `docs/frontend-contract.md` et `src/envelope.ts` du backend de la plateforme. En cas d'écart, la plateforme gagne.

Implémentation : `src/Contracts/Generic` (DTO), `src/Application/Generic/GenericMapper.cs` (conversion pure, sans Revit ni fichier),
`GenericExportService.cs` (écriture), orchestration dans `src/Application/RoomExtractionService.cs`. Schéma : `docs/schema/generic.schema.json`.
Exemples : `docs/examples/generic.example.json` (sortie), `docs/examples/settings.generic.example.json` (réglages). Tests :
`GenericMapperTests`, `GenericSchemaTests`, `GenericExportTests`, sans Revit.

## Réglage

```json
{ "schemaVersion": "1.0", "roomImages": true, "elements": ["doors", "equipment", "terminals"], "outputFormat": "generic" }
```

| `outputFormat` | Fichiers du dossier `result/` (ZIP de sortie) |
| --- | --- |
| `generic` | `result.generic.json` seul ; avec `roomImages`, le dossier `Images/` l'accompagne |

- **Défaut dans Design Automation** : quand `settings.json` n'a pas `outputFormat` (absent ou `null`), l'AppBundle utilise `generic`, car la
  plateforme n'envoie que `schemaVersion` et `mainModelPath` et attend `result.generic.json` (sinon `RESULT_MISSING`). Un `outputFormat`
  explicite (`contract`, `trc`, `both`, `generic`) est toujours respecté. Hors Design Automation (`SettingsReader` sans option, tests, outil
  local), le défaut reste `contract`. Code : `InputConfigurationService.ForDesignAutomation()`, appelé par `CompositionRoot`.
- Pas de `result.json` ni de TRC avec `generic` (comme `trc`). Le journal de l'extracteur reste dans le rapport APS.
- Une extraction **en échec** écrit `result.json` (statut `failed`, erreurs) et **pas** `result.generic.json` : l'enveloppe n'a que les statuts
  `done` et `partial`. La plateforme voit alors un résultat absent (`RESULT_UNAVAILABLE`).
- `contract`, `trc` et `both` ne changent pas.

## Contenu

Racine : `contractVersion` (`1.0`), `serviceId` (`pieces-roomtracker`), `status`, `warnings[]`, `models[]`, `objects[]`.

- **`status`** : `done` ; `partial` dès qu'il y a au moins un avertissement de résultat (pièce sans boîte, anomalie d'extraction, d'image,
  d'éléments…). `warnings[]` résume ces points en texte. **Exception : `LINK_MISSING`.** Les liens Revit absents de l'envoi sont nommés
  **tous dans un seul texte** (`LINK_MISSING (information) : 5 lien(s) Revit absent(s) de l'envoi : A.rvt, B.rvt…`), ajouté à la fin de
  `warnings[]`, et **ne rendent pas** l'enveloppe `partial` : un projet envoyé sans ses liens est `done`. Le contrat n'a pas de gravité
  par texte ; il accepte `done` avec des textes dans `warnings` (le validateur de la plateforme ne vérifie que leur type). Les objets
  d'anomalie `LINK_MISSING` restent dans `objects[]` (gravité `warning`). Aucun changement côté backend ; le site doit seulement
  afficher les `warnings` aussi pour `done` s'il veut montrer cette information.
- **`models[]`** : `main` (le RVT principal, titre = nom du fichier sans extension) et `link-1`, `link-2`… pour les modèles liés dont un
  élément est exporté. `urn` est **vide** : le plugin ne connaît pas l'URN de conversion (voir « Écarts »).
- **Objet** : `{ id, name, children[], properties[] }`, `id` unique dans tout le fichier.
- **Propriété** : `{ key, label, type, value, unit?, group? }`. Types : `text`, `number` (avec `unit` : `m`, `m²`, `m³`), `boolean`, `date`,
  `box`, `ref`, `file`. Les valeurs sont typées, jamais du texte arrondi ; une valeur inconnue est omise, jamais mise à zéro ; pas de limite
  de 255 caractères (la limite TRC ne s'applique pas ici).
- **Aucun champ `kind` ni `category` sur l'objet.** `Category` est une propriété ordinaire (`Space`, `Door`, `cvc equipement`… comme le TRC ;
  `Anomaly` et `Files` pour les objets d'anomalie).

### Pièce (objet racine)

| Élément | Valeur |
| --- | --- |
| `id` | `room-{ElementId}` |
| `name` | `{numéro} {nom}` |
| `viewerId` | `UniqueId` Revit de la pièce (propriété `text` réservée) |
| `id_revit`, `unique_id`, `Category` (`Space`), `modelKey` (`main`) | propriétés ordinaires |
| `box` | boîte englobante (mm, repère du modèle principal) ; omise avec un avertissement si la pièce n'est pas placée |
| `image` (type `file`) | vue 3D `/Images/{nom TRC}.jpg`, seulement si le fichier existe réellement |
| surface, volume, hauteur, finitions, paramètres | nombres et textes typés, groupés (`group`) |
| `contains` (`ref`, répétable) | éléments rangés sous une autre pièce mais contenus aussi dans celle-ci |
| enfants | éléments rangés ici, puis anomalies de la pièce |

La `box` vient de `Room.get_BoundingBox(null)`, convertie en millimètres (`CollectedRoom.BoundingBox`, lue à la collecte, y compris pour une
pièce en `EXTRACTION_ERROR`). Elle est alignée sur les axes : fidèle pour une pièce rectangulaire, englobante pour une pièce en L.

### Porte, équipement, terminal (objets)

- **Un seul objet** par élément, rangé **une fois** sous la **première pièce** qui le contient (ordre des pièces du résultat, c'est-à-dire par
  `ElementId`). `id` = `element-{UniqueId}`, `viewerId` = son `UniqueId`.
- L'objet pointe **toutes** ses pièces par des `ref` de clé `room` (répétée). Une porte ajoute `room_from` et `room_to` (côté `from` et `to`
  de la porte). Chaque autre pièce le pointe par un `ref` `contains`.
- Un élément d'un modèle lié porte `modelKey` (`link-n`), `modele_lie` (`true`) et `lien` (nom du lien) ; sa `box` n'est pas exportée.
- Paramètres d'occurrence et de type : propriétés typées, groupes « Paramètres d'occurrence » et « Paramètres de type », clés préfixées
  `occurrence:` et `type:`. Les unités des paramètres Revit ne sont pas connues du plugin : ce sont des nombres sans `unit`.

### Anomalies

Une anomalie est un objet `anomaly-{n}` (`Category` = `Anomaly`) avec `code`, `severity`, `message`, `details` (texte JSON). Portée pièce :
enfant de la pièce ; portée document : objet racine.

### Fichiers

Les fichiers du résultat que le plugin sait désigner sont des propriétés `file` (`{ name, mimeType, url }`, `url` = chemin relatif commençant par
`/`, jamais `http:`). Aujourd'hui : les images de pièces. Le TRC et le ZIP ne sont pas listés (voir « Écarts »).

## Écarts avec la spécification de la plateforme

1. **`urn` vide.** `validateEnvelope` exige un `urn` non vide pour chaque entrée de `models[]` : la plateforme doit le renseigner
   (`conversion.urn` du traitement) avant de valider, sinon l'enveloppe est refusée (`RESULT_INVALID`). Le plugin ne le connaît pas.
2. **`ref` sans rôle.** Une propriété `ref` n'a que `value` (l'`id` cible) : le rôle est porté par la **clé** (`room`, `room_from`, `room_to`,
   `contains`).
3. **Objet partagé.** La spécification autorise un objet identique sous chaque parent ; ce plugin range l'objet **une fois** et utilise des
   `ref` (décision de l'équipe), ce que le validateur accepte aussi.
4. **TRC et ZIP.** Avec `generic` seul, aucun TRC n'est écrit et le plugin ne connaît ni le nom ni l'URL du ZIP : ils ne sont pas des propriétés
   `file`. `GenericMapContext.ResultFiles` sait les lister si une option future écrit les deux.
5. **Échec.** Pas d'enveloppe pour une extraction en échec (voir plus haut).

## Limites

Non validé sur un vrai Revit ni dans Design Automation (aucun test APS payant dans cette étape).
