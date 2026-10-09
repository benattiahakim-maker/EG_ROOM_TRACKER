using EgRoomTracker.Application;
using EgRoomTracker.Application.Quality;
using EgRoomTracker.Application.Revit;
using EgRoomTracker.Contracts.Codes;
using EgRoomTracker.Contracts.Configuration;
using EgRoomTracker.Contracts.Results;
using EgRoomTracker.Tests.Support;

namespace EgRoomTracker.Tests.Application;

/// <summary>
/// Cohérence du bilan produit par <see cref="RoomExtractionService"/> à partir des indicateurs qualité
/// (sections 6 et 9) : transmission des anomalies, statut, compteurs, conservation de toutes les pièces.
/// </summary>
public class ExtractionPipelineTests
{
    [Fact]
    public void RoomLevelAnomaliesFromReader_AreForwardedToQualityCheck()
    {
        var pipeline = new TestPipeline
        {
            RoomIds = [412345, 412398],
            OpenDocument = () => new DocumentDescriptor(
                new SourceInfo("A.rvt", "A.rvt", null),
                [new AnomalyDto
                {
                    Code = DocumentAnomalyCodes.LinkMissing,
                    Severity = AnomalySeverity.Warning,
                    Scope = AnomalyScope.Document,
                    Message = "Lien Structure.rvt absent du ZIP.",
                }]),
            ReadRoom = id => id == 412398
                ? new RoomReadResult(
                    Rooms.Complete(id) with { Level = null, QualityFlags = [QualityFlagCodes.LevelNotFound] },
                    [Rooms.RoomAnomaly(QualityFlagCodes.LevelNotFound, "412398")])
                : new RoomReadResult(Rooms.Complete(id), []),
        };

        pipeline.Run();

        Assert.Equal(["412345", "412398"], pipeline.QualityInputRooms!.Select(r => r.ElementId));
        Assert.Equal(
            [DocumentAnomalyCodes.LinkMissing, QualityFlagCodes.LevelNotFound],
            pipeline.QualityInputAnomalies!.Select(a => a.Code));
    }

    [Fact]
    public void ExtractionErrorOnOneRoom_KeepsAllRooms_AndRoomsExportedEqualsRoomsCollected()
    {
        // Section 6 : erreur limitée à une pièce => identité exportée, EXTRACTION_ERROR, on continue.
        var pipeline = new TestPipeline
        {
            RoomIds = [3, 1, 2],
            ReadRoom = id => id == 2
                ? new RoomReadResult(
                    new RoomDto
                    {
                        ElementId = "2",
                        UniqueId = "u-2",
                        VolumeStatus = VolumeStatus.NotAvailable,
                        QualityFlags = [QualityFlagCodes.ExtractionError],
                    },
                    [Rooms.RoomAnomaly(QualityFlagCodes.ExtractionError, "2", AnomalySeverity.Error)])
                : new RoomReadResult(Rooms.Complete(id), []),
            Evaluate = (rooms, anomalies, _) => new QualityReport(rooms, anomalies, ExtractionStatus.SucceededWithWarnings),
        };

        var result = pipeline.Run();

        Assert.Equal(ExtractionStatus.SucceededWithWarnings, result.Summary.Status);
        Assert.Equal(["1", "2", "3"], result.Rooms.Select(r => r.ElementId));
        Assert.Equal(result.Summary.Counts.RoomsCollected, result.Summary.Counts.RoomsExported);
        Assert.Equal(1, result.Summary.Counts.RoomsWithAnomalies);
        Assert.Empty(result.Summary.Errors);
        var broken = result.Rooms.Single(r => r.ElementId == "2");
        Assert.Null(broken.Name);
        Assert.Null(broken.AreaM2);
        Assert.Null(broken.VolumeM3);
        ResultSchema.AssertValid(ContractJson.Serialize(result));
    }

    [Theory]
    [InlineData(ExtractionStatus.Succeeded)]
    [InlineData(ExtractionStatus.SucceededWithWarnings)]
    public void Status_IsTakenFromQualityReport_WhenNoGlobalError(ExtractionStatus status)
    {
        var pipeline = new TestPipeline
        {
            RoomIds = [412345],
            Evaluate = (rooms, anomalies, _) => new QualityReport(rooms, anomalies, status),
        };

        Assert.Equal(status, pipeline.Run().Summary.Status);
    }

