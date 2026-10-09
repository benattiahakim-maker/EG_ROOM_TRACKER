using EgRoomTracker.Application;
using EgRoomTracker.Application.Configuration;
using EgRoomTracker.Contracts.Codes;
using EgRoomTracker.Contracts.Configuration;
using EgRoomTracker.Tests.Support;

namespace EgRoomTracker.Tests.Pending;

/// <summary>
/// Spécification exécutable de la validation de <c>settings.json</c> (section 2, codes 9.2), à activer avec EGR-05.
/// </summary>
public sealed class InputConfigurationServiceSpecTests : IDisposable
{
    private const string Skip = PendingImplementations.Egr05;

    private readonly string _root = Directory.CreateTempSubdirectory("egr17-config-").FullName;
    private readonly IInputConfigurationService _service = new InputConfigurationService();

    public InputConfigurationServiceSpecTests()
    {
        Directory.CreateDirectory(Path.Combine(InputDirectory, "Projet"));
        File.WriteAllBytes(Path.Combine(InputDirectory, "Projet", "EG_Batiment_A.rvt"), []);
    }

    private string InputDirectory => Path.Combine(_root, "input");

    private string SettingsPath => Path.Combine(_root, "settings.json");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact(Skip = Skip)]
    public void Example_IsLoadedAsTypedSettings()
    {
        File.Copy(ResultSchema.DataPath("settings.example.json"), SettingsPath);

        var settings = _service.Load(InputDirectory, SettingsPath);

        Assert.Equal("1.0", settings.SchemaVersion);
        Assert.Equal("Projet/EG_Batiment_A.rvt", settings.MainModelPath);
        Assert.Null(settings.PhaseIds);
        Assert.Equal(VolumeMode.Preserve, settings.VolumeMode);
        Assert.Equal([new SharedParameterMapping("codeLocal", Rooms.CodeLocalGuid)], settings.SharedParameters);
    }

    [Fact(Skip = Skip)]
    public void MinimalSettings_UseDefaults()
    {
        var settings = Load("""{ "schemaVersion": "1.0" }""");

        // Décision du chef de projet : mainModelPath est toujours résolu (RVT unique détecté automatiquement).
        Assert.Equal("Projet/EG_Batiment_A.rvt", settings.MainModelPath);
        Assert.Null(settings.PhaseIds);
        Assert.Equal(VolumeMode.Preserve, settings.VolumeMode);
        Assert.Empty(settings.SharedParameters);
    }

    [Fact(Skip = Skip)]
    public void ExplicitOptions_AreLoaded()
    {
        var settings = Load("""
            { "schemaVersion": "1.0", "mainModelPath": null, "phaseIds": ["86961", "86960"], "volumeMode": "compute",
              "sharedParameters": [ { "key": "a", "guid": "3f2b8c1e-6d4a-4b7e-9a51-2c0d8e7f4a10" },
                                    { "key": "b", "guid": "0b6c1d2e-3f40-4a5b-8c6d-7e8f9a0b1c2d" } ] }
            """);

        Assert.Equal(["86961", "86960"], settings.PhaseIds);
        Assert.Equal(VolumeMode.Compute, settings.VolumeMode);
        Assert.Equal(["a", "b"], settings.SharedParameters.Select(p => p.Key));
    }

    [Fact(Skip = Skip)]
    public void MinorSchemaVersion_IsAccepted() =>
        Assert.Equal("1.1", Load("""{ "schemaVersion": "1.1" }""").SchemaVersion);

    [Fact(Skip = Skip)]
    public void MissingSettingsFile_IsConfigInvalid() =>
        AssertFails(ErrorCodes.ConfigInvalid, () => _service.Load(InputDirectory, SettingsPath));

    public static TheoryData<string, string, string> InvalidSettings => new()
    {
        { "JSON illisible", "{ \"schemaVersion\": ", ErrorCodes.ConfigInvalid },
        { "document non objet", "[]", ErrorCodes.ConfigInvalid },
        { "schemaVersion absent", "{ \"volumeMode\": \"preserve\" }", ErrorCodes.ConfigInvalid },
        { "schemaVersion non chaîne", "{ \"schemaVersion\": 1 }", ErrorCodes.ConfigInvalid },
        { "version majeure 2", "{ \"schemaVersion\": \"2.0\" }", ErrorCodes.ConfigSchemaUnsupported },
        { "version majeure 0", "{ \"schemaVersion\": \"0.9\" }", ErrorCodes.ConfigSchemaUnsupported },
        { "volumeMode inconnu", "{ \"schemaVersion\": \"1.0\", \"volumeMode\": \"fast\" }", ErrorCodes.ConfigInvalid },
        { "phaseIds non numérique", "{ \"schemaVersion\": \"1.0\", \"phaseIds\": [\"Existant\"] }", ErrorCodes.ConfigInvalid },
        { "clé de paramètre partagé en double", "{ \"schemaVersion\": \"1.0\", \"sharedParameters\": [ { \"key\": \"a\", \"guid\": \"3f2b8c1e-6d4a-4b7e-9a51-2c0d8e7f4a10\" }, { \"key\": \"a\", \"guid\": \"0b6c1d2e-3f40-4a5b-8c6d-7e8f9a0b1c2d\" } ] }", ErrorCodes.ConfigInvalid },
        { "clé de paramètre partagé vide", "{ \"schemaVersion\": \"1.0\", \"sharedParameters\": [ { \"key\": \"\", \"guid\": \"3f2b8c1e-6d4a-4b7e-9a51-2c0d8e7f4a10\" } ] }", ErrorCodes.ConfigInvalid },
        { "GUID invalide", "{ \"schemaVersion\": \"1.0\", \"sharedParameters\": [ { \"key\": \"a\", \"guid\": \"codeLocal\" } ] }", ErrorCodes.ConfigInvalid },
        { "mainModelPath remontant", "{ \"schemaVersion\": \"1.0\", \"mainModelPath\": \"../autre.rvt\" }", ErrorCodes.PathOutsideInput },
        { "mainModelPath remontant imbriqué", "{ \"schemaVersion\": \"1.0\", \"mainModelPath\": \"Projet/../../autre.rvt\" }", ErrorCodes.PathOutsideInput },
        { "mainModelPath absolu", "{ \"schemaVersion\": \"1.0\", \"mainModelPath\": \"/etc/autre.rvt\" }", ErrorCodes.PathOutsideInput },
    };

    [Theory(Skip = Skip)]
    [MemberData(nameof(InvalidSettings))]
    public void InvalidSettings_FailWithExpectedCode(string description, string json, string expectedCode)
    {
        _ = description;
        File.WriteAllText(SettingsPath, json);

        AssertFails(expectedCode, () => _service.Load(InputDirectory, SettingsPath));
    }

    private ExtractionSettings Load(string json)
    {
        File.WriteAllText(SettingsPath, json);
        return _service.Load(InputDirectory, SettingsPath);
    }

    private static void AssertFails(string code, Action action)
    {
        var ex = Assert.Throws<ExtractionFailedException>(action);
        Assert.Equal(code, ex.Error.Code);
        Assert.Equal(Stages.Configuration, ex.Error.Stage);
    }
}
