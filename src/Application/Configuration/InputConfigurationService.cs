using EgRoomTracker.Contracts.Configuration;

namespace EgRoomTracker.Application.Configuration;

/// <summary>
/// Implémentation d'<see cref="IInputConfigurationService"/> (EGR-05) : lecture stricte de <c>settings.json</c>
/// (<see cref="SettingsReader"/>), puis résolution confinée du RVT principal (<see cref="MainModelResolver"/>).
/// Les erreurs de configuration sont détectées avant toute exploration du répertoire d'entrée.
/// </summary>
public sealed class InputConfigurationService : IInputConfigurationService
{
    private readonly OutputFormat _defaultOutputFormat;

    /// <param name="defaultOutputFormat">Format utilisé quand <c>outputFormat</c> est absent ou null (défaut : <see cref="OutputFormat.Contract"/>).</param>
    public InputConfigurationService(OutputFormat defaultOutputFormat = OutputFormat.Contract)
    {
        _defaultOutputFormat = defaultOutputFormat;
    }

    /// <summary>
    /// Service du chemin Design Automation (point d'entrée APS) : sans <c>outputFormat</c> dans <c>settings.json</c>, la sortie
    /// est <see cref="OutputFormat.Generic"/> (<c>result.generic.json</c>), car la plateforme n'envoie que
    /// <c>schemaVersion</c> et <c>mainModelPath</c>. Un <c>outputFormat</c> explicite reste respecté.
    /// </summary>
    public static InputConfigurationService ForDesignAutomation() => new(OutputFormat.Generic);

    public ExtractionSettings Load(string inputDirectory, string settingsPath)
    {
        var settings = SettingsReader.Read(settingsPath, _defaultOutputFormat);
        return settings with { ResolvedMainModelPath = MainModelResolver.Resolve(inputDirectory, settings.MainModelPath) };
    }
}
