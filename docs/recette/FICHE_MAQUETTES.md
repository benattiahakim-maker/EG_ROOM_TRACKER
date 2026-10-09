# Fiche de construction des maquettes de référence (EGR-18)

À suivre dans Revit, pour chaque année (2025, 2026, 2027). Le résultat attendu de chaque cas est décrit dans
[`docs/RECETTE.md`](../RECETTE.md) ; les valeurs ci-dessous doivent être saisies **exactement** (casse, accents,
espaces, zéros initiaux). Les noms de commandes sont donnés en anglais (interface FR entre parenthèses quand elle
est connue).

## 0. Préparation

- Une maquette par année : soit construire les maquettes dans chaque version (recommandé pour la première année),
  soit construire en 2025 puis ouvrir et **enregistrer sous** dans 2026 et 2027 (mise à niveau). Noter la méthode
  retenue dans la matrice EGR-14 (question Q3 de RECETTE.md).
- Dossier de travail : `EGR18_<année>/` contenant `EGR_Ref.rvt`, `EGR_Ref_Volumes.rvt`, `EGR_Collab.rvt`,
  `EGR_Futur.rvt` (sauf 2027) et `Projet/` (`EGR_Liens.rvt`, `Liens/EGR_Lien_A.rvt`, `Liens/EGR_Lien_B.rvt`,
  `Liens/EGR_Lien_C.rvt`).
- Gabarit : gabarit architectural **métrique** fourni (unités mm / m²). Vérifier Manage → Project Units : longueur
  en mm, surface en m², volume en m³ (les unités d'affichage n'influent pas sur le résultat, mais facilitent la
  saisie).
- Les pièces sont délimitées par des **lignes de séparation de pièce** (Architecture → Room Separator), pas par des
  murs : la surface vaut exactement le rectangle dessiné, sans dépendre de l'épaisseur des murs ni de la règle de
  calcul « à la face de finition ».
- Créer les pièces **dans l'ordre P01 → P13** (les ElementId croissent en principe dans l'ordre de création ; le
  résultat est trié par ElementId, `bind` recalcule l'ordre de toute façon).

## 1. EGR_Ref.rvt (cas R01, R03, R04, R06, R10, R11)

### 1.1 Niveaux et phases

1. Garder deux niveaux : `R+0` à 0,000 et `R+1` à +3,000 (renommer, supprimer les autres niveaux et leurs vues si
   besoin ; accepter de renommer les vues correspondantes).
2. Manage → Phases (Phases) : deux phases exactement, dans cet ordre : `Existant`, puis `Nouvelle construction`
   (renommer `Existing` / `New Construction` si le gabarit est anglais).
3. Area and Volume Computations (Architecture → Room & Area ▾ → Area and Volume Computations) → onglet
   Computations : cocher **Areas only** (« Surfaces uniquement »). C'est indispensable : « Areas and Volumes » est
   la valeur par défaut. Laisser « Room Area Computation » sur « At wall finish ».

### 1.2 Paramètres partagés (avant les pièces)

1. Manage → Shared Parameters → Browse : choisir `docs/recette/EGR_Recette_SharedParameters.txt` (groupe
   `EGR_Recette`).
2. Manage → Project Parameters → Add → Shared parameter → Select, puis pour chacun : **Instance**, catégorie
   **Rooms** uniquement, groupe « Data » (indifférent) :
   `EGR_Texte` (Text), `EGR_Entier` (Integer), `EGR_Nombre` (Number), `EGR_Longueur` (Length), `EGR_Surface`
   (Area), `EGR_Volume` (Volume), `EGR_OuiNon` (Yes/No), `EGR_Angle` (Angle).
3. Ajouter `EGR_MurSeulement` (Text), Instance, catégorie **Walls** uniquement (pas Rooms) : sert au cas R06.
4. Si Revit refuse le fichier (format historique, point C24) : créer un nouveau fichier de paramètres partagés depuis
   la boîte de dialogue, avec les mêmes noms et types. Les GUID seront alors différents : relever chaque GUID (par
   exemple avec un schedule ou une macro qui lit `SharedParameterElement.GuidValue`) et remplacer les GUID dans
   `docs/recette/settings/R01.json`, `R06.json` (`murSeulement`) et dans les attendus (`bind` ne les modifie pas).
   Le GUID `inconnu` (`6f79aade-…`) ne doit **jamais** exister dans les maquettes.

