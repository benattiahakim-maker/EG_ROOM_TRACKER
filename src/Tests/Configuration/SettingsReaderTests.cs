using System.Text;
using EgRoomTracker.Application;
using EgRoomTracker.Application.Configuration;
using EgRoomTracker.Contracts.Codes;
using EgRoomTracker.Contracts.Configuration;

namespace EgRoomTracker.Tests.Configuration;

/// <summary>Validation de <c>settings.json</c> (section 2 ; codes CONFIG_INVALID et CONFIG_SCHEMA_UNSUPPORTED, section 9.2).</summary>
public sealed class SettingsReaderTests : IDisposable
{
    private static readonly Guid CodeLocal = Guid.Parse("3f2b8c1e-6d4a-4b7e-9a51-2c0d8e7f4a10");

    private readonly InputFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void Example_IsReadAsTyped()
    {
        var settings = SettingsReader.Read(Path.Combine(AppContext.BaseDirectory, "Data", "settings.example.json"));

        Assert.Equal("1.0", settings.SchemaVersion);
        Assert.Equal("Projet/EG_Batiment_A.rvt", settings.MainModelPath);
        Assert.Null(settings.ResolvedMainModelPath);
        Assert.Null(settings.PhaseIds);
        Assert.Null(settings.PhaseElementIds);
        Assert.Equal(VolumeMode.Preserve, settings.VolumeMode);
        Assert.Equal([new SharedParameterMapping("codeLocal", CodeLocal)], settings.SharedParameters);
        Assert.Equal(OutputFormat.Trc, settings.OutputFormat); // livrable : TRCValidationDict.json seul
    }

    [Fact]
    public void MinimalSettings_UseDefaults()
    {
        var settings = SettingsReader.Parse("""{ "schemaVersion": "1.0" }""");

        Assert.Null(settings.MainModelPath);
        Assert.Null(settings.PhaseIds);
        Assert.Null(settings.PhaseElementIds);
        Assert.Equal(VolumeMode.Preserve, settings.VolumeMode);
        Assert.Empty(settings.SharedParameters);
    }

    [Fact]
    public void ExplicitNulls_UseDefaults()
    {
        var settings = SettingsReader.Parse("""
            { "schemaVersion": "1.0", "mainModelPath": null, "phaseIds": null, "volumeMode": null, "sharedParameters": null }
            """);

        Assert.Null(settings.MainModelPath);
        Assert.Null(settings.PhaseElementIds);
        Assert.Equal(VolumeMode.Preserve, settings.VolumeMode);
        Assert.Empty(settings.SharedParameters);
    }

    [Fact]
    public void ExplicitOptions_AreTyped_AndPhaseIdsKeptAsRead()
    {
        var settings = SettingsReader.Parse("""
            { "schemaVersion": "1.0", "mainModelPath": "Projet\\A.rvt", "phaseIds": ["86961", "0086960"], "volumeMode": "compute",
              "sharedParameters": [ { "key": "codeLocal", "guid": "3F2B8C1E-6D4A-4B7E-9A51-2C0D8E7F4A10" },
                                    { "key": "CodeLocal", "guid": "0b6c1d2e-3f40-4a5b-8c6d-7e8f9a0b1c2d" } ] }
            """);

        Assert.Equal("Projet\\A.rvt", settings.MainModelPath);
        Assert.Equal(["86961", "0086960"], settings.PhaseIds);
        Assert.Equal([86961L, 86960L], settings.PhaseElementIds);
        Assert.Equal(VolumeMode.Compute, settings.VolumeMode);
        Assert.Equal(["codeLocal", "CodeLocal"], settings.SharedParameters.Select(p => p.Key));
        Assert.Equal(CodeLocal, settings.SharedParameters[0].Guid);
    }

    [Fact]
    public void Utf8Bom_AndAccents_AreAccepted()
    {
        File.WriteAllText(_fixture.SettingsPath, """{ "schemaVersion": "1.0", "mainModelPath": "Bâtiment/Été.rvt" }""", new UTF8Encoding(true));

        Assert.Equal("Bâtiment/Été.rvt", SettingsReader.Read(_fixture.SettingsPath).MainModelPath);
    }

    [Theory]
    [InlineData("1.1")]
    [InlineData("1.12")]
    public void NewerMinorVersion_IsAccepted_AndUnknownPropertiesIgnored(string version)
    {
        var settings = SettingsReader.Parse($$"""
            { "schemaVersion": "{{version}}", "futureOption": true,
              "sharedParameters": [ { "key": "a", "guid": "3f2b8c1e-6d4a-4b7e-9a51-2c0d8e7f4a10", "label": "x" } ] }
            """);

        Assert.Equal(version, settings.SchemaVersion);
        Assert.Single(settings.SharedParameters);
    }

