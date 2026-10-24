using DesignAutomationFramework;
using EgRoomTracker.Application;

namespace EgRoomTracker.EntryPoint;

/// <summary>
/// Adaptation du contexte APS vers <see cref="ExtractionRequest"/>.
/// Les noms locaux sont ceux de la définition de l'Activity (<c>src/Packaging/definitions</c>) : le paramètre de sortie
/// <c>result</c> est le dossier <c>result/</c>, zippé par Design Automation (<c>zip: true</c>) ; il contient
/// <c>result.json</c> (ou <c>result.generic.json</c>), <c>log.jsonl</c> (journal JSON Lines, fichier annexe) et, avec
/// <c>roomImages</c>, le dossier <c>Images/</c> (vues 3D des pièces).
/// </summary>
internal static class ActivityContract
{
    public const string InputDirectoryName = "input";
    public const string SettingsFileName = "settings.json";
    public const string ResultDirectoryName = "result";
    public const string ResultFileName = "result.json";
    public const string ImagesDirectoryName = "Images";

    /// <summary>Journal JSON Lines copié dans le ZIP de sortie (fichier annexe : hors <c>result.json</c> et hors enveloppe generic).</summary>
    public const string LogFileName = "log.jsonl";

    public static string LogFilePath(string workingDirectory) =>
        Path.Combine(workingDirectory, ResultDirectoryName, LogFileName);

    public static ExtractionRequest CreateRequest(DesignAutomationData data)
    {
        var workingDirectory = Directory.GetCurrentDirectory();
        var application = data.RevitApp;
        var resultDirectory = Path.Combine(workingDirectory, ResultDirectoryName);
        return new ExtractionRequest
        {
            // Valeurs par défaut : AutomationApplication les remplace par le contexte du WorkItem (WorkItemContext).
            CorrelationId = $"run-{Guid.NewGuid():N}",
            InputDirectory = Path.Combine(workingDirectory, InputDirectoryName),
            SettingsPath = Path.Combine(workingDirectory, SettingsFileName),
            OutputPath = Path.Combine(resultDirectory, ResultFileName),
            ImagesDirectory = Path.Combine(resultDirectory, ImagesDirectoryName),
            RevitVersion = application.VersionNumber,
            RevitBuild = application.VersionBuild,
            ApsEngine = null,
        };
    }
}
