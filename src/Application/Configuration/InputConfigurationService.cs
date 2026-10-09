using EgRoomTracker.Contracts.Codes;
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
        EnsureSupportedVolumeMode(settings);
        return settings with { ResolvedMainModelPath = MainModelResolver.Resolve(inputDirectory, settings.MainModelPath) };
    }

    /// <summary>
    /// <c>volumeMode: "compute"</c> (P2, EGR-16) n'est pas encore disponible : refus précoce CONFIG_INVALID (étape
    /// <c>configuration</c>), avant la résolution du RVT et l'ouverture du document. Le service de volume garde un
    /// contrôle défensif équivalent.
    /// </summary>
    internal static void EnsureSupportedVolumeMode(ExtractionSettings settings)
    {
        if (settings.VolumeMode == VolumeMode.Compute)
        {
            throw new ExtractionFailedException(ErrorCodes.ConfigInvalid, Stages.Configuration,
                "settings.json : volumeMode « compute » non disponible dans cette version (EGR-16) ; utiliser « preserve ».");
        }
    }
}
