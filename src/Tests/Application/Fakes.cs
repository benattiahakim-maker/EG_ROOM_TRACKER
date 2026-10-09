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

    public IReadOnlyList<long> RoomIds { get; set; } = [415002, 412345];
    public Func<long, RoomReadResult>? ReadRoom { get; set; }
    public Exception? ExportException { get; set; }

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
            return new DocumentDescriptor(new SourceInfo("A.rvt", "Projet/A.rvt", false), []);
        }

        public VolumeComputationState Prepare(VolumeMode mode)
        {
            p.Calls.Add("volume");
            return new VolumeComputationState(mode, VolumesComputed: false);
        }

        public IReadOnlyList<long> CollectRoomIds(IReadOnlyList<string>? phaseIds)
        {
            p.Calls.Add("collect");
            return p.RoomIds;
        }

        public RoomReadResult Read(long roomElementId, IReadOnlyList<SharedParameterMapping> sharedParameters, VolumeComputationState volumeState)
        {
            p.Calls.Add($"read:{roomElementId}");
            return p.ReadRoom?.Invoke(roomElementId) ?? new RoomReadResult(
                new RoomDto { ElementId = roomElementId.ToString(), UniqueId = $"u-{roomElementId}", VolumeStatus = VolumeStatus.NotComputed },
                []);
        }

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
            return new QualityReport(rooms, [.. anomalies, documentAnomaly], ExtractionStatus.Succeeded);
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

        public void Log(LogEvent logEvent) => p.Logs.Add(logEvent);

        public void Flush() => p.Flushes++;
    }
}
