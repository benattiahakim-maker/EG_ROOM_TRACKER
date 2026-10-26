using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using EgRoomTracker.Application.Revit;
using EgRoomTracker.Application.Trc;
using EgRoomTracker.Contracts.Codes;
using EgRoomTracker.Contracts.Generic;
using EgRoomTracker.Contracts.Results;

namespace EgRoomTracker.Application.Generic;

/// <summary>Groupes d'affichage des propriétés (champ <c>group</c>).</summary>
public static class GenericGroups
{
    public const string Identification = "Identification";
    public const string Dimensions = "Dimensions";
    public const string Finishes = "Finitions";
    public const string Images = "Images";
    public const string Relations = "Relations";
    public const string Shared = "Paramètres partagés";
    public const string Occurrence = "Paramètres d'occurrence";
    public const string Type = "Paramètres de type";
    public const string Anomaly = "Anomalie";
    public const string Files = "Fichiers";
    public const string Level = "Niveau";
}

/// <summary>Clés des propriétés de l'enveloppe (la propriété réservée <c>viewerId</c> est dans <see cref="GenericContract"/>).</summary>
public static class GenericKeys
{
    public const string UniqueId = "unique_id";
    public const string RevitId = "id_revit";
    public const string Category = "Category";

    /// <summary>Libellé lisible de <see cref="Category"/> (la valeur technique reste inchangée).</summary>
    public const string CategoryLabel = "categorie_libelle";
    public const string ModelKey = "modelKey";
    public const string Room = "room";
    public const string Contains = "contains";
    public const string RoomFrom = "room_from";
    public const string RoomTo = "room_to";
    public const string Box = "box";
    public const string Image = "image";

    /// <summary>Préfixe des paramètres d'occurrence (<c>occurrence:Nom</c>).</summary>
    public const string OccurrencePrefix = "occurrence:";

    /// <summary>Préfixe des paramètres de type (<c>type:Nom</c>).</summary>
    public const string TypePrefix = "type:";

    /// <summary>Préfixe des paramètres partagés de la pièce (<c>partage:cléDuMapping</c>).</summary>
    public const string SharedPrefix = "partage:";
}

/// <summary>Valeurs de la propriété <c>Category</c> propres à la sortie <c>generic</c> (les autres sont celles du TRC : <c>Space</c>, <c>Door</c>…).</summary>
public static class GenericCategories
{
    public const string Anomaly = "Anomaly";
    public const string Files = "Files";
    public const string Level = "Level";

    /// <summary>Objet de regroupement (« Portes », « Équipements », « Terminaux » d'une pièce) : un nom et des enfants, rien d'autre.</summary>
    public const string Group = "Group";
}

/// <summary>Contexte de conversion : ce que l'extraction ne sait pas, parce que cela dépend des fichiers écrits.</summary>
public sealed record GenericMapContext
{
    /// <summary>Noms des fichiers réellement présents dans <c>Images/</c> ; null ou vide : aucune propriété <c>image</c>.</summary>
    public IReadOnlySet<string>? ImageFileNames { get; init; }

    /// <summary>
    /// <c>false</c> (défaut) : les pièces d'une option de conception secondaire sont exclues (avec une information chiffrée dans
    /// <c>warnings</c>) ; <c>true</c> : toutes les pièces sont exportées, <c>option_conception</c> indiquant l'option.
    /// </summary>
    public bool IncludeSecondaryDesignOptions { get; init; }

    /// <summary>Identifiant du service dans l'enveloppe (<c>serviceId</c>).</summary>
    public string ServiceId { get; init; } = GenericContract.ServiceId;

    /// <summary>Dossier des images dans le ZIP de sortie, sans barre finale (<c>Images</c>).</summary>
    public string ImagesDirectory { get; init; } = "Images";

    /// <summary>Fichiers produits à la racine du résultat (ex. <c>TRCValidationDict.json</c>), listés dans un objet « Fichiers du résultat ».</summary>
    public IReadOnlyList<GenericFile> ResultFiles { get; init; } = [];
}

/// <summary>
/// Conversion pure de l'extraction en enveloppe commune de la plateforme (<c>result.generic.json</c>), sans Revit ni fichier.
/// <list type="bullet">
/// <item>Racine : un objet « Niveau » par niveau (altitude croissante), puis « Sans niveau » ; ses enfants : les pièces (<c>id</c>
/// <c>room-{ElementId}</c>) en tri naturel du numéro. Enfants d'une pièce : groupes « Portes », « Équipements », « Terminaux » (non vides),
/// puis ses anomalies de portée pièce.</item>
/// <item>Une porte, un équipement ou un terminal = un objet (<c>id</c> <c>element-{UniqueId}</c>) <b>répété, identique,</b> sous chaque
/// pièce qui le contient (objet partagé accepté par la plateforme) ; il pointe toutes ses pièces par des <c>ref</c> <c>room</c> (portes :
/// aussi <c>room_from</c> et <c>room_to</c>), et chaque pièce pointe ses éléments par des <c>ref</c> <c>contains</c>.</item>
/// <item>Aucun champ de type de contenu : <c>Category</c> (<c>Space</c>, <c>Door</c>, <c>cvc equipement</c>…) est une propriété normale.</item>
/// <item>Valeurs typées : nombres avec unité (m, m², m³), jamais de texte arrondi ; une valeur inconnue est omise, jamais mise à zéro.</item>
/// </list>
/// </summary>
public static class GenericMapper
{
    public const string ImageMimeType = "image/jpeg";

