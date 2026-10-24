using EgRoomTracker.Application;
using EgRoomTracker.Application.Configuration;
using EgRoomTracker.Contracts.Codes;
using EgRoomTracker.Contracts.Configuration;
using EgRoomTracker.Contracts.Results;
using EgRoomTracker.Tests.Application;

namespace EgRoomTracker.Tests.Configuration;

/// <summary>Service complet (EGR-05) et son intégration à l'orchestration : configuration typée, RVT résolu, codes 9.2.</summary>
public sealed class InputConfigurationServiceTests : IDisposable
{
    private readonly InputFixture _fixture = new();
    private readonly IInputConfigurationService _service = new InputConfigurationService();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void Example_WithMatchingZip_IsLoadedAndResolved()
    {
        _fixture.AddFiles("Projet/EG_Batiment_A.rvt", "Projet/Liens/Structure.rvt");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "settings.example.json"), _fixture.SettingsPath);

        var settings = Load();

        Assert.Equal("Projet/EG_Batiment_A.rvt", settings.MainModelPath);
        Assert.Equal("Projet/EG_Batiment_A.rvt", settings.ResolvedMainModelPath);
        Assert.Single(settings.SharedParameters);
    }

    [Fact]
    public void SingleRvtZip_WithoutPath_IsResolved_AndSettingsStayAsRead()
    {
        _fixture.AddFile("Maquette/A.rvt").WriteSettings("""{ "schemaVersion": "1.0", "phaseIds": ["86961"] }""");

        var settings = Load();

        Assert.Null(settings.MainModelPath);
        Assert.Equal("Maquette/A.rvt", settings.ResolvedMainModelPath);
        Assert.Equal(["86961"], settings.PhaseIds);
        Assert.Equal([86961L], settings.PhaseElementIds);
    }

    [Fact]
    public void ExplicitPath_IsKeptAsRead_AndResolvedIsNormalized()
    {
        _fixture.AddFile("Projet/A.rvt").WriteSettings("""{ "schemaVersion": "1.0", "mainModelPath": "Projet\\A.rvt" }""");

        var settings = Load();

        Assert.Equal("Projet\\A.rvt", settings.MainModelPath);
        Assert.Equal("Projet/A.rvt", settings.ResolvedMainModelPath);
    }

    [Fact]
    public void DefaultService_WithoutOutputFormat_KeepsContract()
    {
        _fixture.AddFile("A.rvt").WriteSettings("""{ "schemaVersion": "1.0" }""");

        Assert.Equal(OutputFormat.Contract, Load().OutputFormat);
    }

    [Fact]
    public void DesignAutomationService_WithoutOutputFormat_DefaultsToGeneric()
    {
        // La plateforme n'envoie que schemaVersion et mainModelPath : result.generic.json doit être écrit.
        _fixture.AddFile("A.rvt").WriteSettings("""{ "schemaVersion": "1.0", "mainModelPath": "A.rvt" }""");

        var settings = InputConfigurationService.ForDesignAutomation().Load(_fixture.InputDirectory, _fixture.SettingsPath);

        Assert.Equal(OutputFormat.Generic, settings.OutputFormat);
        Assert.Equal("A.rvt", settings.ResolvedMainModelPath);
    }

    [Fact]
    public void DesignAutomationService_WithNullOutputFormat_DefaultsToGeneric()
    {
        _fixture.AddFile("A.rvt").WriteSettings("""{ "schemaVersion": "1.0", "outputFormat": null }""");

        var settings = InputConfigurationServiceDa().Load(_fixture.InputDirectory, _fixture.SettingsPath);

        Assert.Equal(OutputFormat.Generic, settings.OutputFormat);
    }

    [Theory]
    [InlineData("contract", OutputFormat.Contract)]
    [InlineData("trc", OutputFormat.Trc)]
    [InlineData("both", OutputFormat.Both)]
    [InlineData("generic", OutputFormat.Generic)]
    public void DesignAutomationService_ExplicitOutputFormat_IsRespected(string name, OutputFormat expected)
    {
        _fixture.AddFile("A.rvt").WriteSettings($$"""{ "schemaVersion": "1.0", "outputFormat": "{{name}}" }""");

        var settings = InputConfigurationServiceDa().Load(_fixture.InputDirectory, _fixture.SettingsPath);

        Assert.Equal(expected, settings.OutputFormat);
    }

    [Fact]
    public void DesignAutomationService_UnknownOutputFormat_StillFails()
    {
        _fixture.AddFile("A.rvt").WriteSettings("""{ "schemaVersion": "1.0", "outputFormat": "xml" }""");

        var ex = Assert.Throws<ExtractionFailedException>(() => InputConfigurationServiceDa().Load(_fixture.InputDirectory, _fixture.SettingsPath));

        Assert.Equal(ErrorCodes.ConfigInvalid, ex.Error.Code);
    }

    private static InputConfigurationService InputConfigurationServiceDa() => InputConfigurationService.ForDesignAutomation();

    [Fact]
    public void SeveralRvtWithoutPath_IsRvtAmbiguous()
    {
        _fixture.AddFiles("A.rvt", "B.rvt").WriteSettings("""{ "schemaVersion": "1.0" }""");

        AssertFails(ErrorCodes.RvtAmbiguous, Stages.Input);
    }

    [Fact]
    public void ConfigurationErrors_AreReportedBeforeInputErrors()
    {
        // ZIP vide et configuration invalide : la configuration est signalée en premier.
        _fixture.WriteSettings("""{ "schemaVersion": "1.0", "volumeMode": "fast" }""");

        AssertFails(ErrorCodes.ConfigInvalid, Stages.Configuration);
    }

    [Fact]
    public void UnsupportedSchema_IsReportedBeforePathChecks()
    {
        _fixture.WriteSettings("""{ "schemaVersion": "2.0", "mainModelPath": "../x.rvt" }""");

        AssertFails(ErrorCodes.ConfigSchemaUnsupported, Stages.Configuration);
    }

    [Fact]
    public void PathOutsideInput_IsReported()
    {
        _fixture.AddFile("A.rvt").WriteSettings("""{ "schemaVersion": "1.0", "mainModelPath": "../outside/A.rvt" }""");
        _fixture.AddOutsideFile("A.rvt");

        AssertFails(ErrorCodes.PathOutsideInput, Stages.Configuration);
    }

    public static TheoryData<string, string, string> PipelineFailures => new()
    {
        { "{ \"schemaVersion\": \"3.0\" }", ErrorCodes.ConfigSchemaUnsupported, Stages.Configuration },
        { "{ \"schemaVersion\": \"1.0\", \"phaseIds\": [] }", ErrorCodes.ConfigInvalid, Stages.Configuration },
        { "{ \"schemaVersion\": \"1.0\", \"mainModelPath\": \"/etc/A.rvt\" }", ErrorCodes.PathOutsideInput, Stages.Configuration },
        { "{ \"schemaVersion\": \"1.0\" }", ErrorCodes.RvtAmbiguous, Stages.Input },
        { "{ \"schemaVersion\": \"1.0\", \"mainModelPath\": \"C.rvt\" }", ErrorCodes.RvtNotFound, Stages.Input },
    };

    [Theory]
    [MemberData(nameof(PipelineFailures))]
    public void Pipeline_ReportsInputErrorsAsFailedResult(string json, string code, string stage)
    {
        _fixture.AddFiles("A.rvt", "B.rvt").WriteSettings(json);
        var pipeline = new Pipeline { LoadSettings = Load };

        var result = pipeline.CreateService().Run(Pipeline.Request);

        Assert.Equal(ExtractionStatus.Failed, result.Summary.Status);
        var error = Assert.Single(result.Summary.Errors);
        Assert.Equal((code, stage), (error.Code, error.Stage));
        Assert.Null(result.Summary.Source);
        Assert.Empty(result.Rooms);
        Assert.Equal(["config", "close", "export"], pipeline.Calls);
    }

    [Fact]
    public void Pipeline_Success_ReportsOptionsAsRead()
    {
        _fixture.AddFile("A.rvt").WriteSettings("""{ "schemaVersion": "1.0", "phaseIds": ["86961"], "volumeMode": "compute" }""");
        var pipeline = new Pipeline { LoadSettings = Load };

        var result = pipeline.CreateService().Run(Pipeline.Request);

        Assert.Equal(["86961"], result.Summary.Options.PhaseIds);
        Assert.Equal(VolumeMode.Compute, result.Summary.Options.VolumeMode);
        Assert.Empty(result.Summary.Errors);
    }

    private ExtractionSettings Load() => _service.Load(_fixture.InputDirectory, _fixture.SettingsPath);

    private void AssertFails(string code, string stage)
    {
        var ex = Assert.Throws<ExtractionFailedException>(Load);
        Assert.Equal((code, stage), (ex.Error.Code, ex.Error.Stage));
    }
}