    [Fact]
    public void RoomsWithAnomalies_CountsRoomsHavingAtLeastOneFlag_NotAnomalies()
    {
        var flagged = new[] { QualityFlagCodes.Unplaced, QualityFlagCodes.DuplicateNumber };
        var pipeline = new TestPipeline
        {
            RoomIds = [1, 2, 3, 4],
            ReadRoom = id => new RoomReadResult(Rooms.Complete(id) with { QualityFlags = id <= 2 ? flagged : [] }, []),
            Evaluate = (rooms, anomalies, _) => new QualityReport(
                rooms,
                [.. rooms.Where(r => r.QualityFlags.Count > 0).SelectMany(r => r.QualityFlags.Select(f => Rooms.RoomAnomaly(f, r.ElementId)))],
                ExtractionStatus.SucceededWithWarnings),
        };

        var result = pipeline.Run();

        Assert.Equal(new RoomCounts(4, 4, 2), result.Summary.Counts);
        Assert.Equal(4, result.Anomalies.Count);
    }

    [Fact]
    public void QualityReportRooms_AreTheOnesExported()
    {
        // Les indicateurs posés par le contrôle qualité (ex. DUPLICATE_NUMBER) figurent dans le résultat.
        var pipeline = new TestPipeline
        {
            RoomIds = [412345, 415002],
            ReadRoom = id => new RoomReadResult(Rooms.Complete(id, "001"), []),
            Evaluate = (rooms, anomalies, _) => new QualityReport(
                [.. rooms.Select(r => r with { QualityFlags = [QualityFlagCodes.DuplicateNumber] })],
                [.. rooms.Select(r => Rooms.RoomAnomaly(QualityFlagCodes.DuplicateNumber, r.ElementId))],
                ExtractionStatus.SucceededWithWarnings),
        };

        var result = pipeline.Run();

        Assert.All(result.Rooms, r => Assert.Equal([QualityFlagCodes.DuplicateNumber], r.QualityFlags));
        Assert.Equal(2, result.Summary.Counts.RoomsWithAnomalies);
        ResultSchema.AssertValid(ContractJson.Serialize(result));
    }

    [Fact]
    public void VolumeState_IsPassedToQualityCheck()
    {
        var pipeline = new TestPipeline
        {
            RoomIds = [412345],
            VolumesComputed = false,
            LoadSettings = () => new ExtractionSettings { SchemaVersion = "1.0", VolumeMode = VolumeMode.Preserve },
        };

        pipeline.Run();

        Assert.Equal(new VolumeComputationState(VolumeMode.Preserve, false), pipeline.QualityVolumeState);
    }

    [Fact]
    public void NoRooms_IsASuccessWithZeroCounts()
    {
        var result = new TestPipeline { RoomIds = [] }.Run();

        Assert.Equal(ExtractionStatus.Succeeded, result.Summary.Status);
        Assert.Equal(new RoomCounts(0, 0, 0), result.Summary.Counts);
        Assert.Empty(result.Rooms);
        ResultSchema.AssertValid(ContractJson.Serialize(result));
    }

    [Fact]
    public void GlobalError_AfterCollection_DiscardsRoomsAndAnomalies()
    {
        var pipeline = new TestPipeline
        {
            RoomIds = [1, 2],
            Evaluate = (_, _, _) => throw new ExtractionFailedException(ErrorCodes.UnexpectedError, Stages.Quality, "Erreur."),
        };

        var result = pipeline.Run();

        Assert.Equal(ExtractionStatus.Failed, result.Summary.Status);
        Assert.Empty(result.Rooms);
        Assert.Empty(result.Anomalies);
        Assert.Equal(new RoomCounts(2, 0, 0), result.Summary.Counts);
        ResultSchema.AssertValid(ContractJson.Serialize(result));
    }

    [Fact(Skip = "Écart EGR-11/EGR-12 : RoomExtractionService trie les identifiants avant lecture mais ne retrie pas report.Rooms ; "
        + "un IQualityCheckService qui réordonne casserait le tri par elementId (section 2). À trancher : retri dans l'orchestration ou dans l'export.")]
    public void Rooms_StaySorted_EvenIfQualityCheckReordersThem()
    {
        var pipeline = new TestPipeline
        {
            RoomIds = [1, 2, 3],
            Evaluate = (rooms, anomalies, _) => new QualityReport([.. rooms.Reverse()], anomalies, ExtractionStatus.Succeeded),
        };

        Assert.Equal(["1", "2", "3"], pipeline.Run().Rooms.Select(r => r.ElementId));
    }
}
