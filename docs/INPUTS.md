# Entrées : ZIP, settings.json et RVT principal (EGR-05)

Implémentation : `src/Application/Configuration` (`InputConfigurationService`, `SettingsReader`, `MainModelResolver`).
Tests : `src/Tests/Configuration`, exécutés sans Revit.

## Contrat de l'Activity

| Paramètre | Sens | Nom local | Rôle |
| --- | --- | --- | --- |
| ZIP de maquette | entrée | `input/` (décompression `zip: true`) | répertoire d'entrée : tous les chemins y sont confinés |
| `settings.json` | entrée | `settings.json` | configuration (section 2 des spécifications) |
| `result.json` | sortie | `result.json` | contrat de résultat unique |

Noms alignés sur `src/EntryPoint/ActivityContract.cs`. La définition de l'Activity relève d'EGR-13, la validation
des chemins après décompression dans un vrai WorkItem relève d'EGR-14.

## Ordre des contrôles

1. `settings.json` est entièrement validé : erreurs `CONFIG_INVALID` et `CONFIG_SCHEMA_UNSUPPORTED`, étape `configuration`.
2. Le RVT principal est ensuite résolu dans `input/` : `PATH_OUTSIDE_INPUT` (étape `configuration`), `RVT_NOT_FOUND`
   et `RVT_AMBIGUOUS` (étape `input`).

Le service retourne la configuration **telle que lue**, complétée par deux champs :

- `ResolvedMainModelPath` : toujours renseigné ; utilisé par `IRevitDocumentService` (EGR-06) ;
- `PhaseElementIds` : `phaseIds` converti en `long`.

## Règles de settings.json

