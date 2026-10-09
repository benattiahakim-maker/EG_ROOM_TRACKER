using EgRoomTracker.Application;
using EgRoomTracker.Application.Revit;
using EgRoomTracker.Contracts.Codes;
using EgRoomTracker.Contracts.Configuration;
using EgRoomTracker.Contracts.Results;
using EgRoomTracker.Logging;

namespace EgRoomTracker.Tests.Application;

public class RoomExtractionServiceTests
{
    [Fact]
    public void Run_Success_CallsStepsInOrderAndSortsRoomsByElementId()
    {
        var pipeline = new Pipeline();

        var result = pipeline.CreateService().Run(Pipeline.Request);

        Assert.Equal(
            ["config", "document", "volume", "collect", "read:412345", "read:415002", "quality", "close", "export"],
            pipeline.Calls);
        Assert.Equal(ExtractionStatus.Succeeded, result.Summary.Status);
        Assert.Equal(["412345", "415002"], result.Rooms.Select(r => r.ElementId));
        Assert.Equal(new RoomCounts(2, 2, 0), result.Summary.Counts);
        Assert.Equal(["codeLocal"], result.Summary.Options.SharedParameterKeys);
        Assert.Equal("Projet/A.rvt", result.Summary.Source?.RelativePath);
        Assert.Same(result, pipeline.Exported);
        Assert.Equal(1, pipeline.Flushes);
    }

    [Fact]
    public void Run_Success_LogsMinimalEvents()
    {
        var pipeline = new Pipeline();

        pipeline.CreateService().Run(Pipeline.Request);

        Assert.Equal(
            [
                LogEventCodes.ExtractionStarted, LogEventCodes.ConfigurationValidated, LogEventCodes.DocumentAccessible,
                LogEventCodes.CollectionCompleted, LogEventCodes.AnomaliesDetected, LogEventCodes.ExportCompleted,
                LogEventCodes.ExtractionSummary,
            ],
            pipeline.Logs.Select(l => l.EventCode));
        var anomalies = pipeline.Logs.Single(l => l.EventCode == LogEventCodes.AnomaliesDetected);
        Assert.Equal(LogLevel.Info, anomalies.Level);
    }

    [Fact]
    public void Run_ConfigurationError_ExportsFailedResultWithEmptyRooms()
    {
        var pipeline = new Pipeline
        {
            LoadSettings = () => throw new ExtractionFailedException(ErrorCodes.ConfigInvalid, Stages.Configuration, "settings.json absent."),
        };

        var result = pipeline.CreateService().Run(Pipeline.Request);

        Assert.Equal(ExtractionStatus.Failed, result.Summary.Status);
        var error = Assert.Single(result.Summary.Errors);
        Assert.Equal(ErrorCodes.ConfigInvalid, error.Code);
        Assert.Equal(Stages.Configuration, error.Stage);
        Assert.Empty(result.Rooms);
        Assert.Empty(result.Anomalies);
        Assert.Null(result.Summary.Source);
        Assert.Equal(["config", "close", "export"], pipeline.Calls);
        Assert.Same(result, pipeline.Exported);
        var failure = Assert.Single(pipeline.Logs, l => l.Level == LogLevel.Error && l.EventCode == LogEventCodes.ExtractionFailed);
        Assert.Equal(ErrorCodes.ConfigInvalid, failure.Data?["errorCode"]);
        Assert.Equal(Stages.Configuration, failure.Stage);
        Assert.NotNull(failure.Exception);
        Assert.Equal(1, pipeline.Flushes);
    }

