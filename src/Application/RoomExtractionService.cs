using System.Diagnostics;
using System.Reflection;
using EgRoomTracker.Application.Configuration;
using EgRoomTracker.Application.Quality;
using EgRoomTracker.Application.Revit;
using EgRoomTracker.Contracts.Codes;
using EgRoomTracker.Contracts.Configuration;
using EgRoomTracker.Contracts.Results;
using EgRoomTracker.Export;
using EgRoomTracker.Logging;

namespace EgRoomTracker.Application;

/// <summary>
/// Orchestration des étapes et production du bilan (spécifications, section 4).
/// Exécution séquentielle sur le thread appelant : aucun Task.Run ni accès parallèle aux objets Revit.
/// </summary>
public sealed class RoomExtractionService
{
    private readonly IInputConfigurationService _configuration;
    private readonly IRevitDocumentService _document;
    private readonly IVolumeCalculationService _volume;
    private readonly IRoomCollectionService _collection;
    private readonly IRoomParameterReader _reader;
    private readonly IQualityCheckService _quality;
    private readonly IExportService _export;
    private readonly ILogService _log;
    private readonly TimeProvider _time;

    public RoomExtractionService(
        IInputConfigurationService configuration,
        IRevitDocumentService document,
        IVolumeCalculationService volume,
        IRoomCollectionService collection,
        IRoomParameterReader reader,
        IQualityCheckService quality,
        IExportService export,
        ILogService log,
        TimeProvider? time = null)
    {
        _configuration = configuration;
        _document = document;
        _volume = volume;
        _collection = collection;
        _reader = reader;
        _quality = quality;
        _export = export;
        _log = log;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Version de l'extracteur, issue de l'assembly (sans métadonnées de build).</summary>
    public static string ExtractorVersion { get; } = ReadExtractorVersion();

    /// <summary>
    /// Exécute l'extraction et écrit <c>result.json</c>. Ne lève pas d'exception : un échec global
    /// est porté par le statut <c>failed</c> du résultat retourné.
    /// </summary>
    public ExtractionResult Run(ExtractionRequest request)
    {
        var startedAt = _time.GetUtcNow();
        var stopwatch = Stopwatch.StartNew();
        var stage = Stages.Startup;
        ExtractionSettings? settings = null;
        DocumentDescriptor? document = null;
        var roomsCollected = 0;
        ExtractionResult result;

        try
        {
            _log.Info(LogEventCodes.ExtractionStarted, stage, $"Extraction démarrée (Revit {request.RevitVersion}, extracteur {ExtractorVersion}).");

            stage = Stages.Configuration;
            settings = _configuration.Load(request.InputDirectory, request.SettingsPath);
            _log.Info(LogEventCodes.ConfigurationValidated, stage, "Configuration validée.");

            stage = Stages.Document;
            document = _document.Open(settings, request.InputDirectory);
            _log.Log(new LogEvent(LogLevel.Info, LogEventCodes.DocumentAccessible, stage, "Document accessible.")
            {
                DocumentPath = document.Source.RelativePath,
            });

            var volumeState = _volume.Prepare(settings.VolumeMode);

            stage = Stages.Collection;
            var roomIds = _collection.CollectRoomIds(settings.PhaseIds);
            roomsCollected = roomIds.Count;
            var rooms = new List<RoomDto>(roomIds.Count);
            var anomalies = new List<AnomalyDto>(document.Anomalies);
            foreach (var roomId in roomIds.OrderBy(id => id))
            {
                var read = _reader.Read(roomId, settings.SharedParameters, volumeState);
                rooms.Add(read.Room);
                anomalies.AddRange(read.Anomalies);
            }

            _log.Log(new LogEvent(LogLevel.Info, LogEventCodes.CollectionCompleted, stage, $"{rooms.Count} pièce(s) collectée(s).")
            {
                Data = new Dictionary<string, object?> { ["roomsCollected"] = roomsCollected },
            });

            stage = Stages.Quality;
            var report = _quality.Evaluate(rooms, anomalies, volumeState);
            if (report.Anomalies.Count > 0)
            {
                LogAnomalySummary(report.Anomalies);
            }

            result = BuildResult(request, settings, document, startedAt, report.Status, report.Rooms, report.Anomalies, roomsCollected, []);
        }
        catch (ExtractionFailedException ex)
        {
            _log.Error(LogEventCodes.ExtractionFailed, ex.Error.Stage, $"{ex.Error.Code} : {ex.Error.Message}", ex);
            result = BuildResult(request, settings, document, startedAt, ExtractionStatus.Failed, [], [], roomsCollected, [ex.Error]);
        }
        catch (Exception ex)
        {
            var error = new GlobalError(ErrorCodes.UnexpectedError, stage, "Erreur inattendue hors du périmètre d'une pièce.");
            _log.Error(LogEventCodes.ExtractionFailed, stage, $"{error.Code} : {ex.Message}", ex);
            result = BuildResult(request, settings, document, startedAt, ExtractionStatus.Failed, [], [], roomsCollected, [error]);
        }

        try
        {
            result = Export(request, result);
            LogSummary(result, stopwatch.Elapsed);
        }
        finally
        {
            _log.Flush();
        }

        return result;
    }

    private ExtractionResult Export(ExtractionRequest request, ExtractionResult result)
    {
        try
        {
            _export.Write(result, request.OutputPath);
            _log.Info(LogEventCodes.ExportCompleted, Stages.Export, "result.json écrit.");
            return result;
        }
        catch (Exception ex)
        {
            var error = new GlobalError(ErrorCodes.ExportFailed, Stages.Export, "Écriture ou finalisation de result.json impossible.");
            _log.Error(LogEventCodes.ExtractionFailed, Stages.Export, $"{error.Code} : {ex.Message}", ex);
            return result with
            {
                Rooms = [],
                Anomalies = [],
                Summary = result.Summary with
                {
                    Status = ExtractionStatus.Failed,
                    Errors = [.. result.Summary.Errors, error],
                    Counts = result.Summary.Counts with { RoomsExported = 0, RoomsWithAnomalies = 0 },
                },
            };
        }
    }

    private ExtractionResult BuildResult(
        ExtractionRequest request,
        ExtractionSettings? settings,
        DocumentDescriptor? document,
        DateTimeOffset startedAt,
        ExtractionStatus status,
        IReadOnlyList<RoomDto> rooms,
        IReadOnlyList<AnomalyDto> anomalies,
        int roomsCollected,
        IReadOnlyList<GlobalError> errors)
    {
        var options = new AppliedOptions(
            settings?.PhaseIds,
            settings?.VolumeMode ?? VolumeMode.Preserve,
            settings?.SharedParameters.Select(p => p.Key).ToList() ?? []);

        return new ExtractionResult
        {
            Summary = new ExtractionSummary
            {
                CorrelationId = request.CorrelationId,
                ExtractorVersion = ExtractorVersion,
                Revit = new RevitInfo(request.RevitVersion, request.RevitBuild),
                ApsEngine = request.ApsEngine,
                Source = document?.Source,
                StartedAtUtc = startedAt,
                CompletedAtUtc = _time.GetUtcNow(),
                Options = options,
                Counts = new RoomCounts(roomsCollected, rooms.Count, rooms.Count(r => r.QualityFlags.Count > 0)),
                Status = status,
                Errors = errors,
            },
            Rooms = rooms,
            Anomalies = anomalies,
        };
    }

    /// <summary>Compteur agrégé par code : pas un message par pièce (section 5).</summary>
    private void LogAnomalySummary(IReadOnlyList<AnomalyDto> anomalies)
    {
        var byCode = anomalies
            .GroupBy(a => a.Code, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (object?)g.Count(), StringComparer.Ordinal);
        var level = anomalies.Any(a => a.Severity != AnomalySeverity.Info) ? LogLevel.Warning : LogLevel.Info;
        _log.Log(new LogEvent(level, LogEventCodes.AnomaliesDetected, Stages.Quality, $"{anomalies.Count} anomalie(s) ; détail dans result.json.")
        {
            Data = byCode,
        });
    }

    private void LogSummary(ExtractionResult result, TimeSpan duration)
    {
        var summary = result.Summary;
        var level = summary.Status switch
        {
            ExtractionStatus.Failed => LogLevel.Error,
            ExtractionStatus.SucceededWithWarnings => LogLevel.Warning,
            _ => LogLevel.Info,
        };
        _log.Log(new LogEvent(level, LogEventCodes.ExtractionSummary, Stages.Completion, $"Extraction terminée : {summary.Status}.")
        {
            DocumentPath = summary.Source?.RelativePath,
            Duration = duration,
            Data = new Dictionary<string, object?>
            {
                ["status"] = summary.Status.ToString(),
                ["roomsCollected"] = summary.Counts.RoomsCollected,
                ["roomsExported"] = summary.Counts.RoomsExported,
                ["roomsWithAnomalies"] = summary.Counts.RoomsWithAnomalies,
                ["errors"] = summary.Errors.Select(e => e.Code).ToArray(),
            },
        });
    }

    private static string ReadExtractorVersion()
    {
        var assembly = typeof(RoomExtractionService).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "0.0.0";
        var plus = version.IndexOf('+');
        return plus >= 0 ? version[..plus] : version;
    }
}
