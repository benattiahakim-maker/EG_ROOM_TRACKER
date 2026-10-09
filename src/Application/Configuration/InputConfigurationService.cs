using EgRoomTracker.Contracts.Configuration;

namespace EgRoomTracker.Application.Configuration;

/// <summary>
/// Implémentation d'<see cref="IInputConfigurationService"/> (EGR-05) : lecture stricte de <c>settings.json</c>
/// (<see cref="SettingsReader"/>), puis résolution confinée du RVT principal (<see cref="MainModelResolver"/>).
/// Les erreurs de configuration sont détectées avant toute exploration du répertoire d'entrée.
/// </summary>
public sealed class InputConfigurationService : IInputConfigurationService
{
    public ExtractionSettings Load(string inputDirectory, string settingsPath)
    {
        var settings = SettingsReader.Read(settingsPath);
        return settings with { ResolvedMainModelPath = MainModelResolver.Resolve(inputDirectory, settings.MainModelPath) };
    }
}
