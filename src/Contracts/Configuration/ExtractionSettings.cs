namespace EgRoomTracker.Contracts.Configuration;

/// <summary>
/// Configuration typée issue de <c>settings.json</c> (spécifications, section 2).
/// Produite par <c>IInputConfigurationService</c> après validation.
/// </summary>
public sealed record ExtractionSettings
{
    /// <summary>Version du schéma de configuration (<c>1.0</c>).</summary>
    public required string SchemaVersion { get; init; }

    /// <summary>
    /// <c>mainModelPath</c> tel que lu dans settings.json : chemin relatif du RVT principal dans le répertoire d'entrée ;
    /// obligatoire si plusieurs RVT, null pour la détection automatique. Voir <see cref="ResolvedMainModelPath"/>.
    /// </summary>
    public string? MainModelPath { get; init; }

    /// <summary>
    /// Chemin relatif normalisé (séparateur <c>/</c>, sans <c>.</c> ni <c>..</c>) du RVT principal, confiné au répertoire
    /// d'entrée et existant. Toujours renseigné par <c>IInputConfigurationService.Load</c>, y compris pour un ZIP à RVT
    /// unique (détection automatique) ; null seulement avant résolution.
    /// </summary>
    public string? ResolvedMainModelPath { get; init; }

    /// <summary>ElementId des phases tels que lus (chaînes) ; null = toutes les phases. Voir <see cref="PhaseElementIds"/>.</summary>
    public IReadOnlyList<string>? PhaseIds { get; init; }

    /// <summary>
    /// ElementId des phases typés (<c>ElementId.Value</c>), validés par <c>IInputConfigurationService</c> : entiers positifs,
    /// sans doublon, tableau non vide ; null = toutes les phases. Même ordre que <see cref="PhaseIds"/>.
    /// </summary>
    public IReadOnlyList<long>? PhaseElementIds { get; init; }

    public VolumeMode VolumeMode { get; init; } = VolumeMode.Preserve;

    public IReadOnlyList<SharedParameterMapping> SharedParameters { get; init; } = [];

    /// <summary>Paramètres de la pièce exportés : aucun (défaut) ou tous (<c>rooms[].parameters</c>, schéma 1.4).</summary>
    public RoomParametersMode RoomParameters { get; init; } = RoomParametersMode.None;

    /// <summary>Éléments à rattacher aux pièces (<c>doors</c>, <c>equipment</c>, <c>terminals</c>) ; aucun par défaut.</summary>
    public IReadOnlyList<ElementKind> Elements { get; init; } = [];

    /// <summary>
    /// Catégories Revit (noms de <c>BuiltInCategory</c>, ex. <c>OST_PlumbingFixtures</c>) remplaçant la liste par défaut d'une
    /// famille d'éléments ; une famille absente garde sa liste par défaut.
    /// </summary>
    public IReadOnlyDictionary<ElementKind, IReadOnlyList<string>> ElementCategories { get; init; } =
        new Dictionary<ElementKind, IReadOnlyList<string>>();

    /// <summary>Exporte une vue 3D par pièce dans <c>Images/</c> de la sortie ZIP (nommage TRC) ; désactivé par défaut.</summary>
    public bool RoomImages { get; init; }

    /// <summary>Largeur en pixels des images de pièces (<see cref="DefaultImageWidthPx"/> par défaut, de 400 à 8000).</summary>
    public int ImageWidthPx { get; init; } = DefaultImageWidthPx;

    public const int DefaultImageWidthPx = 8000;

    /// <summary>
    /// Mesure la hauteur minimale de chaque pièce par lancer de rayons (<c>rooms[].heights</c>) et sépare hauteur mesurée et
    /// renseignée ; désactivé par défaut (coût proportionnel à la surface des pièces).
    /// </summary>
    public bool RoomHeights { get; init; }

    /// <summary>
    /// Parcourt aussi les modèles liés pour les équipements et terminaux (<c>true</c> par défaut) : leur géométrie est
    /// ramenée dans le repère du modèle principal par la transformation du lien, puis rattachée à une pièce du modèle principal.
    /// </summary>
    public bool LinkedElements { get; init; } = true;