| Clé | Règle | Erreur |
| --- | --- | --- |
| (fichier) | Présent, au plus 1 Mio, UTF-8 (avec ou sans BOM), JSON strict (pas de commentaire ni de virgule finale), racine objet, pas de propriété en double | `CONFIG_INVALID` |
| `schemaVersion` | Chaîne obligatoire `majeure.mineure` (ex. `1.0`) | `CONFIG_INVALID` si absente ou mal formée |
| | Majeure différente de `1` | `CONFIG_SCHEMA_UNSUPPORTED` (signalé avant les autres contrôles) |
| (propriétés inconnues) | En `1.0` : refusées (faute de frappe, ex. `volumemode`). En `1.x` avec x > 0 : ignorées (ajouts facultatifs d'une mineure plus récente) | `CONFIG_INVALID` |
| `mainModelPath` | Chaîne non vide, ou null / absent (détection automatique) | `CONFIG_INVALID` |
| `phaseIds` | null / absent = toutes les phases. Sinon tableau **non vide** de chaînes d'entiers positifs (`"86961"`), sans doublon (comparaison numérique), dans les limites d'un Int64 | `CONFIG_INVALID` |
| `volumeMode` | `"preserve"` (défaut si absent ou null) ou `"compute"`, casse exacte. `"compute"` : calcul temporaire des volumes (EGR-16, voir `docs/LECTURE.md`) | `CONFIG_INVALID` |
| `sharedParameters` | null / absent = aucun. Sinon tableau d'objets `{ key, guid }`. `key` : chaîne non vide, sans espace en début ou fin, unique (comparaison sensible à la casse, comme les clés JSON de `rooms[].sharedParameters`). `guid` : format `xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx`, GUID nul refusé | `CONFIG_INVALID` |

| `roomParameters` | null / absent / `"none"` (défaut) ou `"all"` (tous les paramètres de chaque pièce dans `rooms[].parameters`, voir `docs/ELEMENTS.md`) | `CONFIG_INVALID` |
| `elements` | null / absent = aucun. Sinon tableau sans doublon de `"doors"`, `"equipment"`, `"terminals"` : portes, équipements et terminaux rattachés aux pièces | `CONFIG_INVALID` |
| `elementCategories` | null / absent = catégories par défaut. Sinon objet `{ famille : [OST_…] }` (familles `doors`, `equipment`, `terminals`), tableaux non vides sans doublon de noms de `BuiltInCategory` | `CONFIG_INVALID` |

| `roomImages` | null / absent / `false` (défaut) ou `true` : une vue 3D par pièce placée dans `Images/` de la sortie ZIP (voir `docs/IMAGES.md`) | `CONFIG_INVALID` |
| `imageWidthPx` | null / absent = 8000 (valeur de TRC). Sinon entier de 400 à 8000 (largeur des images en pixels) | `CONFIG_INVALID` |

| `roomHeights` | null / absent / `false` (défaut) ou `true` : hauteur renseignée et hauteur minimale mesurée par lancer de rayons, séparées, dans `rooms[].heights` (voir `docs/HEIGHTS.md`) | `CONFIG_INVALID` |
| `heightStepM` | null / absent = 0,5. Sinon nombre de 0,1 à 2 (pas de la grille de rayons, en mètres) | `CONFIG_INVALID` |

| `linkedElements` | null / absent / `true` (défaut) : les équipements et terminaux des modèles liés sont aussi collectés (`false` : modèle principal seulement) | `CONFIG_INVALID` |

| `includeSecondaryDesignOptions` | null / absent / `false` (défaut) : la sortie `generic` n'exporte que les pièces hors option de conception et celles de l'option principale ; les pièces des options secondaires sont exclues, avec une information chiffrée `DESIGN_OPTIONS_EXCLUDED` dans `warnings[]`. `true` : toutes les pièces sont exportées, `option_conception` indiquant l'option. Sans effet sur `contract`, `trc` et `both` (voir `docs/GENERIC.md`) | `CONFIG_INVALID` |
| `outputFormat` | `"contract"` (`result.json`), `"trc"`, `"trc"` (`TRCValidationDict.json`, données seules, voir `docs/TRC.md`), `"both"` (les deux) ou `"generic"` (`result.generic.json`, enveloppe commune de la plateforme, voir `docs/GENERIC.md`). **Absent ou null** : `result.json` (`contract`) pour la bibliothèque et l'outil local ; **`generic` dans l'AppBundle Design Automation** (la plateforme n'envoie que `schemaVersion` et `mainModelPath`). Une valeur explicite est toujours respectée | `CONFIG_INVALID` |

Les messages d'erreur nomment la propriété en cause (ex. `settings.json : sharedParameters[1].key « a » en double…`)
et ne contiennent aucun chemin absolu du WorkItem.

## Résolution du RVT principal

- **`mainModelPath` renseigné.**
  - Chemin relatif à `input/` ; `/` et `\` sont acceptés comme séparateurs.
  - Le chemin est normalisé : pas de `.` ni de `..`, séparateur `/` (ex. `Projet\A.rvt` devient `Projet/A.rvt`).
  - Un chemin absolu (`/…`, `C:\…`, `C:…`, UNC `\\serveur\…`) donne `PATH_OUTSIDE_INPUT`.
  - Un chemin qui sort de `input/` après normalisation (`../…`, `Projet/../../…`) donne `PATH_OUTSIDE_INPUT`.
  - Un chemin qui reste dans `input/` mais dont un composant est un lien symbolique pointant vers l'extérieur donne aussi `PATH_OUTSIDE_INPUT`.
  - Les caractères `* ? " < > |` et le caractère nul donnent `CONFIG_INVALID`.
  - Un fichier absent ou sans extension `.rvt` donne `RVT_NOT_FOUND`.
  - `mainModelPath` peut désigner explicitement n'importe quel RVT, y compris une copie de sauvegarde.
- **`mainModelPath` null ou absent : détection automatique.**
  - La recherche est récursive dans `input/`, extension `.rvt` sans tenir compte de la casse.
  - Les liens symboliques (fichiers et dossiers) ne sont pas suivis.
  - Sont exclus : les dossiers `__MACOSX/` et les fichiers `._*` (métadonnées d'archives créées sous macOS), ainsi que les copies de sauvegarde Revit `Nom.NNNN.rvt` (quatre chiffres) **seulement si `Nom.rvt` est présent dans le même dossier**. Un fichier isolé comme `Projet.2026.rvt` ou `Nom.0001.rvt` sans original reste candidat.
  - Avec 0 candidat : `RVT_NOT_FOUND` (de même si `input/` est absent).
  - Avec 1 candidat : il est retenu.
  - Avec 2 candidats ou plus : `RVT_AMBIGUOUS`. Le message cite au plus 10 candidats, en ordre stable, et demande de renseigner `mainModelPath`. **Aucun RVT n'est jamais choisi arbitrairement.**
- `MainModelLocation` (EGR-06) refait un contrôle défensif sur `ResolvedMainModelPath` avant l'ouverture.

## Contraintes du ZIP sur les machines APS

Le ZIP est décompressé par le service APS sur une machine Windows, avant le démarrage de l'extracteur. Deux règles
de préparation en découlent ; une erreur à ce stade n'est pas détectable par l'extracteur.

- **Chemins courts (limite Windows de 260 caractères).** La documentation APS demande de préférer des chemins courts
  pour ne pas dépasser la limite de 260 caractères des chemins Windows. Cette limite s'applique au chemin complet sur
  la machine du service : dossier de travail du WorkItem, puis `input/`, puis le chemin relatif dans le ZIP. Le dossier
  de travail n'est pas documenté et sa longueur n'est pas connue à l'avance. Il faut donc garder les chemins relatifs
  du ZIP nettement plus courts que 260 caractères : peu de niveaux de dossiers, noms de dossiers et de fichiers
  concis. Ne pas zipper l'arborescence complète du serveur de fichiers au-dessus du dossier commun du projet (D4,
  point 1).
- **Noms de fichiers non ASCII (accents, etc.).** La plupart des archiveurs n'enregistrent pas les noms en UTF-8 et
  n'indiquent pas de page de code. Le service a alors besoin d'une indication pour décompresser correctement les
  noms non ASCII. Il faut l'une des deux mesures suivantes :
  - forcer l'UTF-8 dans l'archiveur (par exemple 7-Zip avec l'option `cu`) ;
  - ou ajouter au ZIP un fichier vide `XYZ.codepage`, où `XYZ` est le nom ou le numéro de la page de code utilisée
    pour les noms (par exemple `CP866.codepage` ou `866.codepage` pour le cyrillique).

  Sans l'une de ces mesures, un nom accentué peut être mal décompressé : `mainModelPath` ne correspond alors plus au
  fichier (`RVT_NOT_FOUND`), ou un lien n'est pas retrouvé (`LINK_MISSING`). En cas de doute, préférer des noms ASCII.

Sources : APS, *Automation API > Developer's Guide > Restrictions* et *Automation API > Developer's Guide >
Troubleshooting* (« Non-English filenames in ZIP files »).

## D4 — Arborescence des liens dans le ZIP (tranchée, EGR-05)

1. **La racine du ZIP correspond au dossier commun au projet.** L'arborescence du serveur de fichiers est conservée sous cette racine : le RVT principal et ses liens Revit gardent leurs positions relatives.
2. **Les liens Revit sont enregistrés en chemin relatif.** Dans la maquette hôte, le type de chemin des liens doit être *Relatif* (Gérer les liens → Type de chemin), et chaque lien est placé dans le ZIP à ce chemin relatif par rapport au RVT hôte. Disposition recommandée : liens dans le dossier du RVT hôte ou dans un sous-dossier (`Projet/Liens/…`).
3. **`mainModelPath` est obligatoire dès que le ZIP contient des liens Revit.** Un lien est lui-même un `.rvt` : un ZIP avec liens contient donc plusieurs RVT, et sans `mainModelPath` la résolution aboutit à `RVT_AMBIGUOUS`.
4. **Un lien absent ou introuvable ne bloque pas.** Un lien en chemin absolu (`C:\…`, `\\serveur\…`) ou absent du ZIP n'est pas résolu dans le WorkItem. Il est signalé en `LINK_MISSING` (EGR-06, warning), ce qui donne le statut `succeededWithWarnings` sans échec global. Un lien présent mais déchargé est indiqué en `LINK_NOT_LOADED` (info, #26). Le MVP ne parcourt pas les pièces des liens ; un lien manquant n'affecte que le contexte du document.
5. **Les liens imbriqués et les autres types de liens sont facultatifs.** Les liens de liens ne sont pas nécessaires. Les liens DWG, IFC et les images sont ignorés par l'extracteur et ne produisent pas `LINK_MISSING`, qui ne concerne que les liens Revit.
6. **Aucun chemin ne sort de `input/`.** L'extracteur ne lit rien hors du répertoire d'entrée et ne suit pas les liens symboliques lors de la détection.

### À vérifier dans un vrai WorkItem (EGR-06, EGR-14)

- Comment RevitCoreConsole résout les liens en chemin relatif après décompression ; s'il se replie sur le dossier de l'hôte pour un lien en chemin absolu.
- Le comportement réel avec des noms accentués : ZIP en UTF-8, ZIP avec fichier `.codepage`, puis ZIP sans indication (voir « Contraintes du ZIP sur les machines APS »).
- La sensibilité à la casse : le moteur APS tourne sous Windows, qui ne distingue pas la casse, alors que les tests tournent aussi sous Linux.
