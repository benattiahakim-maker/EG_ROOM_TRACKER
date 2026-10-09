using DesignAutomationFramework;
using EgRoomTracker.Application;

namespace EgRoomTracker.EntryPoint;

/// <summary>
/// Adaptation du contexte APS vers <see cref="ExtractionRequest"/>.
/// Noms locaux provisoires : à aligner sur la définition de l'Activity (EGR-05, EGR-13).
/// </summary>
internal static class ActivityContract
{
    public const string InputDirectoryName = "input";
    public const string SettingsFileName = "settings.json";
    public const string ResultFileName = "result.json";

    public static ExtractionRequest CreateRequest(DesignAutomationData data)
    {
        var workingDirectory = Directory.GetCurrentDirectory();
        var application = data.RevitApp;
        return new ExtractionRequest
        {
            // Valeurs par défaut : AutomationApplication les remplace par le contexte du WorkItem (WorkItemContext).
            CorrelationId = $"run-{Guid.NewGuid():N}",
            InputDirectory = Path.Combine(workingDirectory, InputDirectoryName),
            SettingsPath = Path.Combine(workingDirectory, SettingsFileName),
            OutputPath = Path.Combine(workingDirectory, ResultFileName),
            RevitVersion = application.VersionNumber,
            RevitBuild = application.VersionBuild,
            ApsEngine = null,
        };
    }
}