    [Theory]
    [InlineData("2.0")]
    [InlineData("0.9")]
    [InlineData("10.0")]
    public void UnsupportedMajorVersion_IsConfigSchemaUnsupported(string version) =>
        AssertFails(ErrorCodes.ConfigSchemaUnsupported, $$"""{ "schemaVersion": "{{version}}", "unknown": 1 }""");

    public static TheoryData<string, string> InvalidSettings => new()
    {
        { "JSON illisible", "{ \"schemaVersion\": " },
        { "JSON vide", "" },
        { "commentaire", "{ \"schemaVersion\": \"1.0\" /* x */ }" },
        { "virgule finale", "{ \"schemaVersion\": \"1.0\", }" },
        { "racine tableau", "[]" },
        { "racine chaîne", "\"1.0\"" },
        { "schemaVersion absent", "{ \"volumeMode\": \"preserve\" }" },
        { "schemaVersion nombre", "{ \"schemaVersion\": 1.0 }" },
        { "schemaVersion null", "{ \"schemaVersion\": null }" },
        { "schemaVersion sans mineure", "{ \"schemaVersion\": \"1\" }" },
        { "schemaVersion à trois niveaux", "{ \"schemaVersion\": \"1.0.0\" }" },
        { "schemaVersion zéro initial", "{ \"schemaVersion\": \"01.0\" }" },
        { "schemaVersion texte", "{ \"schemaVersion\": \"v1\" }" },
        { "propriété en double", "{ \"schemaVersion\": \"1.0\", \"volumeMode\": \"preserve\", \"volumeMode\": \"compute\" }" },
        { "propriété inconnue (faute de frappe) en 1.0", "{ \"schemaVersion\": \"1.0\", \"volumemode\": \"compute\" }" },
        { "mainModelPath nombre", "{ \"schemaVersion\": \"1.0\", \"mainModelPath\": 3 }" },
        { "mainModelPath vide", "{ \"schemaVersion\": \"1.0\", \"mainModelPath\": \"\" }" },
        { "mainModelPath blanc", "{ \"schemaVersion\": \"1.0\", \"mainModelPath\": \"  \" }" },
        { "phaseIds vide", "{ \"schemaVersion\": \"1.0\", \"phaseIds\": [] }" },
        { "phaseIds chaîne", "{ \"schemaVersion\": \"1.0\", \"phaseIds\": \"86961\" }" },
        { "phaseIds nombre JSON", "{ \"schemaVersion\": \"1.0\", \"phaseIds\": [86961] }" },
        { "phaseIds nom de phase", "{ \"schemaVersion\": \"1.0\", \"phaseIds\": [\"Existant\"] }" },
        { "phaseIds négatif", "{ \"schemaVersion\": \"1.0\", \"phaseIds\": [\"-1\"] }" },
        { "phaseIds zéro", "{ \"schemaVersion\": \"1.0\", \"phaseIds\": [\"0\"] }" },
        { "phaseIds décimal", "{ \"schemaVersion\": \"1.0\", \"phaseIds\": [\"86961.0\"] }" },
        { "phaseIds avec espaces", "{ \"schemaVersion\": \"1.0\", \"phaseIds\": [\" 86961\"] }" },
        { "phaseIds hors Int64", "{ \"schemaVersion\": \"1.0\", \"phaseIds\": [\"99999999999999999999\"] }" },
        { "phaseIds null dans le tableau", "{ \"schemaVersion\": \"1.0\", \"phaseIds\": [null] }" },
        { "phaseIds en double", "{ \"schemaVersion\": \"1.0\", \"phaseIds\": [\"86961\", \"86961\"] }" },
        { "phaseIds en double après normalisation", "{ \"schemaVersion\": \"1.0\", \"phaseIds\": [\"86961\", \"086961\"] }" },
        { "volumeMode inconnu", "{ \"schemaVersion\": \"1.0\", \"volumeMode\": \"fast\" }" },
        { "volumeMode casse", "{ \"schemaVersion\": \"1.0\", \"volumeMode\": \"Preserve\" }" },
        { "volumeMode booléen", "{ \"schemaVersion\": \"1.0\", \"volumeMode\": true }" },
        { "sharedParameters objet", "{ \"schemaVersion\": \"1.0\", \"sharedParameters\": { \"key\": \"a\" } }" },
        { "mapping non objet", "{ \"schemaVersion\": \"1.0\", \"sharedParameters\": [\"a\"] }" },
        { "clé absente", "{ \"schemaVersion\": \"1.0\", \"sharedParameters\": [ { \"guid\": \"3f2b8c1e-6d4a-4b7e-9a51-2c0d8e7f4a10\" } ] }" },
        { "clé vide", "{ \"schemaVersion\": \"1.0\", \"sharedParameters\": [ { \"key\": \"\", \"guid\": \"3f2b8c1e-6d4a-4b7e-9a51-2c0d8e7f4a10\" } ] }" },
        { "clé avec espaces autour", "{ \"schemaVersion\": \"1.0\", \"sharedParameters\": [ { \"key\": \" a\", \"guid\": \"3f2b8c1e-6d4a-4b7e-9a51-2c0d8e7f4a10\" } ] }" },
        { "clé nombre", "{ \"schemaVersion\": \"1.0\", \"sharedParameters\": [ { \"key\": 1, \"guid\": \"3f2b8c1e-6d4a-4b7e-9a51-2c0d8e7f4a10\" } ] }" },
        { "clé en double", "{ \"schemaVersion\": \"1.0\", \"sharedParameters\": [ { \"key\": \"a\", \"guid\": \"3f2b8c1e-6d4a-4b7e-9a51-2c0d8e7f4a10\" }, { \"key\": \"a\", \"guid\": \"0b6c1d2e-3f40-4a5b-8c6d-7e8f9a0b1c2d\" } ] }" },
        { "GUID absent", "{ \"schemaVersion\": \"1.0\", \"sharedParameters\": [ { \"key\": \"a\" } ] }" },
        { "GUID invalide", "{ \"schemaVersion\": \"1.0\", \"sharedParameters\": [ { \"key\": \"a\", \"guid\": \"codeLocal\" } ] }" },
        { "GUID entre accolades", "{ \"schemaVersion\": \"1.0\", \"sharedParameters\": [ { \"key\": \"a\", \"guid\": \"{3f2b8c1e-6d4a-4b7e-9a51-2c0d8e7f4a10}\" } ] }" },
        { "GUID sans tirets", "{ \"schemaVersion\": \"1.0\", \"sharedParameters\": [ { \"key\": \"a\", \"guid\": \"3f2b8c1e6d4a4b7e9a512c0d8e7f4a10\" } ] }" },
        { "GUID nul", "{ \"schemaVersion\": \"1.0\", \"sharedParameters\": [ { \"key\": \"a\", \"guid\": \"00000000-0000-0000-0000-000000000000\" } ] }" },
        { "propriété inconnue dans un mapping en 1.0", "{ \"schemaVersion\": \"1.0\", \"sharedParameters\": [ { \"key\": \"a\", \"guid\": \"3f2b8c1e-6d4a-4b7e-9a51-2c0d8e7f4a10\", \"label\": \"x\" } ] }" },
    };

