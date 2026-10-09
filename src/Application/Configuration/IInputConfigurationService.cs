using EgRoomTracker.Contracts.Configuration;

namespace EgRoomTracker.Application.Configuration;

/// <summary>
/// Validation de <c>settings.json</c> et des chemins (confinés au répertoire d'entrée) ; configuration typée.
/// Lève <see cref="ExtractionFailedException"/> avec CONFIG_INVALID, CONFIG_SCHEMA_UNSUPPORTED ou PATH_OUTSIDE_INPUT.
/// </summary>
public interface IInputConfigurationService
{
    ExtractionSettings Load(string inputDirectory, string settingsPath);
}

/// <summary>Squelette ; implémentation en EGR-05 (#5).</summary>
public sealed class InputConfigurationService : IInputConfigurationService
{
    public ExtractionSettings Load(string inputDirectory, string settingsPath) =>
        throw new NotImplementedException("EGR-05 : validation des entrées ZIP/configuration.");
}
