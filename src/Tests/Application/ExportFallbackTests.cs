using System.Text;
using EgRoomTracker.Application;
using EgRoomTracker.Contracts.Codes;
using EgRoomTracker.Contracts.Results;
using EgRoomTracker.Export;
using EgRoomTracker.Logging;
using EgRoomTracker.Tests.Support;

namespace EgRoomTracker.Tests.Application;

/// <summary>
/// Repli après <c>EXPORT_FAILED</c> (EGR-13, décision du chef de projet) : une seule nouvelle tentative d'écrire un
/// <c>result.json</c> minimal (statut <c>failed</c>, erreurs, pièces et anomalies vides), puis le log console seul.
/// </summary>
public class ExportFallbackTests
{
    [Fact]
    public void ExportSuccess_DoesNotRetry()
    {
        var pipeline = new TestPipeline { RoomIds = [412345] };

        pipeline.Run();

        Assert.Single(pipeline.ExportAttempts);
    }

    [Fact]
    public void FirstExportFails_MinimalFailedResultIsWrittenOnce()
    {
        var pipeline = new TestPipeline
        {
            RoomIds = [412398, 412345],
            ExportFailure = attempt => attempt == 0 ? new IOException("disque plein") : null,
        };

        var result = pipeline.Run();

        Assert.Equal(2, pipeline.ExportAttempts.Count);
        var minimal = pipeline.ExportAttempts[1];
        Assert.Same(minimal, pipeline.Exported);
        Assert.Equal(result, minimal);
        Assert.Equal(ExtractionStatus.Failed, minimal.Summary.Status);
        Assert.Equal(ErrorCodes.ExportFailed, Assert.Single(minimal.Summary.Errors).Code);
        Assert.Empty(minimal.Rooms);
        Assert.Empty(minimal.Anomalies);
        Assert.Equal(new RoomCounts(2, 0, 0), minimal.Summary.Counts);
        Assert.NotNull(minimal.Summary.Source);
        Assert.Contains(pipeline.Logs, l => l.EventCode == LogEventCodes.ExportCompleted && l.Message.Contains("minimal", StringComparison.Ordinal));
    }

    [Fact]
    public void MinimalFailedResult_IsSchemaValid_WithTheProductionExporter()
    {
        // Cas typique : la première sérialisation échoue sur une valeur non représentable (NaN) ; le résultat minimal
        // n'a plus de pièces et peut être écrit.
        var pipeline = new TestPipeline
        {
            RoomIds = [412345],
            ExportFailure = attempt => attempt == 0 ? new NotSupportedException("NaN") : null,
        };

        pipeline.Run();

        var json = Encoding.UTF8.GetString(ResultExportService.Serialize(pipeline.ExportAttempts[1]));
        ResultSchema.AssertValid(json);
    }

    [Fact]
    public void BothExportsFail_NoThirdAttempt_ResultFailed_AndConsoleLogOnly()
    {
        var pipeline = new TestPipeline
        {
            RoomIds = [412345],
            ExportException = new IOException("disque plein"),
        };

        var result = pipeline.Run();

        Assert.Equal(2, pipeline.ExportAttempts.Count);
        Assert.Null(pipeline.Exported);
        Assert.Equal(ExtractionStatus.Failed, result.Summary.Status);
        Assert.Equal(ErrorCodes.ExportFailed, Assert.Single(result.Summary.Errors).Code);
        Assert.Empty(result.Rooms);
        Assert.Contains(pipeline.Logs, l =>
            l.Level == LogLevel.Error && l.Data is { } d && d.TryGetValue("fallback", out var f) && f is true);
        Assert.DoesNotContain(pipeline.Logs, l => l.EventCode == LogEventCodes.ExportCompleted);
        Assert.Contains(pipeline.Logs, l => l.EventCode == LogEventCodes.ExtractionSummary);
    }

    [Fact]
    public void EarlierGlobalError_IsKept_BeforeExportFailed()
    {
        var pipeline = new TestPipeline
        {
            LoadSettings = () => throw new ExtractionFailedException(ErrorCodes.ConfigInvalid, Stages.Configuration, "settings.json : JSON invalide."),
            ExportFailure = attempt => attempt == 0 ? new IOException("disque plein") : null,
        };

        pipeline.Run();

        var minimal = pipeline.ExportAttempts[1];
        Assert.Equal([ErrorCodes.ConfigInvalid, ErrorCodes.ExportFailed], minimal.Summary.Errors.Select(e => e.Code));
        ResultSchema.AssertValid(Encoding.UTF8.GetString(ResultExportService.Serialize(minimal)));
    }
}
