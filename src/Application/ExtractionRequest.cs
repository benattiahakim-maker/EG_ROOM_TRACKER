namespace EgRoomTracker.Application;

/// <summary>
/// Contexte d'exécution adapté depuis Design Automation par <c>AutomationApplication</c>.
/// Chemins locaux du WorkItem ; aucun secret ni URL signée.
/// </summary>
public sealed record ExtractionRequest
{
    public required string CorrelationId { get; init; }

    /// <summary>Répertoire d'entrée (ZIP décompressé par l'Activity).</summary>
    public required string InputDirectory { get; init; }

    public required string SettingsPath { get; init; }

    /// <summary>Chemin local de <c>result.json</c>, paramètre de sortie de l'Activity.</summary>
    public required string OutputPath { get; init; }

    /// <summary>Version Revit du moteur : <c>2025</c>, <c>2026</c> ou <c>2027</c>.</summary>
    public required string RevitVersion { get; init; }

    public string? RevitBuild { get; init; }

    public string? ApsEngine { get; init; }
}
