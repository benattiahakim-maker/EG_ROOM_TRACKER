using EgRoomTracker.Application;
using EgRoomTracker.Application.Configuration;
using EgRoomTracker.Application.Quality;
using EgRoomTracker.Application.Revit;
using EgRoomTracker.Contracts.Configuration;
using EgRoomTracker.Contracts.Results;
using EgRoomTracker.Export;
using EgRoomTracker.Logging;

namespace EgRoomTracker.Tests.Support;

/// <summary>
/// Doublures configurables de tous les services de <see cref="RoomExtractionService"/> (sans Revit),
/// avec horloge figée pour des sorties reproductibles.
/// </summary>
internal sealed class TestPipeline
{
    public Func<ExtractionSettings> LoadSettings { get; set; } = () => new ExtractionSettings
    {
        SchemaVersion = "1.0",
        MainModelPath = "Projet/EG_Batiment_A.rvt",
        SharedParameters = [new SharedParameterMapping("codeLocal", Rooms.CodeLocalGuid)],
    };

    public Func<DocumentDescriptor> OpenDocument { get; set; } = () =>
        new DocumentDescriptor(new SourceInfo("EG_Batiment_A.rvt", "Projet/EG_Batiment_A.rvt", false), []);

    public bool VolumesComputed { get; set; }

    public IReadOnlyList<long> RoomIds { get; set; } = [];

    /// <summary>Lecture d'une pièce ; par défaut, une pièce complète sans anomalie.</summary>
    public Func<long, RoomReadResult> ReadRoom { get; set; } = id => new RoomReadResult(Rooms.Complete(id), []);

    /// <summary>Contrôle qualité ; par défaut, renvoie les entrées et le statut <c>succeeded</c>.</summary>
    public Func<IReadOnlyList<RoomDto>, IReadOnlyList<AnomalyDto>, VolumeComputationState, QualityReport> Evaluate { get; set; } =
        (rooms, anomalies, _) => new QualityReport(rooms, anomalies, ExtractionStatus.Succeeded);

    public Exception? ExportException { get; set; }

    public ExtractionResult? Exported { get; private set; }

    public ExtractionSettings? LoadedSettings { get; private set; }

    public IReadOnlyList<RoomDto>? QualityInputRooms { get; private set; }

    public IReadOnlyList<AnomalyDto>? QualityInputAnomalies { get; private set; }

    public VolumeComputationState? QualityVolumeState { get; private set; }

    public List<LogEvent> Logs { get; } = [];

    public static ExtractionRequest Request { get; } = new()
    {
        CorrelationId = "wi-7f3c2a9e5b1d4c08",
        InputDirectory = "/work/input",
        SettingsPath = "/work/settings.json",
        OutputPath = "/work/result.json",
        RevitVersion = "2026",
        RevitBuild = "26.0.4.409",
        ApsEngine = "Autodesk.Revit+2026",
    };

    public ExtractionResult Run(ExtractionRequest? request = null)
    {
        var s = new Services(this);
        return new RoomExtractionService(s, s, s, s, s, s, s, s, new FixedTimeProvider()).Run(request ?? Request);
    }

    private sealed class Services(TestPipeline p) :
        IInputConfigurationService, IRevitDocumentService, IVolumeCalculationService, IRoomCollectionService,
        IRoomParameterReader, IQualityCheckService, IExportService, ILogService
    {
        public string CorrelationId => Request.CorrelationId;

        public ExtractionSettings Load(string inputDirectory, string settingsPath) => p.LoadedSettings = p.LoadSettings();

        public DocumentDescriptor Open(ExtractionSettings settings, string inputDirectory) => p.OpenDocument();

        public VolumeComputationState Prepare(VolumeMode mode) => new(mode, p.VolumesComputed);

        public IReadOnlyList<long> CollectRoomIds(IReadOnlyList<string>? phaseIds) => p.RoomIds;

        public RoomReadResult Read(long roomElementId, IReadOnlyList<SharedParameterMapping> sharedParameters, VolumeComputationState volumeState) =>
            p.ReadRoom(roomElementId);

        public QualityReport Evaluate(IReadOnlyList<RoomDto> rooms, IReadOnlyList<AnomalyDto> anomalies, VolumeComputationState volumeState)
        {
            p.QualityInputRooms = rooms;
            p.QualityInputAnomalies = anomalies;
            p.QualityVolumeState = volumeState;
            return p.Evaluate(rooms, anomalies, volumeState);
        }

        public void Write(ExtractionResult result, string outputPath)
        {
            if (p.ExportException is not null)
            {
                throw p.ExportException;
            }

            p.Exported = result;
        }

        public void Log(LogEvent logEvent) => p.Logs.Add(logEvent);

        public void Flush()
        {
        }
    }
}
