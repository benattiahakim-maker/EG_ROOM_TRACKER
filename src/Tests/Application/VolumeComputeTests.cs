using System.Text.Json.Nodes;
using EgRoomTracker.Application;
using EgRoomTracker.Application.Configuration;
using EgRoomTracker.Application.Quality;
using EgRoomTracker.Application.Revit;
using EgRoomTracker.Application.Units;
using EgRoomTracker.Contracts.Codes;
using EgRoomTracker.Contracts.Configuration;
using EgRoomTracker.Contracts.Results;
using EgRoomTracker.Logging;
using EgRoomTracker.Tests.Configuration;
using EgRoomTracker.Tests.Support;

namespace EgRoomTracker.Tests.Application;

/// <summary>EGR-16 : volumeMode compute (calcul temporaire, lecture avant annulation, traçabilité summary et logs).</summary>
public sealed class VolumeComputeTests : IDisposable
{
    private sealed class FeetUnits : IUnitConversionService
    {
        private const double Foot = 0.3048;
        public double ToMeters(double internalLength) => internalLength * Foot;
        public double ToSquareMeters(double internalArea) => internalArea * Foot * Foot;
        public double ToCubicMeters(double internalVolume) => internalVolume * Foot * Foot * Foot;
    }

    private static readonly IUnitConversionService Units = new FeetUnits();

    private static readonly VolumeComputationState Applied = new(VolumeMode.Compute, true)
    {
        DocumentComputeVolumes = false,
        ComputeApplied = true,
        TransactionRolledBack = true,
        ComputedVolumes = new Dictionary<long, double> { [412345] = 1000.0 },
    };

    private static readonly VolumeComputationState Failed = new(VolumeMode.Compute, false)
    {
        DocumentComputeVolumes = false,
        ComputeFailure = "Activation du calcul des volumes impossible (InvalidOperationException).",
        TransactionRolledBack = true,
    };

    private readonly InputFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private static RoomQuantityValues Convert(VolumeComputationState state, long elementId, Func<double?> live, bool isPlaced = true)
    {
        var (volumesComputed, readVolume) = state.VolumeSource(elementId, live);
        return RoomQuantities.Convert(100.0, 10.0, readVolume, volumesComputed, isPlaced, Units);
    }

    [Fact]
    public void Compute_UsesVolumeReadBeforeRollBack_NotTheLiveValue()
    {
        var q = Convert(Applied, 412345, () => throw new InvalidOperationException("lecture après annulation interdite"));

        Assert.Equal(VolumeStatus.Computed, q.VolumeStatus);
        Assert.Equal(1000.0 * 0.3048 * 0.3048 * 0.3048, q.VolumeM3);
    }

    [Fact]
    public void Compute_RoomMissingFromCache_IsNotAvailable()
    {
        var q = Convert(Applied, 999, () => 5.0);

        Assert.Equal(VolumeStatus.NotAvailable, q.VolumeStatus);
        Assert.Null(q.VolumeM3);
    }

    [Fact]
    public void Compute_UnplacedRoom_IsNotAvailable()
    {
        var q = Convert(Applied, 412345, () => 5.0, isPlaced: false);

        Assert.Equal(VolumeStatus.NotAvailable, q.VolumeStatus);
    }

    [Fact]
    public void Compute_Failure_GivesNotAvailable_NeverNotComputed()
    {
        var q = Convert(Failed, 412345, () => 5.0);

        Assert.Equal(VolumeStatus.NotAvailable, q.VolumeStatus);
        Assert.Null(q.VolumeM3);
    }

    [Theory]
    [InlineData(VolumeMode.Preserve, true, VolumeStatus.Computed)]
    [InlineData(VolumeMode.Preserve, false, VolumeStatus.NotComputed)]
    [InlineData(VolumeMode.Compute, true, VolumeStatus.Computed)]
    public void WithoutTemporaryComputation_ReadsLiveValueAccordingToDocumentSetting(VolumeMode mode, bool documentSetting, VolumeStatus expected)
    {
        var state = new VolumeComputationState(mode, documentSetting) { DocumentComputeVolumes = documentSetting };

        Assert.Equal(expected, Convert(state, 412345, () => 1000.0).VolumeStatus);
    }

    [Fact]
    public void VolumeComputationState_PositionalConstructor_StaysCompatible()
    {
        Assert.Equal(new VolumeComputationState(VolumeMode.Preserve, false), new VolumeComputationState(VolumeMode.Preserve, VolumesComputed: false));
        Assert.Null(new VolumeComputationState(VolumeMode.Preserve, true).ComputedVolumes);
    }

    [Fact]
    public void Configuration_AcceptsCompute()
    {
        _fixture.AddFile("A.rvt").WriteSettings("""{ "schemaVersion": "1.0", "volumeMode": "compute" }""");

        var settings = new InputConfigurationService().Load(_fixture.InputDirectory, _fixture.SettingsPath);

        Assert.Equal(VolumeMode.Compute, settings.VolumeMode);
    }

    [Fact]
    public void Quality_ComputeFailure_ReportsVolumeComputeFailedWarning_InsteadOfVolumeNotComputed()
    {
        var rooms = new[] { Rooms.Complete(412345, "001") with { VolumeM3 = null, VolumeStatus = VolumeStatus.NotAvailable } };

        var report = new QualityCheckService().Evaluate(rooms, [], Failed);

        var anomaly = Assert.Single(report.Anomalies, a => a.Scope == AnomalyScope.Document);
        Assert.Equal(DocumentAnomalyCodes.VolumeComputeFailed, anomaly.Code);
        Assert.Equal(AnomalySeverity.Warning, anomaly.Severity);
        Assert.Equal("compute", anomaly.Details["volumeMode"]);
        Assert.Equal(1, anomaly.Details["roomsAffected"]);
        Assert.Equal(Failed.ComputeFailure, anomaly.Details["reason"]);
        Assert.DoesNotContain(report.Anomalies, a => a.Code == DocumentAnomalyCodes.VolumeNotComputed);
        Assert.Equal(ExtractionStatus.SucceededWithWarnings, report.Status);
    }

