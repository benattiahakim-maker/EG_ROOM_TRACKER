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

`includeSecondaryDesignOptions` (booléen, `false` par défaut) : voir « Options de conception » ci-dessous.

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

- **`status`** et **`warnings[]`** : un élément de `warnings[]` est un **texte** (= avertissement) ou un objet
  `{ severity: "info" | "warning", message, code? }` (`message` : 400 caractères au plus ; `code` : lettres, chiffres, `-`, `_`, 64 au plus, commence par une lettre ou un
  chiffre ; 20 éléments au plus, un seul message `info` par code). `status` vaut `done`, ou `partial` dès qu'il y a au moins un **avertissement** (pièce sans boîte, anomalie
  d'extraction, d'image, d'éléments…, écrits comme des textes). Une **information** ne rend jamais l'enveloppe `partial`.
  **`LINK_MISSING`** : les liens Revit absents de l'envoi sont nommés **tous dans une seule information**
  `{ "severity": "info", "code": "LINK_MISSING", "message": "5 lien(s) absent(s) : A.rvt, B.rvt…" }`, placée après les avertissements :
  un projet envoyé sans ses liens est `done`. Au-delà de 400 caractères, la liste est coupée sur un nom entier et finit par « … et K autre(s) ». Les liens absents ne produisent **aucun** objet « Anomalie » (`anomaly-N`) : seule cette information les dit. Les autres anomalies (par exemple `LINK_NOT_LOADED`) restent des objets.
  **`VOLUME_NOT_COMPUTED`** (document dont le calcul des volumes est désactivé) suit la même règle : **une seule information**
  `{ "severity": "info", "code": "VOLUME_NOT_COMPUTED", "message": "N pièce(s) sans volume calculé : calcul des volumes désactivé dans le document (volumeM3 exporté à null ; mode preserve)." }`
  (N = pièces sans volume, `roomsAffected` de l'anomalie, le même nombre que le journal), jamais `partial`, aucun objet « Anomalie » ; le même texte est écrit dans `log.jsonl` (événement `VOLUME_NOT_COMPUTED`, après `ANOMALIES_DETECTED`).
  `warnings[]` ne dépasse jamais 20 éléments : les informations sont toujours gardées, les avertissements en trop sont remplacés par un dernier avertissement « N autre(s) avertissement(s) non détaillé(s) ».
  Ce format demande que la plateforme (validation) et le site acceptent les objets dans `warnings[]` ; un texte seul reste valide.
- **`models[]`** : `main` (le RVT principal, titre = nom du fichier sans extension) et `link-1`, `link-2`… pour les modèles liés dont un
  élément est exporté. `urn` est **vide** : le plugin ne connaît pas l'URN de conversion (voir « Écarts »).
- **Objet** : `{ id, name, children[], properties[] }`, `id` unique dans tout le fichier.
- **Propriété** : `{ key, label, type, value, unit?, group? }`. Types : `text`, `number` (avec `unit` : `m`, `m²`, `m³`), `boolean`, `date`,
  `box`, `ref`, `file`. Les valeurs sont typées, jamais du texte arrondi ; une valeur inconnue est omise, jamais mise à zéro ; pas de limite
  de 255 caractères (la limite TRC ne s'applique pas ici).
- **Aucun champ `kind` ni `category` sur l'objet.** `Category` est une propriété ordinaire (`Space`, `Door`, `cvc equipement`… comme le TRC ;
  `Level` pour un niveau, `Group` pour un groupe, `Anomaly` et `Files` pour les objets d'anomalie).

### Arbre des objets

```
Niveau (altitude croissante) ; « Sans niveau » en dernier
└─ Pièce (tri naturel du numéro)
   ├─ Portes          (groupe, seulement s'il contient des portes)
   │   └─ porte…
   ├─ Équipements     (groupe, seulement s'il n'est pas vide)
   ├─ Terminaux       (groupe, seulement s'il n'est pas vide)
   └─ anomalies de portée pièce
anomalies de portée document restantes (ex. LINK_NOT_LOADED), puis « Fichiers du résultat »
```

- **Niveau** : un objet par niveau Revit qui a au moins une pièce exportée. `id` = `level-{UniqueId}`, `name` = nom du niveau,
  `Category` = `Level`, `categorie_libelle` = `Niveau`, `altitude` (`number`, `m`, `Level.Elevation`) quand elle est connue. Tri par altitude
  croissante ; un niveau sans altitude lisible vient après les niveaux avec altitude, puis « Sans niveau » (`level-none`, pour les pièces sans
  niveau) est toujours le dernier. Une pièce est listée **une seule fois**, sous son niveau. L'altitude n'est pas écrite dans `result.json`.
- **Pièces d'un niveau** : tri **naturel** du numéro (2 avant 10, `a2` avant `A10`, casse ignorée, nombres comparés comme des nombres) ; un
  numéro vide vient en dernier ; à numéro égal, tri par `ElementId`.
- **Groupes** : objets de regroupement (`name` + `children`), `id` = `{id de la pièce}-portes`, `-equipements`, `-terminaux`, `Category` = `Group`,
  `categorie_libelle` = `Portes` / `Équipements` / `Terminaux`, aucune autre propriété. Ordre fixe : Portes, Équipements, Terminaux ; un groupe
  vide est omis. Les terminaux ont leur propre groupe (décision). Enfants d'un groupe triés par nom (casse ignorée) puis `UniqueId`.
- **Aucun code propre à RoomTracker** n'est nécessaire dans le backend ni le site : ce sont des objets génériques (nom, enfants, propriétés).

### Pièce

| Élément | Valeur |
| --- | --- |
| `id` | `room-{ElementId}` |
| `name` | `{numéro} {nom}` |
| `viewerId` | `UniqueId` Revit de la pièce (propriété `text` réservée) |
| `id_revit`, `unique_id`, `Category` (`Space`), `categorie_libelle` (`Pièce`), `modelKey` (`main`) | propriétés ordinaires |
| `box` | boîte englobante (mm, repère du modèle principal) ; omise avec un avertissement si la pièce n'est pas placée |
| `image` (type `file`) | vue 3D `/Images/{nom TRC}.jpg`, seulement si le fichier existe réellement |
| surface, volume, hauteur, finitions, paramètres | nombres et textes typés, groupés (`group`) |
| enfants | groupes Portes, Équipements, Terminaux (non vides), puis les anomalies de la pièce |
| `contains` (`ref`, répétable) | tous les éléments contenus dans la pièce (ils sont aussi ses petits-enfants, dans les groupes) |

**`Category` et `categorie_libelle`.** `Category` garde la valeur technique (`Space`, `Door`, `cvc equipement`, `Anomaly`, `Files`, comme le
TRC). `categorie_libelle` (texte, groupe `Identification`) donne le libellé lisible : `Pièce`, `Porte`, `Équipement CVC`, `Terminal electricite`
(discipline telle que dans la valeur technique, `cvc` en `CVC`), `Anomalie`, `Fichiers`. Présent sur tous les objets.

**Arrondis.** Nombres avec unité (surface, volume, hauteurs : `m`, `m²`, `m³`) : 3 décimales. Coordonnées de la `box` (mm) : 1 décimale. Paramètres
Revit (unité inconnue du plugin) : 12 chiffres significatifs, ce qui retire seulement le bruit de virgule flottante (`2.6999999999999997` devient
`2.7`) ; les valeurs entières et zéro ne changent pas. Une valeur inconnue reste omise, jamais mise à zéro.

**Unité de la `box`.** La `box` est toujours en **millimètres**, repère du modèle principal (contrat de la plateforme). Elle est arrondie (1 décimale) et n'a **pas** de clé
`unit` : ne jamais en ajouter (le validateur de la plateforme n'accepte `unit` que sur le type `number` et refuserait l'enveloppe, `RESULT_INVALID`).
Le libellé de la propriété dit « (mm) ». Décision PM : c'est la règle, pas une étape provisoire.

La `box` vient de `Room.get_BoundingBox(null)`, convertie en millimètres (`CollectedRoom.BoundingBox`, lue à la collecte, y compris pour une
pièce en `EXTRACTION_ERROR`). Elle est alignée sur les axes : fidèle pour une pièce rectangulaire, englobante pour une pièce en L.

### Options de conception

Une pièce peut appartenir à une option de conception Revit. Par défaut (`includeSecondaryDesignOptions: false`), la sortie `generic`
n'exporte que les pièces **hors option** et celles de l'**option principale** de leur jeu (`DesignOption.IsPrimary`). Les pièces des
options secondaires sont exclues avec leurs éléments et leurs anomalies de pièce, ce qui évite les surfaces comptées deux fois et les
emprises qui se chevauchent. Une information `{ severity: "info", code: "DESIGN_OPTIONS_EXCLUDED", message: "N pièce(s)… exclues" }`
donne le nombre ; elle ne rend pas l'enveloppe `partial`. Si Revit ne dit pas si l'option est principale, la pièce est conservée.
Une pièce exclue n'a ni fichier dans `Images/` ni propriété image, et `summary.images` ne compte que les pièces conservées (PR #67).
Avec `true`, toutes les pièces sont exportées. Avec `contract`, `trc` ou `both`, les pièces restent dans le résultat avec leur image.

Chaque pièce porte `option_conception` (texte) : `Principal` hors option, le nom de l'option suivi de `(principale)` pour l'option principale
(ex. `Unoccupied (principale)`), le nom de l'option sinon. `variante` est nettoyée : le repère `<principal>` que Revit ajoute au nom
est retiré, les chevrons restants aussi, les espaces sont normalisés (`Unoccupied  <principal>` donne `Unoccupied`). Ces réglages ne
changent ni `result.json` ni le TRC.

### Porte, équipement, terminal (objets)

- **Objet partagé** : un élément contenu dans plusieurs pièces (une porte entre deux pièces, par exemple) est **répété sous chacune**, dans le
  groupe de sa famille : même `id` (`element-{UniqueId}`), contenu strictement identique (mêmes propriétés, mêmes `ref`). Le validateur de la
  plateforme accepte un `id` répété si le contenu est identique et ne parcourt le sous-arbre qu'une fois. `viewerId` = son `UniqueId`.
- L'objet pointe **toutes** ses pièces par des `ref` de clé `room` (répétée), la même liste sous chaque parent. Une porte ajoute `room_from` et
  `room_to` (côté `from` et `to` de la porte). Chaque pièce pointe tous ses éléments par des `ref` `contains`.
- Un élément d'un modèle lié porte `modelKey` (`link-n`), `modele_lie` (`true`) et `lien` (nom du lien) ; sa `box` n'est pas exportée.
- Paramètres d'occurrence et de type : propriétés typées, groupes « Paramètres d'occurrence » et « Paramètres de type », clés préfixées
  `occurrence:` et `type:`. **Les valeurs sont celles de Revit, sans aucune conversion** : elles peuvent être en unités anglaises
  (`Btu/h`, `W/ft²`, `%`…). Quand la propriété porte une `unit`, c'est l'unité de la valeur ; aujourd'hui le plugin ne connaît pas l'unité
  d'un paramètre Revit et écrit un nombre sans `unit`. Décision PM : pas de conversion maintenant, sujet à rediscuter avec Hakim.

### Anomalies

Une anomalie est un objet `anomaly-{n}` (`Category` = `Anomaly`) avec `code`, `severity`, `message`, `details` (texte JSON). Portée pièce :
enfant de la pièce ; portée document : objet racine. **Exceptions : `LINK_MISSING` et `VOLUME_NOT_COMPUTED`** ne sont jamais des objets (une information chacune dans `warnings[]`). Les anomalies de portée document restantes (par exemple `LINK_NOT_LOADED`) sont des objets racines, placés après les objets « Niveau ».

### Fichiers

Les fichiers du résultat que le plugin sait désigner sont des propriétés `file` (`{ name, mimeType, url }`, `url` = chemin relatif commençant par
`/`, jamais `http:`). Aujourd'hui : les images de pièces. Le TRC et le ZIP ne sont pas listés (voir « Écarts »).

## Écarts avec la spécification de la plateforme

1. **`urn` vide.** `validateEnvelope` exige un `urn` non vide pour chaque entrée de `models[]` : la plateforme doit le renseigner
   (`conversion.urn` du traitement) avant de valider, sinon l'enveloppe est refusée (`RESULT_INVALID`). Le plugin ne le connaît pas.
2. **`ref` sans rôle.** Une propriété `ref` n'a que `value` (l'`id` cible) : le rôle est porté par la **clé** (`room`, `room_from`, `room_to`,
   `contains`).
3. **Objet partagé.** Pas d'écart : comme le prévoit la spécification, l'objet identique (même `id`, même contenu) est répété sous chaque
   parent ; les `ref` `room`, `room_from`, `room_to`, `contains` restent en plus.
4. **TRC et ZIP.** Avec `generic` seul, aucun TRC n'est écrit et le plugin ne connaît ni le nom ni l'URL du ZIP : ils ne sont pas des propriétés
   `file`. `GenericMapContext.ResultFiles` sait les lister si une option future écrit les deux.
5. **Échec.** Pas d'enveloppe pour une extraction en échec (voir plus haut).

## Limites

Non validé sur un vrai Revit ni dans Design Automation (aucun test APS payant dans cette étape).
