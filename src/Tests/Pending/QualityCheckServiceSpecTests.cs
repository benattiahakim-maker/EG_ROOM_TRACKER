using EgRoomTracker.Application.Quality;
using EgRoomTracker.Application.Revit;
using EgRoomTracker.Contracts.Codes;
using EgRoomTracker.Contracts.Configuration;
using EgRoomTracker.Contracts.Results;
using EgRoomTracker.Tests.Support;

namespace EgRoomTracker.Tests.Pending;

/// <summary>
/// Spécification exécutable des indicateurs qualité et du statut (sections 3, 9.1 et 9.3), à activer avec EGR-11.
/// Hypothèse de répartition (à confirmer en EGR-11) : UNPLACED, MISSING_PARAMETER, LEVEL_NOT_FOUND et EXTRACTION_ERROR
/// sont détectés à la lecture (Revit) et arrivent en entrée ; DUPLICATE_NUMBER, ZERO_AREA, VOLUME_NOT_COMPUTED et le
/// statut sont calculés ici à partir des DTO.
/// </summary>
public class QualityCheckServiceSpecTests
{
    private const string Skip = PendingImplementations.Egr11;

    private static readonly VolumeComputationState Computed = new(VolumeMode.Compute, VolumesComputed: true);
    private static readonly VolumeComputationState NotComputed = new(VolumeMode.Preserve, VolumesComputed: false);

    private readonly IQualityCheckService _service = new QualityCheckService();

    [Fact(Skip = Skip)]
    public void CleanRooms_Succeed_WithoutFlags()
    {
        var rooms = new[] { Rooms.Complete(1, "001"), Rooms.Complete(2, "002") };

        var report = _service.Evaluate(rooms, [], Computed);

        Assert.Equal(ExtractionStatus.Succeeded, report.Status);
        Assert.Empty(report.Anomalies);
        Assert.All(report.Rooms, r => Assert.Empty(r.QualityFlags));
    }

    [Fact(Skip = Skip)]
    public void AllRooms_AreKept_InInputOrder()
    {
        var rooms = new[]
        {
            Rooms.Complete(1, "001"),
            Rooms.Complete(2, "001") with { AreaM2 = 0 },
            Rooms.Complete(3) with { QualityFlags = [QualityFlagCodes.Unplaced] },
        };

        var report = _service.Evaluate(rooms, [Rooms.RoomAnomaly(QualityFlagCodes.Unplaced, "3")], Computed);

        Assert.Equal(["1", "2", "3"], report.Rooms.Select(r => r.ElementId));
    }

    [Fact(Skip = Skip)]
    public void VolumeNotComputed_InPreserveMode_IsASingleInfoDocumentAnomaly_AndStillSucceeds()
    {
        var rooms = new[] { Rooms.Complete(1, "001"), Rooms.Complete(2, "002") };

        var report = _service.Evaluate(rooms, [], NotComputed);

        var anomaly = Assert.Single(report.Anomalies);
        Assert.Equal(DocumentAnomalyCodes.VolumeNotComputed, anomaly.Code);
        Assert.Equal(AnomalySeverity.Info, anomaly.Severity);
        Assert.Equal(AnomalyScope.Document, anomaly.Scope);
        Assert.Null(anomaly.ElementId);
        Assert.Equal(ExtractionStatus.Succeeded, report.Status);
    }

    [Fact(Skip = Skip)]
    public void DuplicateNumber_InSamePhase_FlagsEveryRoomWithOneAnomalyEach()
    {
        var rooms = new[] { Rooms.Complete(412345, "001"), Rooms.Complete(412398, "002"), Rooms.Complete(415002, "001") };

        var report = _service.Evaluate(rooms, [], Computed);

        Assert.Equal([QualityFlagCodes.DuplicateNumber], Room(report, "412345").QualityFlags);
        Assert.Empty(Room(report, "412398").QualityFlags);
        Assert.Equal([QualityFlagCodes.DuplicateNumber], Room(report, "415002").QualityFlags);
        var duplicates = report.Anomalies.Where(a => a.Code == QualityFlagCodes.DuplicateNumber).ToList();
        Assert.Equal(["412345", "415002"], duplicates.Select(a => a.ElementId).Order());
        Assert.All(duplicates, a => Assert.Equal((AnomalySeverity.Warning, AnomalyScope.Room), (a.Severity, a.Scope)));
        Assert.Equal(ExtractionStatus.SucceededWithWarnings, report.Status);
    }

    [Fact(Skip = Skip)]
    public void SameNumber_InDifferentPhases_IsNotADuplicate()
    {
        var rooms = new[] { Rooms.Complete(1, "001", Rooms.NewConstruction), Rooms.Complete(2, "001", Rooms.Existing) };

        var report = _service.Evaluate(rooms, [], Computed);

        Assert.All(report.Rooms, r => Assert.DoesNotContain(QualityFlagCodes.DuplicateNumber, r.QualityFlags));
        Assert.Equal(ExtractionStatus.Succeeded, report.Status);
    }

    [Fact(Skip = Skip)]
    public void NumbersDifferingOnlyByLeadingZeros_AreNotDuplicates()
    {
        var rooms = new[] { Rooms.Complete(1, "001"), Rooms.Complete(2, "1") };

        var report = _service.Evaluate(rooms, [], Computed);

        Assert.All(report.Rooms, r => Assert.Empty(r.QualityFlags));
    }