### 1.3 Pièces du niveau R+0, phase Nouvelle construction

Dans la vue en plan `R+0` (propriétés de vue : Phase = `Nouvelle construction`, Phase Filter = « Show All » ou
« Show Complete »). Pour chaque pièce : dessiner un rectangle fermé en lignes de séparation, aux dimensions
**intérieures** indiquées, isolé des autres (laisser au moins 1 m entre deux rectangles), puis Architecture → Room
et cliquer à l'intérieur. Dans les propriétés de la pièce : Upper Limit = `R+0`, **Limit Offset = 2700 mm**, Base
Offset = 0. Saisir ensuite Name, Number et les autres valeurs.

| # | Rectangle | Name | Number | Occupancy | Floor / Wall / Ceiling / Base Finish | Paramètres partagés |
| --- | --- | --- | --- | --- | --- | --- |
| P01 | 5,000 × 4,000 | `Bureau d'étude` | `001` | `Bureau` | `Parquet chêne` / `Peinture blanche` / `Faux plafond 60×60` (signe ×, U+00D7) / `Plinthe bois` | EGR_Texte `A-001` ; EGR_Entier `42` ; EGR_Nombre `3.5` ; EGR_Longueur `1250 mm` ; EGR_Surface `12.5 m²` ; EGR_Volume `2.4 m³` ; EGR_OuiNon **coché** ; EGR_Angle : ne rien saisir |
| P02 | 4,000 × 3,000 | `Salle de réunion « Été »` (guillemets français U+00AB/U+00BB, espaces simples) | `0010` | `Réunion` | `Moquette` / `Enduit à la chaux` / `Dalles acoustiques` / `Plinthe carrelage` | EGR_Texte `Œuvre-ÀÉÎÕÜ-ç-ß` ; EGR_Entier `0` ; EGR_Nombre `0` ; EGR_Longueur `0` ; EGR_Surface `0` ; EGR_Volume `0` ; EGR_OuiNon **coché puis décoché** ; EGR_Angle `0°` |
| P03 | 3,000 × 2,000 | `Local technique` | `002` | ne rien saisir | Floor `Résine` ; Wall : saisir `x`, valider, **effacer**, valider ; Ceiling `Brut` ; Base : ne jamais toucher | EGR_Texte : saisir `x`, valider, **effacer**, valider ; rien d'autre |
| P04 | 2,000 × 2,000 provisoire | `Réserve` | `001` (accepter l'avertissement éventuel) | `Stockage` | — | EGR_Texte `R-001` |
| P05 | voir ci-dessous | `Couloir` | `003` | `Circulation` | — | — |
| P06 | 2,000 × 2,000 | `Archives` | `1` | `Archives` | — | — |
| P07 | 3,000 × 1,000 | `Dégagement` | **vide** (effacer le numéro proposé) | — | — | — |
| P08 | 1,000 × 1,000 | `Placard` | **vide** | — | — | — |
| P09 | 2,500 × 2,000 | `Sans attributs` | `004` | — | — | — |

Points particuliers :

- **P02** : la saisie de `0` pour l'angle sert seulement à vérifier qu'un paramètre de type non pris en charge ne
  bloque pas ; l'attendu ne contient pas l'angle (seulement l'anomalie `UNSUPPORTED_PARAMETER_TYPE`).
- **P04 (non placée)** : après la saisie, sélectionner la pièce dans le plan et la **supprimer** (Delete). Revit
  indique que la pièce reste dans le projet (non placée) : accepter. Supprimer ensuite les lignes de séparation
  provisoires. Vérifier dans un schedule de pièces (avec Hide/List Unplaced Rooms) que `Réserve` apparaît « Not
  Placed ».
- **P05 (non fermée)** : dessiner trois côtés d'un rectangle 4,000 × 1,500 (laisser un côté ouvert), puis placer la
  pièce à l'intérieur. Revit avertit « Room is not in a properly enclosed region » : accepter. La pièce reste placée
  avec la surface « Not Enclosed ». Ne pas fermer le rectangle.
- **P07 / P08** : si Revit refuse un numéro vide ou en remet un automatiquement, noter le comportement (point C12) et
  laisser le numéro proposé ; l'écart sera signalé `À CONFIRMER`.
- Ne rien saisir dans les champs non cités (en particulier ne pas cliquer dans les cases Oui/Non des autres pièces).

### 1.4 Phase Existant (P10)

Dupliquer la vue `R+0` (Duplicate), régler sa propriété Phase = `Existant`. Y dessiner le rectangle 3,000 × 3,000
(les lignes appartiennent alors à la phase Existant) et placer la pièce : Name `Archives existantes`, Number `001`,
Upper Limit `R+0`, Limit Offset 2700 mm. Vérifier dans les propriétés que la phase de la pièce est `Existant`.

### 1.5 Variantes (P11, P12)

1. Manage → Design Options : créer un jeu d'options `Variantes` (nom indifférent) avec deux options nommées
   exactement `Option A` (principale) et `Option B` (renommer `Option 1`, `Option 2`).
2. Edit Option `Option A` : dans la vue `R+0` (phase Nouvelle construction), dessiner un rectangle 3,000 × 2,500 et
   placer une pièce `Bureau variante A`, Number `010`, Limit Offset 2700 mm. Finish.
3. Edit Option `Option B` : au même endroit, rectangle 3,000 × 2,500, pièce `Bureau variante B`, Number `010`,
   Limit Offset 2700 mm. Finish. **Ne pas** accepter la variante principale (pas de « Accept Primary »).

### 1.6 Niveau R+1 (P13)

Vue `R+1`, phase Nouvelle construction : rectangle 5,000 × 4,000, pièce `Combles aménagés`, Number `101`, Upper
Limit `R+1`, Limit Offset 2700 mm.

### 1.7 Contrôle et enregistrement

- Schedule de pièces (Name, Number, Area, Volume, Level, Phase, Design Option, Occupancy, finitions, paramètres
  EGR_*) : vérifier les 13 pièces et les surfaces (20 ; 12 ; 6 ; Not Placed ; Not Enclosed ; 4 ; 3 ; 1 ; 5 ; 9 ;
  7,5 ; 7,5 ; 20). La pièce de phase Existant n'apparaît que dans un schedule de cette phase.
- Purge facultative, puis Save As `EGR_Ref.rvt` (fichier non collaboratif). Fermer Revit sans autre modification.

## 2. EGR_Ref_Volumes.rvt (cas R02)

Ouvrir `EGR_Ref.rvt` → Area and Volume Computations → **Areas and Volumes** → OK → Save As
`EGR_Ref_Volumes.rvt`. Ne rien modifier d'autre. Contrôle : volume 54 m³ pour Bureau d'étude, 20,25 m³ pour les
variantes. Le volume de Couloir (non fermée) est à relever.

## 3. EGR_Liens.rvt et ses liens (cas R05)

1. Créer trois projets minimaux `EGR_Lien_A.rvt`, `EGR_Lien_B.rvt`, `EGR_Lien_C.rvt` (un mur suffit), enregistrés
   dans `Projet/Liens/`.
2. Créer `Projet/EGR_Liens.rvt` : niveau `R+0`, Areas only (comme §1.1-3), un rectangle 6,000 × 5,000, pièce `Hall`,
   Number `100`, Occupancy `Accueil`, Limit Offset 2700 mm. Pas de paramètre partagé.
3. Insert → Link Revit : lier A, B et C (Positioning : Auto - Origin to Origin). Dans Manage Links, vérifier **Path
   Type = Relative** pour les trois (valeur par défaut).
4. Manage Links → sélectionner `EGR_Lien_C.rvt` → **Unload** (Décharger). Statut attendu : Not Loaded.
5. Enregistrer l'hôte. Le ZIP R05 contient `Projet/EGR_Liens.rvt`, `Projet/Liens/EGR_Lien_A.rvt` et
   `Projet/Liens/EGR_Lien_C.rvt` ; **ne pas** inclure `EGR_Lien_B.rvt` (il devient « Not Found » dans le WorkItem).
6. Variante facultative (point C18) : refaire le ZIP en renommant le dossier `Projet` en `Projet_Été` (et
   `mainModelPath` en conséquence) pour vérifier les noms accentués ; résultat attendu identique, sauf
   `source.relativePath`.

## 4. EGR_Collab.rvt (cas R07)

1. Nouveau projet : niveau `R+0`, Areas only, deux rectangles séparés : 4,000 × 4,000 (pièce `Pièce workset A`,
   Number `201`) et 4,000 × 3,000 (pièce `Pièce workset B`, Number `202`), Limit Offset 2700 mm.
2. Collaborate → Worksets : activer le partage. Créer `Workset A` et `Workset B`. Affecter la pièce A et ses lignes à
   `Workset A`, la pièce B et ses lignes à `Workset B`.
3. Enregistrer sous `EGR_Collab.rvt` (Options : « Make this a Central Model after save » coché). Créer ensuite une
   copie locale, y passer `Workset B` en **Closed**, puis synchroniser avec le central. Fermer.
4. ZIP : uniquement `EGR_Collab.rvt` (le central, pas la copie locale ni le dossier `EGR_Collab_backup`).
   Attendu : `isWorkshared: true` et les deux pièces (l'extracteur ouvre tous les worksets).

## 5. EGR_Futur.rvt (cas R09, années 2025 et 2026)

Pour l'engine N, enregistrer une copie de `EGR_Ref.rvt` avec Revit **N + 1** sous `EGR_Futur.rvt` (ex. 2026 pour
l'engine 2025, 2027 pour l'engine 2026).

## 6. ZIP et settings

| Cas | ZIP (chemins à la racine) | settings |
| --- | --- | --- |
| R01 | `EGR_Ref.rvt` | `docs/recette/settings/R01.json` |
| R02 | `EGR_Ref_Volumes.rvt` | `R02.json` |
| R03 | `EGR_Ref.rvt` | `R03.json` : remplacer `9000011` par l'ElementId de la phase Existant (journal de `bind` R01) |
| R04 | `EGR_Ref.rvt` | `R04.json` |
| R05 | `Projet/EGR_Liens.rvt`, `Projet/Liens/EGR_Lien_A.rvt`, `Projet/Liens/EGR_Lien_C.rvt` | `R05.json` |
| R06 | `EGR_Ref.rvt` | `R06.json` |
| R07 | `EGR_Collab.rvt` | `R07.json` |
| R08 | `EGR_Ref.rvt`, `EGR_Ref_Volumes.rvt` | `R08.json` |
| R09 | `EGR_Futur.rvt` | `R09.json` |
| R10 | `EGR_Ref.rvt` | `R10.json` |
| R11 | `EGR_Ref.rvt` | `R11.json` (`volumeMode: compute`) |

- Créer les ZIP de façon que les fichiers soient **à la racine** (zipper le contenu, pas le dossier parent), avec
  des noms en UTF-8 : 7-Zip est recommandé (`7z a -tzip R05.zip Projet`, UTF-8 pour les noms non ASCII). Pour des
  noms ASCII uniquement, l'Explorateur Windows convient. Contrôler avec `python -m zipfile -l R05.zip`.
- Revit ne doit pas avoir le fichier ouvert au moment du zip.

## 7. Exécution et comparaison

1. Publier en test et lancer un WorkItem par cas ([PUBLISHING.md](../PUBLISHING.md) §5-6), avec le ZIP et le
   `settings.json` du cas. Télécharger `result.json` (→ `resultats/<année>/<cas>.json`) et le rapport.
2. Première exécution de l'année : `bind` de chaque gabarit de succès (R01-R03, R05-R07, R11), puis relecture du journal
   (appariement des 13 pièces, phases, variantes). Les cas d'échec (R04, R08-R10) n'ont pas d'identifiants :
   `bind` les recopie avec seulement `revit.version`.

   ```bash
   for c in R01 R02 R03 R04 R05 R06 R07 R08 R09 R10 R11; do
     python3 tools/recette/compare_result.py bind docs/recette/expected/$c.json resultats/2026/$c.json \
         --year 2026 -o docs/recette/expected/2026/$c.json
   done
   ```

3. Comparer chaque cas et reporter le bilan dans la matrice EGR-14 :

   ```bash
   python3 tools/recette/compare_result.py compare docs/recette/expected/2026/R01.json resultats/2026/R01.json --case R01
   ```

4. Pour chaque `À CONFIRMER`, noter la valeur réelle dans la matrice ; la décision (corriger l'attendu ou le code)
   se prend avec l'équipe (RECETTE.md §5).
5. Contrôles manuels : la source du ZIP n'est pas modifiée, aucun dialogue (rapport), `result.json` présent même en
   échec (D6 : R04, R08, R09, R10), durée du WorkItem (D9) ; pour R11, événement de log `VOLUME_COMPUTATION`
   (`computeApplied: true`, `transactionRolledBack: true`) dans le rapport.
