namespace EgRoomTracker.Contracts.Configuration;

/// <summary>
/// Configuration typée issue de <c>settings.json</c> (spécifications, section 2).
/// Produite par <c>IInputConfigurationService</c> après validation.
/// </summary>
public sealed record ExtractionSettings
{
    /// <summary>Version du schéma de configuration (<c>1.0</c>).</summary>
    public required string SchemaVersion { get; init; }

    /// <summary>Chemin relatif du RVT principal dans le répertoire d'entrée ; obligatoire si plusieurs RVT.</summary>
    public string? MainModelPath { get; init; }

    /// <summary>ElementId des phases (chaînes) ; null = toutes les phases.</summary>
    public IReadOnlyList<string>? PhaseIds { get; init; }

    public VolumeMode VolumeMode { get; init; } = VolumeMode.Preserve;

    public IReadOnlyList<SharedParameterMapping> SharedParameters { get; init; } = [];
}

/// <summary>Mapping d'un paramètre partagé par GUID ; <see cref="Key"/> unique.</summary>
public sealed record SharedParameterMapping(string Key, Guid Guid);

public enum VolumeMode
{
    /// <summary>Lecture du réglage du document, sans recalcul (défaut).</summary>
    Preserve,

    /// <summary>Activation du calcul sur la copie de travail (P2, EGR-16).</summary>
    Compute,
}
