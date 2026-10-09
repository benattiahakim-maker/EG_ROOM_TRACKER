using EgRoomTracker.Application;
using EgRoomTracker.Contracts.Codes;
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
            ["config", "document", "volume", "collect", "read:412345", "read:415002", "quality", "export"],
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
        Assert.Equal(["config", "export"], pipeline.Calls);
        Assert.Same(result, pipeline.Exported);
        Assert.Contains(pipeline.Logs, l => l.Level == LogLevel.Error && l.EventCode == LogEventCodes.ExtractionFailed);
        Assert.Equal(1, pipeline.Flushes);
    }

    [Fact]
    public void Run_UnexpectedException_FailsWithUnexpectedErrorAndCurrentStage()
    {
        var pipeline = new Pipeline { ReadRoom = _ => throw new InvalidOperationException("boom") };

        var result = pipeline.CreateService().Run(Pipeline.Request);

        var error = Assert.Single(result.Summary.Errors);
        Assert.Equal(ErrorCodes.UnexpectedError, error.Code);
        Assert.Equal(Stages.Collection, error.Stage);
        Assert.Equal(ExtractionStatus.Failed, result.Summary.Status);
        Assert.Equal(2, result.Summary.Counts.RoomsCollected);
        Assert.Equal(0, result.Summary.Counts.RoomsExported);
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
}