    public static GenericEnvelope Map(ExtractionResult result, GenericMapContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        context ??= new GenericMapContext();
        var source = result.Summary.Source ?? throw new InvalidOperationException("Modèle source inconnu : sortie generic impossible.");
        if (result.Summary.Status == ExtractionStatus.Failed)
        {
            throw new InvalidOperationException("Extraction en échec : pas d'enveloppe (result.json explique l'échec).");
        }

        var excludedRooms = 0;
        if (!context.IncludeSecondaryDesignOptions)
        {
            var secondary = result.Rooms.Where(IsSecondaryDesignOption).Select(r => r.ElementId).ToHashSet(StringComparer.Ordinal);
            if (secondary.Count > 0)
            {
                excludedRooms = secondary.Count;
                result = result with
                {
                    Rooms = [.. result.Rooms.Where(r => !secondary.Contains(r.ElementId))],
                    Anomalies = [.. result.Anomalies.Where(a => !(a.Scope == AnomalyScope.Room && a.ElementId is { } id && secondary.Contains(id)))],
                };
            }
        }

        var codeFile = TrcNaming.CodeFile(source.FileName);
        var warnings = new List<GenericWarning>();
        var used = new HashSet<string>(StringComparer.Ordinal);

        var elements = Elements(result.Rooms);
        var models = BuildModels(source, elements);
        var linkKeys = models.Where(m => m.Key != GenericContract.MainModelKey).ToDictionary(m => m.Title, m => m.Key, StringComparer.Ordinal);

        // Identifiants : stables et uniques (UniqueId d'élément, ElementId de pièce propre au modèle principal).
        var roomIds = new Dictionary<string, string>(StringComparer.Ordinal); // UniqueId de pièce -> id d'objet
        var roomIdByElementId = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var room in result.Rooms)
        {
            var id = Unique($"room-{room.ElementId}", used);
            roomIds[room.UniqueId] = id;
            roomIdByElementId.TryAdd(room.ElementId, id);
        }