    [Fact]
    public void Run_RoomException_MarksRoomExtractionErrorAndContinues()
    {
        var pipeline = new Pipeline
        {
            ReadRoom = room => room.ElementId == 412345
                ? throw new InvalidOperationException("paramètre illisible")
                : new RoomReadResult(new RoomDto { ElementId = "415002", UniqueId = room.UniqueId, Name = "Réserve", VolumeStatus = VolumeStatus.NotComputed }, []),
        };

        var result = pipeline.CreateService().Run(Pipeline.Request);

        Assert.Equal(ExtractionStatus.SucceededWithWarnings, result.Summary.Status);
        Assert.Empty(result.Summary.Errors);
        Assert.Equal(new RoomCounts(2, 2, 1), result.Summary.Counts);
        var failed = result.Rooms[0];
        Assert.Equal("412345", failed.ElementId);
        Assert.Equal("u-412345", failed.UniqueId);
        Assert.Null(failed.Name);
        Assert.Null(failed.VolumeM3);
        Assert.Equal(VolumeStatus.NotAvailable, failed.VolumeStatus);
        Assert.Equal([QualityFlagCodes.ExtractionError], failed.QualityFlags);
        Assert.Equal("Réserve", result.Rooms[1].Name);
        var anomaly = Assert.Single(result.Anomalies, a => a.Code == QualityFlagCodes.ExtractionError);
        Assert.Equal(AnomalySeverity.Error, anomaly.Severity);
        Assert.Equal(AnomalyScope.Room, anomaly.Scope);
        Assert.Equal("412345", anomaly.ElementId);
        var log = Assert.Single(pipeline.Logs, l => l.EventCode == LogEventCodes.RoomExtractionFailed);
        Assert.Equal("412345", log.RoomElementId);
        Assert.IsType<InvalidOperationException>(log.Exception);
    }

    [Fact]
    public void Run_RoomException_KeepsQualityStatusWhenAlreadyWarning()
    {
        var pipeline = new Pipeline
        {
            QualityStatus = ExtractionStatus.SucceededWithWarnings,
            ReadRoom = _ => throw new InvalidOperationException("boom"),
        };

        var result = pipeline.CreateService().Run(Pipeline.Request);

        Assert.Equal(ExtractionStatus.SucceededWithWarnings, result.Summary.Status);
        Assert.All(result.Rooms, r => Assert.Equal([QualityFlagCodes.ExtractionError], r.QualityFlags));
    }

    [Fact]
    public void Run_UnexpectedException_FailsWithUnexpectedErrorAndCurrentStage()
    {
        var pipeline = new Pipeline { CollectException = new InvalidOperationException("boom") };

        var result = pipeline.CreateService().Run(Pipeline.Request);

        var error = Assert.Single(result.Summary.Errors);
        Assert.Equal(ErrorCodes.UnexpectedError, error.Code);
        Assert.Equal(Stages.Collection, error.Stage);
        Assert.Equal(ExtractionStatus.Failed, result.Summary.Status);
        Assert.Empty(result.Rooms);
        Assert.Equal(0, result.Summary.Counts.RoomsExported);
    }

    [Theory]
    [InlineData(ExtractionStatus.Succeeded, "succeeded")]
    [InlineData(ExtractionStatus.SucceededWithWarnings, "succeededWithWarnings")]
    [InlineData(ExtractionStatus.Failed, "failed")]
    public void Run_SummaryLogsStatusInResultFormat(ExtractionStatus status, string expected)
    {
        var pipeline = new Pipeline
        {
            QualityStatus = status == ExtractionStatus.Failed ? ExtractionStatus.Succeeded : status,
            ExportException = status == ExtractionStatus.Failed ? new IOException("disque plein") : null,
        };

        pipeline.CreateService().Run(Pipeline.Request);

        var summary = Assert.Single(pipeline.Logs, l => l.EventCode == LogEventCodes.ExtractionSummary);
        Assert.Equal(expected, summary.Data?["status"]);
    }