    [Theory(Skip = Skip)]
    [InlineData("")]
    [InlineData(null)]
    public void EmptyOrAbsentNumbers_AreNeverDuplicates(string? number)
    {
        // Décision du chef de projet : numéro vide -> "", absent -> null ; DUPLICATE_NUMBER ignore les numéros vides.
        var rooms = new[] { Rooms.Complete(1) with { Number = number }, Rooms.Complete(2) with { Number = number } };

        var report = _service.Evaluate(rooms, [], Computed);

        Assert.All(report.Rooms, r => Assert.DoesNotContain(QualityFlagCodes.DuplicateNumber, r.QualityFlags));
        Assert.Equal([number, number], report.Rooms.Select(r => r.Number));
        Assert.Equal(ExtractionStatus.Succeeded, report.Status);
    }

    [Fact(Skip = Skip)]
    public void ZeroArea_OnPlacedRoom_IsFlagged()
    {
        var report = _service.Evaluate([Rooms.Complete(1) with { AreaM2 = 0 }], [], Computed);

        Assert.Equal([QualityFlagCodes.ZeroArea], report.Rooms[0].QualityFlags);
        var anomaly = Assert.Single(report.Anomalies);
        Assert.Equal((QualityFlagCodes.ZeroArea, "1", AnomalySeverity.Warning), (anomaly.Code, anomaly.ElementId, anomaly.Severity));
        Assert.Equal(ExtractionStatus.SucceededWithWarnings, report.Status);
    }

    [Fact(Skip = Skip)]
    public void ZeroArea_OnUnplacedRoom_IsOnlyUnplaced()
    {
        var room = Rooms.Complete(415002) with { AreaM2 = 0, QualityFlags = [QualityFlagCodes.Unplaced] };

        var report = _service.Evaluate([room], [Rooms.RoomAnomaly(QualityFlagCodes.Unplaced, "415002")], Computed);

        Assert.Equal([QualityFlagCodes.Unplaced], report.Rooms[0].QualityFlags);
        Assert.Equal([QualityFlagCodes.Unplaced], report.Anomalies.Select(a => a.Code));
    }

    [Fact(Skip = Skip)]
    public void NullArea_IsNotZeroArea()
    {
        var report = _service.Evaluate([Rooms.Complete(1) with { AreaM2 = null }], [], Computed);

        Assert.DoesNotContain(QualityFlagCodes.ZeroArea, report.Rooms[0].QualityFlags);
    }

    public static TheoryData<string, AnomalySeverity> ReaderRoomFlags => new()
    {
        { QualityFlagCodes.Unplaced, AnomalySeverity.Warning },
        { QualityFlagCodes.MissingParameter, AnomalySeverity.Warning },
        { QualityFlagCodes.LevelNotFound, AnomalySeverity.Warning },
        { QualityFlagCodes.ExtractionError, AnomalySeverity.Error },
    };

    [Theory(Skip = Skip)]
    [MemberData(nameof(ReaderRoomFlags))]
    public void RoomFlagsFromReader_AreKept_AndLeadToSucceededWithWarnings(string code, AnomalySeverity severity)
    {
        var room = Rooms.Complete(7) with { QualityFlags = [code] };

        var report = _service.Evaluate([room], [Rooms.RoomAnomaly(code, "7", severity)], Computed);

        Assert.Contains(code, report.Rooms[0].QualityFlags);
        Assert.Contains(report.Anomalies, a => a.Code == code && a.ElementId == "7");
        Assert.Equal(ExtractionStatus.SucceededWithWarnings, report.Status);
    }

    [Theory(Skip = Skip)]
    [InlineData(AnomalySeverity.Info, ExtractionStatus.Succeeded)]
    [InlineData(AnomalySeverity.Warning, ExtractionStatus.SucceededWithWarnings)]
    [InlineData(AnomalySeverity.Error, ExtractionStatus.SucceededWithWarnings)]
    public void Status_FollowsHighestDocumentAnomalySeverity(AnomalySeverity severity, ExtractionStatus expected)
    {
        var anomaly = new AnomalyDto
        {
            Code = DocumentAnomalyCodes.LinkMissing,
            Severity = severity,
            Scope = AnomalyScope.Document,
            Message = "Lien absent.",
        };

        var report = _service.Evaluate([Rooms.Complete(1)], [anomaly], Computed);

        Assert.Equal(expected, report.Status);
        Assert.NotEqual(ExtractionStatus.Failed, report.Status);
    }

    [Fact(Skip = Skip)]
    public void QualityFlags_AreUnique_AndMatchRoomAnomalies()
    {
        var rooms = new[]
        {
            Rooms.Complete(1, "001") with { AreaM2 = 0, Level = null, QualityFlags = [QualityFlagCodes.LevelNotFound] },
            Rooms.Complete(2, "001"),
            Rooms.Complete(3, "001"),
        };

        var report = _service.Evaluate(rooms, [Rooms.RoomAnomaly(QualityFlagCodes.LevelNotFound, "1")], NotComputed);

        foreach (var room in report.Rooms)
        {
            Assert.Equal(room.QualityFlags.Distinct().Count(), room.QualityFlags.Count);
            var anomalyCodes = report.Anomalies.Where(a => a.Scope == AnomalyScope.Room && a.ElementId == room.ElementId).Select(a => a.Code).Distinct();
            Assert.Equal(room.QualityFlags.Order(), anomalyCodes.Order());
        }

        Assert.All(report.Anomalies.Where(a => a.Scope == AnomalyScope.Document), a => Assert.Null(a.ElementId));
    }

    private static RoomDto Room(QualityReport report, string elementId) => report.Rooms.Single(r => r.ElementId == elementId);
}