    [Theory]
    [MemberData(nameof(InvalidSettings))]
    public void InvalidSettings_AreConfigInvalid(string description, string json)
    {
        _ = description;
        AssertFails(ErrorCodes.ConfigInvalid, json);
    }

    [Fact]
    public void MissingFile_IsConfigInvalid()
    {
        var ex = Assert.Throws<ExtractionFailedException>(() => SettingsReader.Read(_fixture.SettingsPath));

        Assert.Equal((ErrorCodes.ConfigInvalid, Stages.Configuration), (ex.Error.Code, ex.Error.Stage));
        Assert.Contains("absent", ex.Error.Message);
    }

    [Fact]
    public void InvalidUtf8_IsConfigInvalid()
    {
        File.WriteAllBytes(_fixture.SettingsPath, [.. "{ \"schemaVersion\": \"1.0\", \"mainModelPath\": \""u8, 0xC3, 0x28, .. "\" }"u8]);

        var ex = Assert.Throws<ExtractionFailedException>(() => SettingsReader.Read(_fixture.SettingsPath));

        Assert.Equal(ErrorCodes.ConfigInvalid, ex.Error.Code);
    }

    [Fact]
    public void OversizedFile_IsConfigInvalid()
    {
        File.WriteAllText(_fixture.SettingsPath, "{ \"schemaVersion\": \"1.0\" }" + new string(' ', SettingsReader.MaxSizeBytes));

        var ex = Assert.Throws<ExtractionFailedException>(() => SettingsReader.Read(_fixture.SettingsPath));

        Assert.Equal(ErrorCodes.ConfigInvalid, ex.Error.Code);
    }

    [Fact]
    public void ErrorMessages_NameTheFaultyProperty()
    {
        var ex = Assert.Throws<ExtractionFailedException>(() => SettingsReader.Parse("""
            { "schemaVersion": "1.0", "sharedParameters": [ { "key": "a", "guid": "3f2b8c1e-6d4a-4b7e-9a51-2c0d8e7f4a10" },
                                                             { "key": "a", "guid": "0b6c1d2e-3f40-4a5b-8c6d-7e8f9a0b1c2d" } ] }
            """));

        Assert.StartsWith("settings.json : sharedParameters[1].key", ex.Error.Message);
    }

    private static void AssertFails(string code, string json)
    {
        var ex = Assert.Throws<ExtractionFailedException>(() => SettingsReader.Parse(json));
        Assert.Equal(code, ex.Error.Code);
        Assert.Equal(Stages.Configuration, ex.Error.Stage);
    }
}