    /// <summary>
    /// Format du fichier de sortie : <c>contract</c> (<c>result.json</c>, défaut), <c>trc</c> (<c>TRCValidationDict.json</c>, fiches de
    /// l'application TRC, données seules, sans statut), <c>both</c> (les deux) ou <c>generic</c> (<c>result.generic.json</c>, enveloppe
    /// commune des résultats de la plateforme ; le TRC reste un export à part).
    /// </summary>
    public OutputFormat OutputFormat { get; init; } = OutputFormat.Contract;

    /// <summary>Pas de la grille de rayons en mètres (0,5 par défaut, comme TRC ; de 0,1 à 2).</summary>
    public double HeightStepM { get; init; } = DefaultHeightStepM;

    public const double DefaultHeightStepM = 0.5;
    public const double MinHeightStepM = 0.1;
    public const double MaxHeightStepM = 2.0;
    public const int MinImageWidthPx = 400;
    public const int MaxImageWidthPx = 8000;
}

/// <summary>Mapping d'un paramètre partagé par GUID ; <see cref="Key"/> unique.</summary>
public sealed record SharedParameterMapping(string Key, Guid Guid);

/// <summary>Format du fichier de sortie du dossier <c>result/</c>.</summary>
public enum OutputFormat
{
    /// <summary><c>result.json</c> : contrat de sortie versionné, bilan et anomalies compris (défaut).</summary>
    Contract,

    /// <summary><c>TRCValidationDict.json</c> : fiches <c>trc.bimobject</c> et <c>trc.bimobjectpropertyvalue</c>, données seules.</summary>
    Trc,

    /// <summary>Les deux fichiers.</summary>
    Both,

    /// <summary>
    /// <c>result.generic.json</c> : enveloppe commune des résultats de la plateforme (objets, propriétés typées, <c>box</c>, <c>ref</c>,
    /// <c>file</c>), avec <c>Images/</c> si <c>roomImages</c> est actif. Sans <c>result.json</c> ni TRC (sauf échec : <c>result.json</c>).
    /// </summary>
    Generic,
}

/// <summary>Noms de <see cref="OutputFormat"/> dans <c>settings.json</c> et <c>summary.options.outputFormat</c>.</summary>
public static class OutputFormatNames
{
    public static string Of(OutputFormat format) => format switch
    {
        OutputFormat.Trc => "trc",
        OutputFormat.Both => "both",
        OutputFormat.Generic => "generic",
        _ => "contract",
    };
}

/// <summary>Paramètres de la pièce exportés dans <c>rooms[].parameters</c>.</summary>
public enum RoomParametersMode
{
    /// <summary>Seuls les champs du contrat (défaut).</summary>
    None,

    /// <summary>Tous les paramètres de la pièce, y compris natifs, partagés et de projet.</summary>
    All,
}

/// <summary>Éléments rattachables aux pièces.</summary>
public enum ElementKind
{
    Doors,
    Equipment,
    Terminals,
}

/// <summary>Noms des <see cref="ElementKind"/> dans <c>settings.json</c> et <c>result.json</c>.</summary>
public static class ElementKinds
{
    public static IReadOnlyList<string> Names { get; } = ["doors", "equipment", "terminals"];

    public static string ToName(ElementKind kind) => kind switch
    {
        ElementKind.Doors => "doors",
        ElementKind.Equipment => "equipment",
        ElementKind.Terminals => "terminals",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    public static bool TryParse(string? name, out ElementKind kind)
    {
        switch (name)
        {
            case "doors":
                kind = ElementKind.Doors;
                return true;
            case "equipment":
                kind = ElementKind.Equipment;
                return true;
            case "terminals":
                kind = ElementKind.Terminals;
                return true;
            default:
                kind = default;
                return false;
        }
    }
}

public enum VolumeMode
{
    /// <summary>Lecture du réglage du document, sans recalcul (défaut).</summary>
    Preserve,

    /// <summary>Activation du calcul sur la copie de travail (P2, EGR-16).</summary>
    Compute,
}
