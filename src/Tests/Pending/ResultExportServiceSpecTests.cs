using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using EgRoomTracker.Application;
using EgRoomTracker.Application.Quality;
using EgRoomTracker.Application.Revit;
using EgRoomTracker.Contracts.Codes;
using EgRoomTracker.Contracts.Results;
using EgRoomTracker.Export;
using EgRoomTracker.Tests.Support;

namespace EgRoomTracker.Tests.Pending;

/// <summary>
/// Spécification exécutable de l'export de <c>result.json</c> (sections 2, 3 et 6), à activer avec EGR-12 :
/// UTF-8 sans BOM, conforme au schéma dans les trois statuts, déterministe, nombres invariants, null / "" / 0,
/// écriture via fichier temporaire sans résidu.
/// </summary>
public sealed class ResultExportServiceSpecTests : IDisposable
{
    private const string Skip = PendingImplementations.Egr12;

    private readonly string _directory = Directory.CreateTempSubdirectory("egr17-export-").FullName;
    private readonly IExportService _service = new ResultExportService();

    private string OutputPath => Path.Combine(_directory, "result.json");

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    public static TheoryData<string> Statuses => new() { "succeeded", "succeededWithWarnings", "failed" };

    [Theory(Skip = Skip)]
    [MemberData(nameof(Statuses))]
    public void WrittenFile_IsSchemaValid_InEveryStatus(string status)
    {
        _service.Write(Result(status), OutputPath);

        var json = ReadUtf8WithoutBom(OutputPath);
        ResultSchema.AssertValid(json);
        Assert.Equal(status, JsonNode.Parse(json)!["summary"]!["status"]!.GetValue<string>());
    }

    [Fact(Skip = Skip)]
    public void WrittenFile_HasNoBom_AndIsStrictUtf8()
    {
        _service.Write(Result("succeededWithWarnings"), OutputPath);

        var bytes = File.ReadAllBytes(OutputPath);
        Assert.False(bytes.AsSpan().StartsWith(Encoding.UTF8.GetPreamble()), "BOM UTF-8 inattendu.");
        Assert.Contains("Réserve", new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes));
    }

    [Fact(Skip = Skip)]
    public void Dates_AreUtcIso8601WithZ_AndAccentsAreWrittenAsIs()
    {
        _service.Write(Result("succeededWithWarnings"), OutputPath);

        var json = ReadUtf8WithoutBom(OutputPath);
        var summary = JsonNode.Parse(json)!["summary"]!;
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?Z$", summary["startedAtUtc"]!.GetValue<string>());
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?Z$", summary["completedAtUtc"]!.GetValue<string>());
        Assert.Contains("\"Réserve\"", json);
        Assert.DoesNotContain("\\u00E9", json);
    }

    [Fact(Skip = Skip)]
    public void Write_KeepsRoomOrderAsReceived()
    {
        // Décision du chef de projet : le tri relève de l'orchestration ; l'export n'en change pas l'ordre.
        var result = Result("succeeded");
        var reversed = result with { Rooms = [.. result.Rooms.Reverse()] };

        _service.Write(reversed, OutputPath);

        var ids = JsonNode.Parse(ReadUtf8WithoutBom(OutputPath))!["rooms"]!.AsArray().Select(r => r!["elementId"]!.GetValue<string>());
        Assert.Equal(reversed.Rooms.Select(r => r.ElementId), ids);
    }

    [Fact(Skip = Skip)]
    public void Write_IsDeterministic()
    {
        var result = Result("succeededWithWarnings");
        var second = Path.Combine(_directory, "second.json");

        _service.Write(result, OutputPath);
        _service.Write(result, second);

        Assert.Equal(File.ReadAllBytes(OutputPath), File.ReadAllBytes(second));
    }

    [Fact(Skip = Skip)]
    public void Numbers_AreInvariant_UnderFrenchCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
        try
        {
            var room = Rooms.Complete(412345) with { AreaM2 = 18.425, UnboundedHeightM = 2.7 };
            _service.Write(Single(room), OutputPath);

            var r = JsonNode.Parse(ReadUtf8WithoutBom(OutputPath))!["rooms"]![0]!;
            Assert.Equal(18.425, r["areaM2"]!.GetValue<double>());
            Assert.Equal(2.7, r["unboundedHeightM"]!.GetValue<double>());
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact(Skip = Skip)]
    public void NullEmptyAndZero_ArePreserved()
    {
        var room = Rooms.Complete(412398) with
        {
            Occupancy = null,
            AreaM2 = 0,
            Finishes = new RoomFinishes("Résine", "Peinture blanche", "", null),
        };

        _service.Write(Single(room), OutputPath);

        var r = JsonNode.Parse(ReadUtf8WithoutBom(OutputPath))!["rooms"]![0]!;
        Assert.True(r.AsObject().ContainsKey("occupancy"));
        Assert.Null(r["occupancy"]);
        Assert.Equal(0d, r["areaM2"]!.GetValue<double>());
        Assert.Equal("", r["finishes"]!["ceiling"]!.GetValue<string>());
        Assert.True(r["finishes"]!.AsObject().ContainsKey("base"));
        Assert.Null(r["finishes"]!["base"]);
        Assert.Null(r["volumeM3"]);
    }

    [Fact(Skip = Skip)]
    public void Write_ReplacesExistingFile_AndLeavesNoTemporaryFile()
    {
        File.WriteAllText(OutputPath, "ancien contenu");

        _service.Write(Result("succeeded"), OutputPath);

        ResultSchema.AssertValid(ReadUtf8WithoutBom(OutputPath));
        Assert.Equal(["result.json"], Directory.GetFiles(_directory).Select(Path.GetFileName));
    }

    [Fact(Skip = Skip)]
    public void Write_ToMissingDirectory_ThrowsIOException() =>
        Assert.ThrowsAny<IOException>(() => _service.Write(Result("succeeded"), Path.Combine(_directory, "absent", "result.json")));

    private static ExtractionResult Result(string status) => status switch
    {
        "succeeded" => new TestPipeline { RoomIds = [412398, 412345] }.Run(),
        "succeededWithWarnings" => new TestPipeline
        {
            RoomIds = [415002, 412345],
            ReadRoom = id => id == 415002
                ? new RoomReadResult(
                    Rooms.Complete(id) with { Name = "Réserve", AreaM2 = 0, QualityFlags = [QualityFlagCodes.Unplaced] },
                    [Rooms.RoomAnomaly(QualityFlagCodes.Unplaced, "415002")])
                : new RoomReadResult(Rooms.Complete(id), []),
            Evaluate = (rooms, anomalies, _) => new QualityReport(rooms, anomalies, ExtractionStatus.SucceededWithWarnings),
        }.Run(),
        _ => new TestPipeline
        {
            LoadSettings = () => throw new ExtractionFailedException(ErrorCodes.ConfigSchemaUnsupported, Stages.Configuration, "schemaVersion 2.0 non prise en charge."),
        }.Run(),
    };

    private static ExtractionResult Single(RoomDto room) =>
        new TestPipeline { RoomIds = [long.Parse(room.ElementId, CultureInfo.InvariantCulture)], ReadRoom = _ => new RoomReadResult(room, []) }.Run();

    private static string ReadUtf8WithoutBom(string path)
    {
        var bytes = File.ReadAllBytes(path);
        Assert.False(bytes.AsSpan().StartsWith(Encoding.UTF8.GetPreamble()), "BOM UTF-8 inattendu.");
        return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
    }
}