    [Fact]
    public void Quality_ComputeApplied_ReportsNoVolumeAnomaly()
    {
        var report = new QualityCheckService().Evaluate([Rooms.Complete(412345, "001")], [], Applied);

        Assert.DoesNotContain(report.Anomalies, a => a.Code is DocumentAnomalyCodes.VolumeNotComputed or DocumentAnomalyCodes.VolumeComputeFailed);
    }

    [Fact]
    public void Pipeline_ComputeApplied_IsTracedInSummaryAndLogs()
    {
        var pipeline = ComputePipeline(Applied);

        var result = pipeline.CreateService().Run(Pipeline.Request);

        Assert.Equal(VolumeMode.Compute, result.Summary.Options.VolumeMode);
        Assert.Equal(new VolumeComputationSummary(false, true, true), result.Summary.VolumeComputation);
        var log = Assert.Single(pipeline.Logs, l => l.EventCode == LogEventCodes.VolumeComputation);
        Assert.Equal(LogLevel.Info, log.Level);
        Assert.Equal(Stages.Document, log.Stage);
        Assert.Equal("compute", log.Data!["volumeMode"]);
        Assert.Equal(false, log.Data["documentComputeVolumes"]);
        Assert.Equal(true, log.Data["computeApplied"]);
        Assert.Equal(true, log.Data["transactionRolledBack"]);
        Assert.Equal(1, log.Data["roomsRead"]);
        Assert.DoesNotContain(pipeline.Logs, l => l.EventCode == LogEventCodes.VolumeComputeFailed);
    }

    [Fact]
    public void Pipeline_RollBackNotConfirmed_IsLoggedAsWarning()
    {
        var pipeline = ComputePipeline(Applied with { TransactionRolledBack = false });

        var result = pipeline.CreateService().Run(Pipeline.Request);

        Assert.Equal(LogLevel.Warning, pipeline.Logs.Single(l => l.EventCode == LogEventCodes.VolumeComputation).Level);
        Assert.Equal(false, result.Summary.VolumeComputation!.TransactionRolledBack);
    }

    [Fact]
    public void Pipeline_ComputeFailure_IsLoggedAndTraced_WithoutFailingTheExtraction()
    {
        var pipeline = ComputePipeline(Failed);

        var result = pipeline.CreateService().Run(Pipeline.Request);

        Assert.NotEqual(ExtractionStatus.Failed, result.Summary.Status);
        Assert.Equal(new VolumeComputationSummary(false, false, true), result.Summary.VolumeComputation);
        var log = Assert.Single(pipeline.Logs, l => l.EventCode == LogEventCodes.VolumeComputeFailed);
        Assert.Equal(LogLevel.Warning, log.Level);
        Assert.Equal(Failed.ComputeFailure, log.Message);
    }

    [Fact]
    public void Pipeline_Preserve_TracesDocumentSettingWithoutTransaction()
    {
        var pipeline = new Pipeline
        {
            PrepareVolume = mode => new VolumeComputationState(mode, false) { DocumentComputeVolumes = false },
        };

        var result = pipeline.CreateService().Run(Pipeline.Request);

        Assert.Equal(new VolumeComputationSummary(false, false, null), result.Summary.VolumeComputation);
    }

    [Fact]
    public void Pipeline_RegenerationFailure_FailsWithDocumentCorrupted_AndNullVolumeComputation()
    {
        var pipeline = ComputePipeline(null, new ExtractionFailedException(ErrorCodes.DocumentCorrupted, Stages.Document, "Échec de régénération."));

        var result = pipeline.CreateService().Run(Pipeline.Request);

        Assert.Equal(ExtractionStatus.Failed, result.Summary.Status);
        var error = Assert.Single(result.Summary.Errors);
        Assert.Equal((ErrorCodes.DocumentCorrupted, Stages.Document), (error.Code, error.Stage));
        Assert.Null(result.Summary.VolumeComputation);
        Assert.Contains("close", pipeline.Calls);
    }

    [Fact]
    public void Contract_VolumeComputationAndComputeFailedAnomaly_AreSchemaValid()
    {
        var pipeline = ComputePipeline(Failed);
        pipeline.ReadRoom = room => new RoomReadResult(
            Rooms.Complete(room.ElementId, room.ElementId.ToString()) with { UniqueId = room.UniqueId, VolumeM3 = null, VolumeStatus = VolumeStatus.NotAvailable },
            []);
        var report = new QualityCheckService();

        var result = pipeline.CreateService().Run(Pipeline.Request);
        var evaluated = report.Evaluate(result.Rooms, [], Failed);
        var json = JsonNode.Parse(ContractJson.Serialize(result with { Anomalies = evaluated.Anomalies }))!;

        Assert.Equal("1.3", json["schemaVersion"]!.GetValue<string>());
        Assert.False(json["summary"]!["volumeComputation"]!["computeApplied"]!.GetValue<bool>());
        ResultSchema.AssertValid(json);
    }

    private static Pipeline ComputePipeline(VolumeComputationState? state, Exception? prepareException = null) => new()
    {
        LoadSettings = () => new ExtractionSettings { SchemaVersion = "1.0", VolumeMode = VolumeMode.Compute },
        PrepareVolume = _ => prepareException is null ? state! : throw prepareException,
    };
}
