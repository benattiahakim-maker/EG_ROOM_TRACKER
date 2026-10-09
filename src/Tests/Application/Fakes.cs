using EgRoomTracker.Application;
using EgRoomTracker.Application.Configuration;
using EgRoomTracker.Application.Quality;
using EgRoomTracker.Application.Revit;
using EgRoomTracker.Contracts.Codes;
using EgRoomTracker.Contracts.Configuration;
using EgRoomTracker.Contracts.Results;
using EgRoomTracker.Export;
using EgRoomTracker.Logging;

namespace EgRoomTracker.Tests.Application;

/// <summary>Doublures des services, enregistrant l'ordre des appels.</summary>
internal sealed class Pipeline
{
    public List<string> Calls { get; } = [];
    public List<LogEvent> Logs { get; } = [];
    public ExtractionResult? Exported { get; private set; }
    public int Flushes { get; private set; }

    public Func<ExtractionSettings> LoadSettings { get; set; } = () => new ExtractionSettings
    {
        SchemaVersion = "1.0",
        SharedParameters = [new SharedParameterMapping("codeLocal", Guid.Parse("3f2b8c1e-6d4a-4b7e-9a51-2c0d8e7f4a10"))],
    };

    public IReadOnlyList<CollectedRoom> Rooms { get; set; } = [new(415002, "u-415002"), new(412345, "u-412345")];
    public Func<CollectedRoom, RoomReadResult>? ReadRoom { get; set; }
    public Exception? CollectException { get; set; }
    public Exception? OpenException { get; set; }
    public Exception? CloseException { get; set; }
    public bool ThrowOnErrorLog { get; set; }
    public bool ReverseQualityOrder { get; set; }
    public ExtractionStatus QualityStatus { get; set; } = ExtractionStatus.Succeeded;
    public Exception? ExportException { get; set; }
    public IReadOnlyList<AnomalyDto> SharedParameterAnomalies { get; set; } = [];

    public static ExtractionRequest Request => new()
    {
        CorrelationId = "wi-test",
        InputDirectory = "/in",
        SettingsPath = "/in/settings.json",
        OutputPath = "/out/result.json",
        RevitVersion = "2026",
        RevitBuild = "26.0.4.409",
        ApsEngine = "Autodesk.Revit+2026",
    };

    public RoomExtractionService CreateService()
    {
        var services = new Services(this);
        return new RoomExtractionService(services, services, services, services, services, services, services, services);
    }

    private sealed class Services(Pipeline p) :
        IInputConfigurationService, IRevitDocumentService, IVolumeCalculationService, IRoomCollectionService,
        IRoomParameterReader, IQualityCheckService, IExportService, ILogService
    {
        public string CorrelationId => "wi-test";

        public ExtractionSettings Load(string inputDirectory, string settingsPath)
        {
            p.Calls.Add("config");
            return p.LoadSettings();
        }

        public DocumentDescriptor Open(ExtractionSettings settings, string inputDirectory)
        {
            p.Calls.Add("document");
            if (p.OpenException is not null)
            {
                throw p.OpenException;
            }

            return new DocumentDescriptor(new SourceInfo("A.rvt", "Projet/A.rvt", false), []);
        }

        public void Close()
        {
            p.Calls.Add("close");
            if (p.CloseException is not null)
            {
                throw p.CloseException;
            }
        }

        public VolumeComputationState Prepare(VolumeMode mode)
        {
            p.Calls.Add("volume");
            return new VolumeComputationState(mode, VolumesComputed: false);
        }

        public IReadOnlyList<CollectedRoom> CollectRooms(IReadOnlyList<string>? phaseIds)
        {
            p.Calls.Add("collect");
            return p.CollectException is null ? p.Rooms : throw p.CollectException;
        }

        public RoomReadResult Read(CollectedRoom room, IReadOnlyList<SharedParameterMapping> sharedParameters, VolumeComputationState volumeState)
        {
            p.Calls.Add($"read:{room.ElementId}");
            return p.ReadRoom?.Invoke(room) ?? new RoomReadResult(
                new RoomDto { ElementId = room.ElementId.ToString(), UniqueId = room.UniqueId, VolumeStatus = VolumeStatus.NotComputed },
                []);
        }

        public IReadOnlyList<AnomalyDto> CheckSharedParameters(IReadOnlyList<SharedParameterMapping> sharedParameters) =>
            p.SharedParameterAnomalies;

        public QualityReport Evaluate(IReadOnlyList<RoomDto> rooms, IReadOnlyList<AnomalyDto> anomalies, VolumeComputationState volumeState)
        {
            p.Calls.Add("quality");
            var documentAnomaly = new AnomalyDto
            {
                Code = DocumentAnomalyCodes.VolumeNotComputed,
                Severity = AnomalySeverity.Info,
                Scope = AnomalyScope.Document,
                Message = "Volumes non calculés.",
            };
            IReadOnlyList<RoomDto> ordered = p.ReverseQualityOrder ? rooms.Reverse().ToList() : rooms;
            return new QualityReport(ordered, [.. anomalies, documentAnomaly], p.QualityStatus);
        }

        public void Write(ExtractionResult result, string outputPath)
        {
            p.Calls.Add("export");
            if (p.ExportException is not null)
            {
                throw p.ExportException;
            }

            p.Exported = result;
        }

        public void Log(LogEvent logEvent)
        {
            p.Logs.Add(logEvent);
            if (p.ThrowOnErrorLog && logEvent.Level == LogLevel.Error)
            {
                throw new InvalidOperationException("journal indisponible");
            }
        }

        public void Flush() => p.Flushes++;
    }
}