        var elementIds = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var element in elements)
        {
            elementIds[element.Item.UniqueId] = Unique($"element-{element.Item.UniqueId}", used);
        }

        // Objet partagé : un élément contenu dans plusieurs pièces est répété, identique (même id, même contenu, mêmes refs), sous
        // chacune ; le validateur de la plateforme l'accepte et ne parcourt son sous-arbre qu'une fois.
        var childrenOf = new Dictionary<string, List<(GenericElement Element, GenericObject Object)>>(StringComparer.Ordinal);
        foreach (var element in elements)
        {
            var shared = ElementObject(element, elementIds[element.Item.UniqueId], roomIds, linkKeys);
            foreach (var space in element.Spaces)
            {
                Add(childrenOf, space, (element, shared));
            }
        }

        // Anomalies : portée pièce en enfants de la pièce, portée document en objets racines.
        var roomAnomalies = new Dictionary<string, List<GenericObject>>(StringComparer.Ordinal);
        var documentAnomalies = new List<GenericObject>();
        var anomalyNumber = 0;
        foreach (var anomaly in result.Anomalies)
        {
            if (anomaly.Code is DocumentAnomalyCodes.LinkMissing or DocumentAnomalyCodes.VolumeNotComputed)
            {
                continue; // Dits seulement par une information unique dans warnings[] (MissingLinksNote, VolumeNotComputedNote).
            }

            var obj = AnomalyObject(anomaly, Unique($"anomaly-{(++anomalyNumber).ToString(CultureInfo.InvariantCulture)}", used));
            if (anomaly.Scope == AnomalyScope.Room && anomaly.ElementId is { } roomElementId && roomIdByElementId.ContainsKey(roomElementId))
            {
                Add(roomAnomalies, roomElementId, obj);
            }
            else
            {
                documentAnomalies.Add(obj);
            }
        }

        var withoutBox = 0;
        var roomObjects = new List<(RoomDto Room, GenericObject Object)>(result.Rooms.Count);
        foreach (var room in result.Rooms)
        {
            var properties = RoomProperties(room, codeFile, context, ref withoutBox);
            var groups = ElementGroups(childrenOf.GetValueOrDefault(room.UniqueId) ?? [], roomIds[room.UniqueId], used).ToList();
            foreach (var contained in groups.SelectMany(g => g.Children))
            {
                properties.Add(Ref(GenericKeys.Contains, "Contient", contained.Id, GenericGroups.Relations));
            }

            var children = new List<GenericObject>();
            children.AddRange(groups);
            children.AddRange(roomAnomalies.GetValueOrDefault(room.ElementId) ?? []);
            roomObjects.Add((room, new GenericObject { Id = roomIds[room.UniqueId], Name = RoomName(room), Children = children, Properties = properties }));
        }

        var objects = new List<GenericObject>(roomObjects.Count + documentAnomalies.Count + 1);
        objects.AddRange(LevelObjects(roomObjects, used));
        objects.AddRange(documentAnomalies);
        if (context.ResultFiles.Count > 0)
        {
            objects.Add(FilesObject(context.ResultFiles, Unique("result-files", used)));
        }

        if (withoutBox > 0)
        {
            warnings.Add(GenericWarning.Warning($"{withoutBox.ToString(CultureInfo.InvariantCulture)} pièce(s) sans boîte englobante (pièce non placée ou boîte illisible) : propriété box omise."));
        }

        warnings.AddRange(AnomalyWarnings(result.Anomalies).Select(GenericWarning.Warning));
        var linkNote = MissingLinksNote(result.Anomalies);
        var volumeNote = VolumeNotComputedNote(result);
        var manyRoomsNote = SharedElementsNote(elements);
        if (warnings.Count == 0 && linkNote is null && result.Summary.Status == ExtractionStatus.SucceededWithWarnings)
        {
            warnings.Add(GenericWarning.Warning("Extraction terminée avec avertissements ; voir les anomalies."));
        }

        // Informations (gravité info) : après les avertissements ; elles ne rendent jamais l'enveloppe « partial » (statut calculé avant).
        var status = warnings.Count > 0 ? GenericStatuses.Partial : GenericStatuses.Done;
        var infos = new List<GenericWarning>();
        if (linkNote is not null)
        {
            infos.Add(linkNote);
        }

        if (volumeNote is not null)
        {
            infos.Add(volumeNote);
        }

        if (manyRoomsNote is not null)
        {
            infos.Add(manyRoomsNote);
        }

        if (excludedRooms > 0)
        {
            infos.Add(GenericWarning.Info(
                "DESIGN_OPTIONS_EXCLUDED",
                $"{excludedRooms.ToString(CultureInfo.InvariantCulture)} pièce(s) d'options de conception secondaires exclues (réglage includeSecondaryDesignOptions : false)."));
        }

        warnings = CapWarnings(warnings, infos);

        return new GenericEnvelope
        {
            ServiceId = context.ServiceId,
            Status = status,
            Warnings = warnings,
            Models = models,
            Objects = objects,
        };
    }

    // ---------------------------------------------------------------- éléments (collecte)

    /// <summary>
    /// Porte, équipement ou terminal, une seule fois (clé : UniqueId), avec ses pièces dans l'ordre des pièces du résultat et la
    /// relation (<c>from</c>, <c>to</c>, <c>in</c>) de chacune. <see cref="Spaces"/> : UniqueId des pièces ; l'objet est répété sous chacune.
    /// </summary>
    private sealed record GenericElement(RoomElementDto Item, string Category, ElementKind Kind, List<(string Space, string Relation)> Rooms)
    {
        public IEnumerable<string> Spaces => Rooms.Select(r => r.Space);
    }

    /// <summary>Famille d'un élément : décide du groupe (« Portes », « Équipements », « Terminaux ») qui le range dans la pièce.</summary>
    private enum ElementKind
    {
        Door,
        Equipment,
        Terminal,
    }

    /// <summary>
    /// Groupes d'une pièce, dans l'ordre Portes, Équipements, Terminaux ; un groupe vide est omis. Enfants triés de façon stable : nom
    /// (sans tenir compte de la casse), puis UniqueId.
    /// </summary>
    private static IEnumerable<GenericObject> ElementGroups(List<(GenericElement Element, GenericObject Object)> items, string roomId, HashSet<string> used)
    {
        foreach (var (kind, label, suffix) in new[] { (ElementKind.Door, "Portes", "portes"), (ElementKind.Equipment, "Équipements", "equipements"), (ElementKind.Terminal, "Terminaux", "terminaux") })
        {
            var members = items.Where(i => i.Element.Kind == kind)
                .OrderBy(i => i.Object.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(i => i.Element.Item.UniqueId, StringComparer.Ordinal)
                .Select(i => i.Object)
                .ToList();
            if (members.Count == 0)
            {
                continue;
            }

            yield return new GenericObject
            {
                Id = Unique($"{roomId}-{suffix}", used),
                Name = label,
                Children = members,
                Properties =
                [
                    Text(GenericKeys.Category, "Catégorie", GenericCategories.Group, GenericGroups.Identification),
                    Text(GenericKeys.CategoryLabel, "Catégorie (libellé)", label, GenericGroups.Identification),
                ],
            };
        }
    }

    private static IReadOnlyList<GenericElement> Elements(IEnumerable<RoomDto> rooms)
    {
        var order = new List<GenericElement>();
        var byId = new Dictionary<string, GenericElement>(StringComparer.Ordinal);
        foreach (var room in rooms)
        {
            var items = (room.Doors ?? []).Select(item => (item, Category: TrcCategories.Door, Kind: ElementKind.Door))
                .Concat((room.Equipment ?? []).Select(item => (item, Category: TrcCategories.Equipment(item.Discipline), Kind: ElementKind.Equipment)))
                .Concat((room.Terminals ?? []).Select(item => (item, Category: TrcCategories.Terminal(item.Discipline), Kind: ElementKind.Terminal)));
            foreach (var (item, category, kind) in items)
            {
                if (!byId.TryGetValue(item.UniqueId, out var entry))
                {
                    byId[item.UniqueId] = entry = new GenericElement(item, category, kind, []);
                    order.Add(entry);
                }

                if (!entry.Rooms.Any(r => r.Space == room.UniqueId))
                {
                    entry.Rooms.Add((room.UniqueId, item.Relation));
                }
            }
        }

        return order;
    }

    // ---------------------------------------------------------------- modèles

    /// <summary>
    /// <c>models[]</c> : la maquette principale (<c>main</c>), puis un <c>link-n</c> par modèle lié d'où vient au moins un élément, triés
    /// par nom. L'URN est celui de la conversion, que seule la plateforme connaît : il reste vide.
    /// </summary>
    private static List<GenericModel> BuildModels(SourceInfo source, IReadOnlyList<GenericElement> elements)
    {
        var models = new List<GenericModel>
        {
            new(GenericContract.MainModelKey, Path.GetFileNameWithoutExtension(source.FileName), string.Empty),
        };
        var number = 0;
        foreach (var link in elements.Select(e => e.Item.LinkName).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            models.Add(new($"link-{(++number).ToString(CultureInfo.InvariantCulture)}", link!, string.Empty));
        }

        return models;
    }

    // ---------------------------------------------------------------- niveaux

    /// <summary>
    /// Racine de l'arbre : un objet « Niveau » par niveau qui a au moins une pièce exportée, par altitude croissante (altitude inconnue après les
    /// autres, par nom), puis « Sans niveau » en dernier. Ses enfants sont ses pièces, en tri naturel du numéro (départage : ElementId).
    /// </summary>
    private static List<GenericObject> LevelObjects(IReadOnlyList<(RoomDto Room, GenericObject Object)> rooms, HashSet<string> used)
    {
        var groups = rooms
            .GroupBy(r => r.Room.Level is { } level ? (string.IsNullOrEmpty(level.UniqueId) ? "name:" + level.Name : level.UniqueId) : null, StringComparer.Ordinal)
            .Select(g => (Level: g.First().Room.Level, Rooms: g.OrderBy(r => r.Room.Number, NaturalOrder.Instance)
                .ThenBy(r => r.Room.ElementId, Comparer<string>.Create(NaturalOrder.CompareElementIds)).Select(r => r.Object).ToList()))
            .ToList();
        // Ordre des niveaux : altitude croissante ; à altitude égale, nom (tri naturel) puis ElementId. Un niveau sans altitude lisible
        // (null ou non finie) vient après tous ceux qui en ont une, mais avant « Sans niveau » (ajouté en dernier plus bas).
        var levels = groups
            .Where(g => g.Level is not null)
            .OrderBy(g => g.Level!.ElevationM is { } e && double.IsFinite(e) ? 0 : 1)
            .ThenBy(g => g.Level!.ElevationM is { } e && double.IsFinite(e) ? e : 0)
            .ThenBy(g => g.Level!.Name, NaturalOrder.Instance)
            .ThenBy(g => g.Level!.ElementId, Comparer<string>.Create(NaturalOrder.CompareElementIds))
            .Select(g => LevelObject(g.Level, g.Rooms, used))
            .ToList();
        var without = groups.FirstOrDefault(g => g.Level is null);
        if (without.Rooms is not null)
        {
            levels.Add(LevelObject(null, without.Rooms, used));
        }

        return levels;
    }

    private static GenericObject LevelObject(LevelRef? level, List<GenericObject> rooms, HashSet<string> used)
    {
        var name = level is null ? "Sans niveau" : string.IsNullOrWhiteSpace(level.Name) ? $"Niveau {level.ElementId}" : level.Name;
        var p = new List<GenericProperty>
        {
            Text(GenericKeys.Category, "Catégorie", GenericCategories.Level, GenericGroups.Identification),
            Text(GenericKeys.CategoryLabel, "Catégorie (libellé)", CategoryLabel(GenericCategories.Level), GenericGroups.Identification),
        };
        if (level is not null)
        {
            AddText(p, GenericKeys.UniqueId, "UniqueId Revit", level.UniqueId, GenericGroups.Identification);
            AddText(p, GenericKeys.RevitId, "ElementId Revit", level.ElementId, GenericGroups.Identification);
            AddNumber(p, "altitude", "Altitude", level.ElevationM, "m", GenericGroups.Dimensions);
        }

        var id = Unique(level is null ? "level-none" : $"level-{(string.IsNullOrEmpty(level.UniqueId) ? level.ElementId : level.UniqueId)}", used);
        return new GenericObject { Id = id, Name = name, Children = rooms, Properties = p };
    }

    // ---------------------------------------------------------------- pièces

    private static string RoomName(RoomDto room)
    {
        var name = string.Join(' ', new[] { room.Number, room.Name }.Where(part => !string.IsNullOrWhiteSpace(part)));
        return name.Length > 0 ? name : $"Pièce {room.ElementId}";
    }

    private static List<GenericProperty> RoomProperties(RoomDto room, string codeFile, GenericMapContext context, ref int withoutBox)
    {
        var p = new List<GenericProperty>
        {
            Text(GenericContract.ViewerIdKey, "Identifiant du viewer", room.UniqueId, GenericGroups.Identification),
            Text(GenericKeys.UniqueId, "UniqueId Revit", room.UniqueId, GenericGroups.Identification),
            Text(GenericKeys.RevitId, "ElementId Revit", room.ElementId, GenericGroups.Identification),
            Text(GenericKeys.Category, "Catégorie", TrcCategories.Space, GenericGroups.Identification),
            Text(GenericKeys.CategoryLabel, "Catégorie (libellé)", CategoryLabel(TrcCategories.Space), GenericGroups.Identification),
            Text(GenericKeys.ModelKey, "Maquette", GenericContract.MainModelKey, GenericGroups.Identification),
        };
        AddText(p, "numero", "Numéro", room.Number, GenericGroups.Identification);
        AddText(p, "niveau", "Niveau", room.Level?.Name, GenericGroups.Identification);
        AddText(p, "phase", "Phase", room.Phase?.Name, GenericGroups.Identification);
        AddText(p, "variante", "Variante", room.DesignOption is null ? null : CleanOptionName(room.DesignOption.Name), GenericGroups.Identification);
        AddText(p, "option_conception", "Option de conception", DesignOptionLabel(room), GenericGroups.Identification);
        AddText(p, "occupation", "Occupation", room.Occupancy, GenericGroups.Identification);
        if (room.NamedParameters is { } named)
        {
            AddText(p, "ouvrage", "Ouvrage", Named(named, TrcIdentification.Ouvrage), GenericGroups.Identification);
            AddText(p, "typologie_espace", "Typologie d'espace", Named(named, TrcIdentification.Typologie), GenericGroups.Identification);
            AddText(p, "local_code", "Code du local", Named(named, TrcIdentification.LocalCode), GenericGroups.Identification);
        }

        AddNumber(p, "surface", "Surface", room.AreaM2, "m²", GenericGroups.Dimensions);
        if (room.VolumeStatus == VolumeStatus.Computed)
        {
            AddNumber(p, "volume", "Volume", room.VolumeM3, "m³", GenericGroups.Dimensions);
        }

        if (room.Heights is { } heights)
        {
            AddNumber(p, "hauteur", "Hauteur effective", heights.EffectiveM, "m", GenericGroups.Dimensions);
            AddNumber(p, "hauteur_mesuree", "Hauteur mesurée", heights.MeasuredMinM, "m", GenericGroups.Dimensions);
            AddNumber(p, "hauteur_renseignee", "Hauteur renseignée", heights.DeclaredM, "m", GenericGroups.Dimensions);
            AddText(p, "hauteur_source", "Source de la hauteur effective", heights.EffectiveSource, GenericGroups.Dimensions);
            AddText(p, "hauteur_element_limitant", "Élément limitant la hauteur", heights.LimitingCategory is null ? null : $"{heights.LimitingCategory} ({heights.LimitingDocument})", GenericGroups.Dimensions);
        }

        AddNumber(p, "hauteur_non_limitee", "Hauteur non limitée", room.UnboundedHeightM, "m", GenericGroups.Dimensions);

        if (room.BoundingBox is { } box && Box(box) is { } value)
        {
            p.Add(new GenericProperty { Key = GenericKeys.Box, Label = "Boîte englobante (mm)", Type = GenericPropertyTypes.Box, Value = value, Group = GenericGroups.Dimensions });
        }
        else
        {
            withoutBox++;
        }

        AddText(p, "finition_sol", "Finition du sol", room.Finishes.Floor, GenericGroups.Finishes);
        AddText(p, "finition_murs", "Finition des murs", room.Finishes.Wall, GenericGroups.Finishes);
        AddText(p, "finition_plafond", "Finition du plafond", room.Finishes.Ceiling, GenericGroups.Finishes);
        AddText(p, "finition_plinthe", "Finition de la plinthe", room.Finishes.Base, GenericGroups.Finishes);

        foreach (var (key, shared) in room.SharedParameters.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            AddValue(p, GenericKeys.SharedPrefix + key, key, shared.Value, null, GenericGroups.Shared);
        }

        if (context.ImageFileNames is { Count: > 0 } images && long.TryParse(room.ElementId, NumberStyles.None, CultureInfo.InvariantCulture, out var elementId))
        {
            var fileName = TrcNaming.RoomImageFileName(room.UniqueId, codeFile, elementId);
            if (images.Contains(fileName))
            {
                p.Add(File(GenericKeys.Image, "Vue 3D de la pièce", new GenericFile(fileName, ImageMimeType, RelativeUrl(context.ImagesDirectory, fileName)), GenericGroups.Images));
            }
        }

        AddParameters(p, room.Parameters, GenericKeys.OccurrencePrefix, GenericGroups.Occurrence);
        return p;
    }

    /// <summary>Pièce d'une option de conception secondaire : option connue et non principale (option inconnue : conservée, par prudence).</summary>
    internal static bool IsSecondaryDesignOption(RoomDto room) => room.DesignOption is not null && room.DesignOptionIsPrimary == false;

    private static readonly Regex PrimaryMarker = new(@"<\s*(principale?|primary)\s*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex Spaces = new(@"\s+", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>
    /// Nom d'option lisible : Revit ajoute un repère à l'option principale (« Unoccupied  &lt;principal&gt; ») : le repère est retiré, les
    /// chevrons restants aussi, les espaces sont normalisés ; un nom vide donne « Principal ».
    /// </summary>
    internal static string CleanOptionName(string? name)
    {
        var text = PrimaryMarker.Replace(name ?? string.Empty, " ").Replace("<", " ", StringComparison.Ordinal).Replace(">", " ", StringComparison.Ordinal);
        text = Spaces.Replace(text, " ").Trim();
        return text.Length == 0 ? "Principal" : text;
    }

    /// <summary>« Principal » hors option ; sinon le nom nettoyé de l'option, suivi de « (principale) » pour l'option principale de son jeu.</summary>
    private static string DesignOptionLabel(RoomDto room)
    {
        if (room.DesignOption is null)
        {
            return "Principal";
        }

        var name = CleanOptionName(room.DesignOption.Name);
        return room.DesignOptionIsPrimary == true && name != "Principal" ? $"{name} (principale)" : name;
    }

    private static string? Named(IReadOnlyDictionary<string, string?> named, string name) =>
        named.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    // ---------------------------------------------------------------- éléments

    private static GenericObject ElementObject(GenericElement element, string id, IReadOnlyDictionary<string, string> roomIds, IReadOnlyDictionary<string, string> linkKeys)
    {
        var item = element.Item;
        var linked = !string.IsNullOrWhiteSpace(item.LinkName);
        var p = new List<GenericProperty>
        {
            Text(GenericContract.ViewerIdKey, "Identifiant du viewer", item.UniqueId, GenericGroups.Identification),
            Text(GenericKeys.UniqueId, "UniqueId Revit", item.UniqueId, GenericGroups.Identification),
            Text(GenericKeys.RevitId, "ElementId Revit", item.ElementId, GenericGroups.Identification),
            Text(GenericKeys.Category, "Catégorie", element.Category, GenericGroups.Identification),
            Text(GenericKeys.CategoryLabel, "Catégorie (libellé)", CategoryLabel(element.Category), GenericGroups.Identification),
            Text(GenericKeys.ModelKey, "Maquette", linked ? linkKeys[item.LinkName!] : GenericContract.MainModelKey, GenericGroups.Identification),
            new GenericProperty { Key = "modele_lie", Label = "Élément d'un modèle lié", Type = GenericPropertyTypes.Boolean, Value = linked, Group = GenericGroups.Identification },
        };
        AddText(p, "lien", "Modèle lié", item.LinkName, GenericGroups.Identification);
        AddText(p, "categorie_revit", "Catégorie Revit", item.Category, GenericGroups.Identification);
        AddText(p, "famille", "Famille", item.Family, GenericGroups.Identification);
        AddText(p, "type", "Type", item.Type, GenericGroups.Identification);
        AddText(p, "repere", "Repère", item.Mark, GenericGroups.Identification);
        AddText(p, "numero", "Numéro", item.Number, GenericGroups.Identification);
        AddText(p, "niveau", "Niveau", item.Level, GenericGroups.Identification);
        AddText(p, "discipline", "Discipline", item.Discipline, GenericGroups.Identification);
        AddNumber(p, "largeur", "Largeur de passage libre", item.WidthM, "m", GenericGroups.Dimensions);
        AddNumber(p, "hauteur", "Hauteur de passage libre", item.HeightM, "m", GenericGroups.Dimensions);
        AddText(p, "source_dimensions", "Source des dimensions", item.DimensionSource, GenericGroups.Dimensions);

        // Pièces : « room » pour chacune ; une porte précise aussi son côté (relation from / to de result.json). Ordre explicite et stable,
        // indépendant de l'ordre des pièces dans l'entrée : id de pièce en tri naturel (room-2 avant room-10). Les ids de pièces sont uniques
        // (Unique), donc deux pièces n'ont jamais le même id : aucun départage supplémentaire n'est possible.
        var orderedRooms = element.Rooms
            .OrderBy(r => roomIds[r.Space], NaturalOrder.Instance)
            .ToList();
        foreach (var (space, _) in orderedRooms)
        {
            p.Add(Ref(GenericKeys.Room, "Pièce", roomIds[space], GenericGroups.Relations));
        }

        if (element.Category == TrcCategories.Door)
        {
            foreach (var (space, relation) in orderedRooms)
            {
                if (RelationKey(relation) is { } key)
                {
                    p.Add(Ref(key, RelationLabel(relation), roomIds[space], GenericGroups.Relations));
                }
            }
        }

        AddParameters(p, item.Parameters, GenericKeys.OccurrencePrefix, GenericGroups.Occurrence);
        AddParameters(p, item.TypeParameters, GenericKeys.TypePrefix, GenericGroups.Type);
        return new GenericObject { Id = id, Name = ElementName(item), Properties = p };
    }

    private static string? RelationKey(string relation) => relation switch
    {
        ElementRoomRelation.From => GenericKeys.RoomFrom,
        ElementRoomRelation.To => GenericKeys.RoomTo,
        _ => null,
    };

    private static string RelationLabel(string relation) => relation == ElementRoomRelation.From ? "Pièce de départ" : "Pièce d'arrivée";

    private static string ElementName(RoomElementDto item)
    {
        var name = string.Join(" : ", new[] { item.Family, item.Type }.Where(part => !string.IsNullOrWhiteSpace(part)));
        return name.Length > 0 ? name : $"{item.Category} {item.ElementId}";
    }

    // ---------------------------------------------------------------- anomalies

    private static GenericObject AnomalyObject(AnomalyDto anomaly, string id)
    {
        var p = new List<GenericProperty>
        {
            Text(GenericKeys.Category, "Catégorie", GenericCategories.Anomaly, GenericGroups.Anomaly),
            Text(GenericKeys.CategoryLabel, "Catégorie (libellé)", CategoryLabel(GenericCategories.Anomaly), GenericGroups.Anomaly),
            Text("code", "Code", anomaly.Code, GenericGroups.Anomaly),
            Text("severity", "Gravité", anomaly.Severity.ToString().ToLowerInvariant(), GenericGroups.Anomaly),
            Text("scope", "Portée", anomaly.Scope.ToString().ToLowerInvariant(), GenericGroups.Anomaly),
            Text("message", "Message", anomaly.Message, GenericGroups.Anomaly),
        };
        if (anomaly.Details.Count > 0)
        {
            p.Add(Text("details", "Détails (JSON)", JsonSerializer.Serialize(anomaly.Details, DetailsOptions), GenericGroups.Anomaly));
        }

        return new GenericObject { Id = id, Name = anomaly.Code, Properties = p };
    }

    private static readonly JsonSerializerOptions DetailsOptions = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Un avertissement par code d'anomalie de gravité warning ou error, sauf <c>LINK_MISSING</c> (voir <see cref="MissingLinksNote"/>).</summary>
    private static IEnumerable<string> AnomalyWarnings(IReadOnlyList<AnomalyDto> anomalies) =>
        anomalies
            .Where(a => a.Severity != AnomalySeverity.Info && a.Code != DocumentAnomalyCodes.LinkMissing)
            .GroupBy(a => a.Code, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => g.Count() == 1
                ? $"{g.Key} : {g.First().Message}"
                : $"{g.Key} : {g.Count().ToString(CultureInfo.InvariantCulture)} anomalies (ex. {g.First().Message})");

    /// <summary>
    /// Information unique <c>VOLUME_NOT_COMPUTED</c> (document sans calcul des volumes) : nombre de pièces sans volume (celui de
    /// l'anomalie, lu par <see cref="DocumentAnomalies.VolumeNotComputedRooms"/>) ; null si l'anomalie est absente ou si aucune pièce n'est concernée. Aucun objet anomalie : seulement cette information.
    /// </summary>
    private static GenericWarning? VolumeNotComputedNote(ExtractionResult result)
    {
        var rooms = DocumentAnomalies.VolumeNotComputedRooms(result.Anomalies) ?? 0; // même nombre que le journal (un seul calcul)
        return rooms == 0 ? null : GenericWarning.Info(DocumentAnomalyCodes.VolumeNotComputed, DocumentAnomalies.VolumeNotComputedMessage(rooms));
    }

    /// <summary>
    /// Avertissements puis informations, au plus <see cref="GenericWarning.MaxItems"/> éléments (limite de la plateforme) : les
    /// informations sont gardées, les avertissements en trop sont remplacés par un dernier avertissement qui les compte.
    /// </summary>
    private static List<GenericWarning> CapWarnings(List<GenericWarning> warnings, List<GenericWarning> infos)
    {
        var room = Math.Max(GenericWarning.MaxItems - infos.Count, 0);
        var kept = warnings.Count <= room ? warnings : [.. warnings.Take(Math.Max(room - 1, 0))];
        if (warnings.Count > room && room > 0)
        {
            kept.Add(GenericWarning.Warning($"{(warnings.Count - kept.Count).ToString(CultureInfo.InvariantCulture)} autre(s) avertissement(s) non détaillé(s) ; voir result.json."));
        }

        return [.. kept, .. infos];
    }

    /// <summary>
    /// Garde-fou de taille : un élément est répété sous chacune de ses pièces, donc un élément partagé par très nombreuses pièces alourdit le
    /// fichier. Au-delà de <see cref="SharedElementRoomThreshold"/> pièces pour un élément, UNE seule information (code, nombre d'éléments
    /// concernés, seuil) ; les répétitions ne sont jamais limitées. Null s'il n'y en a aucun.
    /// </summary>
    private static GenericWarning? SharedElementsNote(IReadOnlyList<GenericElement> elements)
    {
        var many = elements.Count(e => e.Rooms.Count > SharedElementRoomThreshold);
        return many == 0
            ? null
            : GenericWarning.Info(
                SharedElementManyRoomsCode,
                $"{SharedElementManyRoomsCode} : {many.ToString(CultureInfo.InvariantCulture)} élément(s) répété(s) sous plus de {SharedElementRoomThreshold.ToString(CultureInfo.InvariantCulture)} pièces (seuil : {SharedElementRoomThreshold.ToString(CultureInfo.InvariantCulture)} pièces par élément) ; chaque pièce garde son exemplaire, le fichier est plus volumineux.");
    }

    /// <summary>Nombre de pièces par élément au-delà duquel l'information <see cref="SharedElementManyRoomsCode"/> est écrite.</summary>
    internal const int SharedElementRoomThreshold = 20;

    /// <summary>Code de l'information de garde-fou sur les éléments répétés sous beaucoup de pièces.</summary>
    internal const string SharedElementManyRoomsCode = "SHARED_ELEMENT_MANY_ROOMS";

    /// <summary>Information unique <c>LINK_MISSING</c> qui nomme tous les liens Revit absents (sans doublon, dans l'ordre) ; null s'il n'y en a pas.</summary>
    private static GenericWarning? MissingLinksNote(IReadOnlyList<AnomalyDto> anomalies)
    {
        var names = anomalies
            .Where(a => a.Code == DocumentAnomalyCodes.LinkMissing)
            .Select(a => a.Details.TryGetValue("linkName", out var name) ? Convert.ToString(name, CultureInfo.InvariantCulture) : null)
            .Select(name => string.IsNullOrWhiteSpace(name) ? "(nom inconnu)" : name.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return names.Count == 0 ? null : GenericWarning.Info(DocumentAnomalyCodes.LinkMissing, LinkMessage(names));
    }

    /// <summary>
    /// <c>N lien(s) absent(s) : a, b, c</c>, limité à <see cref="GenericWarning.MaxMessageLength"/> caractères : la liste est coupée
    /// proprement (noms entiers) et finit par « … et K autre(s) ».
    /// </summary>
    internal static string LinkMessage(IReadOnlyList<string> names)
    {
        var count = names.Count.ToString(CultureInfo.InvariantCulture);
        var head = $"{count} lien(s) absent(s) : ";
        var full = head + string.Join(", ", names);
        if (full.Length <= GenericWarning.MaxMessageLength)
        {
            return full;
        }

        for (var kept = names.Count - 1; kept >= 0; kept--)
        {
            var rest = names.Count - kept;
            var tail = $"… et {rest.ToString(CultureInfo.InvariantCulture)} autre(s)";
            var text = head + (kept == 0 ? string.Empty : string.Join(", ", names.Take(kept)) + ", ") + tail;
            if (text.Length <= GenericWarning.MaxMessageLength)
            {
                return text;
            }
        }

        return (head + "…")[..Math.Min(head.Length + 1, GenericWarning.MaxMessageLength)];
    }

    private static GenericObject FilesObject(IReadOnlyList<GenericFile> files, string id)
    {
        var p = new List<GenericProperty>
        {
            Text(GenericKeys.Category, "Catégorie", GenericCategories.Files, GenericGroups.Files),
            Text(GenericKeys.CategoryLabel, "Catégorie (libellé)", CategoryLabel(GenericCategories.Files), GenericGroups.Files),
        };
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            p.Add(File(Unique($"file:{file.Name}", keys), file.Name, file, GenericGroups.Files));
        }

        return new GenericObject { Id = id, Name = "Fichiers du résultat", Properties = p };
    }

    // ---------------------------------------------------------------- propriétés

    private static GenericProperty Text(string key, string label, string value, string group) =>
        new() { Key = key, Label = label, Type = GenericPropertyTypes.Text, Value = value, Group = group };

    private static GenericProperty Ref(string key, string label, string id, string group) =>
        new() { Key = key, Label = label, Type = GenericPropertyTypes.Ref, Value = id, Group = group };

    private static GenericProperty File(string key, string label, GenericFile file, string group) =>
        new() { Key = key, Label = label, Type = GenericPropertyTypes.File, Value = file, Group = group };

    private static void AddText(List<GenericProperty> list, string key, string label, string? value, string group)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            list.Add(Text(key, label, value, group));
        }
    }

    private static void AddNumber(List<GenericProperty> list, string key, string label, double? value, string? unit, string group)
    {
        if (value is { } number && double.IsFinite(number))
        {
            list.Add(new GenericProperty { Key = key, Label = label, Type = GenericPropertyTypes.Number, Value = Math.Round(number, QuantityDecimals, MidpointRounding.AwayFromZero), Unit = unit, Group = group });
        }
    }

    /// <summary>Paramètres Revit à plat <c>{ nom : valeur }</c> : texte, nombre ou booléen selon la valeur ; sans valeur : omis (jamais d'inventé).</summary>
    private static void AddParameters(List<GenericProperty> list, IReadOnlyDictionary<string, object?>? parameters, string prefix, string group)
    {
        if (parameters is null)
        {
            return;
        }

        foreach (var (name, value) in parameters)
        {
            AddValue(list, prefix + name, name, value, null, group);
        }
    }

    private static void AddValue(List<GenericProperty> list, string key, string label, object? value, string? unit, string group)
    {
        switch (value)
        {
            case null:
                return;
            case string text:
                list.Add(new GenericProperty { Key = key, Label = label, Type = GenericPropertyTypes.Text, Value = text, Group = group });
                return;
            case bool flag:
                list.Add(new GenericProperty { Key = key, Label = label, Type = GenericPropertyTypes.Boolean, Value = flag, Group = group });
                return;
            case double or float or decimal or int or long or short or byte or uint or ulong:
                var number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                // Un entier au-delà de 2^53 n'est pas représentable sans perte en JSON : texte.
                if (value is long or ulong && Math.Abs(number) > MaxExactInteger)
                {
                    list.Add(new GenericProperty { Key = key, Label = label, Type = GenericPropertyTypes.Text, Value = Convert.ToString(value, CultureInfo.InvariantCulture)!, Group = group });
                }
                else if (double.IsFinite(number))
                {
                    list.Add(new GenericProperty { Key = key, Label = label, Type = GenericPropertyTypes.Number, Value = RoundSignificant(number), Unit = unit, Group = group });
                }

                return;
            default:
                list.Add(new GenericProperty { Key = key, Label = label, Type = GenericPropertyTypes.Text, Value = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty, Group = group });
                return;
        }
    }

    private const double MaxExactInteger = 9_007_199_254_740_992d;

    /// <summary>Décimales des quantités avec unité (surfaces, volumes, hauteurs : m, m², m³) : 3, soit le millimètre ou le litre.</summary>
    public const int QuantityDecimals = 3;

    /// <summary>Décimales des coordonnées de la boîte (mm) : 1, soit le dixième de millimètre.</summary>
    public const int CoordinateDecimals = 1;

    /// <summary>Chiffres significatifs des paramètres Revit (unité inconnue du plugin) : retire le bruit de virgule flottante (17 chiffres) sans perdre de valeur : Revit ne donne pas 12 chiffres justes.</summary>
    public const int ParameterSignificantDigits = 12;

    /// <summary>Arrondit à <see cref="ParameterSignificantDigits"/> chiffres significatifs (zéro et valeurs entières inchangés).</summary>
    internal static double RoundSignificant(double value)
    {
        if (value == 0 || !double.IsFinite(value) || value == Math.Floor(value))
        {
            return value;
        }

        var digits = ParameterSignificantDigits - 1 - (int)Math.Floor(Math.Log10(Math.Abs(value)));
        if (digits > 15)
        {
            return value;
        }

        return digits >= 0
            ? Math.Round(value, digits, MidpointRounding.AwayFromZero)
            : Math.Round(value / Math.Pow(10, -digits), MidpointRounding.AwayFromZero) * Math.Pow(10, -digits);
    }

    /// <summary>Libellé lisible d'une valeur de <c>Category</c> : <c>Pièce</c>, <c>Porte</c>, <c>Équipement CVC</c>, <c>Terminal électricité</c>…</summary>
    internal static string CategoryLabel(string category)
    {
        switch (category)
        {
            case TrcCategories.Space:
                return "Pièce";
            case TrcCategories.Door:
                return "Porte";
            case GenericCategories.Anomaly:
                return "Anomalie";
            case GenericCategories.Level:
                return "Niveau";
            case GenericCategories.Files:
                return "Fichiers";
        }

        foreach (var (suffix, label) in new[] { (" equipement", "Équipement"), (" terminal", "Terminal") })
        {
            if (category.EndsWith(suffix, StringComparison.Ordinal) && category.Length > suffix.Length)
            {
                var discipline = category[..^suffix.Length];
                return $"{label} {(discipline == "cvc" ? "CVC" : discipline)}";
            }
        }

        return category;
    }

    /// <summary>Boîte valide (valeurs finies, min ≤ max sur chaque axe) ; null sinon.</summary>
    private static GenericBox? Box(BoundingBoxMm box)
    {
        double[] min = [box.MinX, box.MinY, box.MinZ];
        double[] max = [box.MaxX, box.MaxY, box.MaxZ];
        var valid = min.Concat(max).All(double.IsFinite) && min[0] <= max[0] && min[1] <= max[1] && min[2] <= max[2];
        return valid ? new GenericBox([.. min.Select(RoundCoordinate)], [.. max.Select(RoundCoordinate)]) : null;
    }

    private static double RoundCoordinate(double value) => Math.Round(value, CoordinateDecimals, MidpointRounding.AwayFromZero);

    /// <summary>Chemin relatif au ZIP de sortie, commençant par <c>/</c> ; le nom est encodé (espaces, <c>£</c>…).</summary>
    private static string RelativeUrl(string directory, string fileName) =>
        $"/{string.Join('/', directory.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString))}/{Uri.EscapeDataString(fileName)}";

    private static string Unique(string raw, HashSet<string> used)
    {
        var id = raw;
        for (var counter = 2; !used.Add(id); counter++)
        {
            id = $"{raw}-{counter.ToString(CultureInfo.InvariantCulture)}";
        }

        return id;
    }

    private static void Add<T>(Dictionary<string, List<T>> map, string key, T value)
    {
        if (!map.TryGetValue(key, out var list))
        {
            map[key] = list = [];
        }

        list.Add(value);
    }
}