    [Fact]
    public void Run_ExportFailure_ReturnsFailedWithExportFailed()
    {
        var pipeline = new Pipeline { ExportException = new IOException("disque plein") };

        var result = pipeline.CreateService().Run(Pipeline.Request);

        Assert.Equal(ExtractionStatus.Failed, result.Summary.Status);
        Assert.Equal(ErrorCodes.ExportFailed, Assert.Single(result.Summary.Errors).Code);
        Assert.Empty(result.Rooms);
        Assert.Equal(1, pipeline.Flushes);
    }

    [Fact]
    public void ExtractorVersion_HasNoBuildMetadata() =>
        Assert.DoesNotContain("+", RoomExtractionService.ExtractorVersion);

    [Fact]
    public void Run_DocumentOpenFailure_ClosesThenExportsFailedResult()
    {
        var pipeline = new Pipeline { OpenException = new ExtractionFailedException(ErrorCodes.RevitVersionIncompatible, Stages.Document, "RVT 2027.") };

        var result = pipeline.CreateService().Run(Pipeline.Request);

        Assert.Equal(["config", "document", "close", "export"], pipeline.Calls);
        Assert.Equal(ErrorCodes.RevitVersionIncompatible, Assert.Single(result.Summary.Errors).Code);
    }

    [Fact]
    public void Run_CloseFailure_IsLoggedWithoutChangingResult()
    {
        var pipeline = new Pipeline { CloseException = new InvalidOperationException("close") };

        var result = pipeline.CreateService().Run(Pipeline.Request);

        Assert.Equal(ExtractionStatus.Succeeded, result.Summary.Status);
        Assert.Contains(pipeline.Logs, l => l.EventCode == LogEventCodes.DocumentCloseFailed && l.Level == LogLevel.Warning);
    }

    [Fact]
    public void Run_UnknownPhase_FailsWithConfigInvalidInsteadOfEmptySuccess()
    {
        var pipeline = new Pipeline
        {
            CollectException = new ExtractionFailedException(ErrorCodes.ConfigInvalid, Stages.Collection, "phaseIds : phase(s) absente(s) du document : 99."),
        };

        var result = pipeline.CreateService().Run(Pipeline.Request);

        Assert.Equal(ExtractionStatus.Failed, result.Summary.Status);
        var error = Assert.Single(result.Summary.Errors);
        Assert.Equal(ErrorCodes.ConfigInvalid, error.Code);
        Assert.Equal(Stages.Collection, error.Stage);
        Assert.Contains("99", error.Message);
        Assert.Empty(result.Rooms);
        Assert.Equal(["config", "document", "volume", "collect", "close", "export"], pipeline.Calls);
    }

    [Fact]
    public void Run_ClosesDocumentAndFlushes_EvenWhenFailureHandlingThrows()
    {
        var pipeline = new Pipeline { CollectException = new InvalidOperationException("boom"), ThrowOnErrorLog = true };

        Assert.ThrowsAny<Exception>(() => pipeline.CreateService().Run(Pipeline.Request));

        Assert.Contains("close", pipeline.Calls);
        Assert.Equal(1, pipeline.Flushes);
    }

    [Fact]
    public void Run_RoomsAreResortedByNumericElementIdAfterQualityCheck()
    {
        var pipeline = new Pipeline
        {
            Rooms = [new(9, "u-9"), new(100, "u-100"), new(10, "u-10"), new(1_000_000_000_000, "u-big")],
            ReverseQualityOrder = true,
        };

        var result = pipeline.CreateService().Run(Pipeline.Request);

        Assert.Equal(["9", "10", "100", "1000000000000"], result.Rooms.Select(r => r.ElementId));
    }

    [Fact]
    public void Collection_ReceivesValidatedPhaseElementIds()
    {
        var pipeline = new Pipeline
        {
            LoadSettings = () => new ExtractionSettings { SchemaVersion = "1.0", PhaseIds = ["0086961"], PhaseElementIds = [86961L] },
        };

        pipeline.CreateService().Run(Pipeline.Request);

        Assert.Equal([86961L], pipeline.CollectedPhaseIds);
    }
}
