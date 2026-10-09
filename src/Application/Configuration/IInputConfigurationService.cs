using EgRoomTracker.Contracts.Configuration;

namespace EgRoomTracker.Application.Configuration;

/// <summary>
/// Validation de <c>settings.json</c> et résolution du RVT principal dans le répertoire d'entrée (ZIP décompressé) ;
/// configuration typée dont <see cref="ExtractionSettings.ResolvedMainModelPath"/> est toujours renseigné
/// (<see cref="ExtractionSettings.MainModelPath"/> reste tel que lu).
/// Lève <see cref="ExtractionFailedException"/> avec CONFIG_INVALID, CONFIG_SCHEMA_UNSUPPORTED ou PATH_OUTSIDE_INPUT
/// (étape <c>configuration</c>), RVT_NOT_FOUND ou RVT_AMBIGUOUS (étape <c>input</c>). Voir docs/INPUTS.md.
/// </summary>
public interface IInputConfigurationService
{
    ExtractionSettings Load(string inputDirectory, string settingsPath);
}
