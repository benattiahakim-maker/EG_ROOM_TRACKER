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

namespace EgRoomTracker.Tests.Export;

/// <summary>
/// Export de <c>result.json</c> (sections 2, 3 et 6, EGR-12) : UTF-8 sans BOM, conforme au schéma dans les trois
/// statuts, déterministe, nombres invariants, null / "" / 0, écriture via fichier temporaire sans résidu.
/// Les huit premiers cas sont la spécification écrite en EGR-17 (#25), activée ici sans modification.
/// </summary>
public sealed class ResultExportServiceTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("egr12-export-").FullName;
    private readonly IExportService _service = new ResultExportService();

    private string OutputPath => Path.Combine(_directory, "result.json");

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    public static TheoryData<string> Statuses => new() { "succeeded", "succeededWithWarnings", "failed" };

    [Theory]
    [MemberData(nameof(Statuses))]
    public void WrittenFile_IsSchemaValid_InEveryStatus(string status)
    {
        _service.Write(Result(status), OutputPath);

        var json = ReadUtf8WithoutBom(OutputPath);
        ResultSchema.AssertValid(json);
        Assert.Equal(status, JsonNode.Parse(json)!["summary"]!["status"]!.GetValue<string>());
    }

    [Fact]
    public void WrittenFile_HasNoBom_AndIsStrictUtf8()
    {
        _service.Write(Result("succeededWithWarnings"), OutputPath);

        var bytes = File.ReadAllBytes(OutputPath);
        Assert.False(bytes.AsSpan().StartsWith(Encoding.UTF8.GetPreamble()), "BOM UTF-8 inattendu.");
        Assert.Contains("Réserve", new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes));
    }

    [Fact]
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

    [Fact]
    public void Write_KeepsRoomOrderAsReceived()
    {
        // Décision du chef de projet : le tri relève de l'orchestration ; l'export n'en change pas l'ordre.
        var result = Result("succeeded");
        var reversed = result with { Rooms = [.. result.Rooms.Reverse()] };

        _service.Write(reversed, OutputPath);

        var ids = JsonNode.Parse(ReadUtf8WithoutBom(OutputPath))!["rooms"]!.AsArray().Select(r => r!["elementId"]!.GetValue<string>());
        Assert.Equal(reversed.Rooms.Select(r => r.ElementId), ids);
    }

    [Fact]
    public void Write_IsDeterministic()
    {
        var result = Result("succeededWithWarnings");
        var second = Path.Combine(_directory, "second.json");

        _service.Write(result, OutputPath);
        _service.Write(result, second);

        Assert.Equal(File.ReadAllBytes(OutputPath), File.ReadAllBytes(second));
    }

    [Fact]
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

    [Fact]
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

    [Fact]
    public void Write_ReplacesExistingFile_AndLeavesNoTemporaryFile()
    {
        File.WriteAllText(OutputPath, "ancien contenu");

        _service.Write(Result("succeeded"), OutputPath);

        ResultSchema.AssertValid(ReadUtf8WithoutBom(OutputPath));
        Assert.Equal(["result.json"], Directory.GetFiles(_directory).Select(Path.GetFileName));
    }

    [Fact]
    public void Write_ToMissingDirectory_ThrowsIOException() =>
        Assert.ThrowsAny<IOException>(() => _service.Write(Result("succeeded"), Path.Combine(_directory, "absent", "result.json")));

    // Cas propres à EGR-12.

    [Fact]
    public void DocumentationExample_IsReproducedWithoutLoss()
    {
        var example = ResultSchema.LoadExample();

        _service.Write(ContractJson.Deserialize(example.ToJsonString()), OutputPath);

        var written = JsonNode.Parse(ReadUtf8WithoutBom(OutputPath))!;
        Assert.True(JsonNode.DeepEquals(example, written), "Écart avec docs/examples/result.example.json :\n" + written.ToJsonString());
    }

    [Theory]
    [MemberData(nameof(Statuses))]
    public void Output_IsEquivalentToContractReferenceSerialization(string status)
    {
        var result = Result(status);

        var exported = JsonNode.Parse(Encoding.UTF8.GetString(ResultExportService.Serialize(result)));
        var reference = JsonNode.Parse(ContractJson.Serialize(result));

        Assert.True(JsonNode.DeepEquals(reference, exported));
    }

    [Fact]
    public void Layout_IsIndentedWithLfOnly_AndEndsWithNewline()
    {
        var bytes = ResultExportService.Serialize(Result("succeededWithWarnings"));
        var text = Encoding.UTF8.GetString(bytes);

        Assert.DoesNotContain('\r', text);
        Assert.StartsWith("{\n  \"schemaVersion\": \"" + SchemaVersions.Result + "\",\n  \"summary\": {\n", text);
        Assert.EndsWith("]\n}\n", text);
    }

    [Fact]
    public void CarriageReturnInsideAString_IsEscaped_NotNormalized()
    {
        var room = Rooms.Complete(412345) with { Name = "Ligne 1\r\nLigne 2" };

        _service.Write(Single(room), OutputPath);

        var json = ReadUtf8WithoutBom(OutputPath);
        Assert.Contains("\"Ligne 1\\r\\nLigne 2\"", json);
        Assert.Equal("Ligne 1\r\nLigne 2", JsonNode.Parse(json)!["rooms"]![0]!["name"]!.GetValue<string>());
    }

    [Fact]
    public void Bytes_AreIdenticalAcrossCultures()
    {
        var result = Result("succeededWithWarnings");
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var invariant = ResultExportService.Serialize(result);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var french = ResultExportService.Serialize(result);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            var turkish = ResultExportService.Serialize(result);

            Assert.Equal(invariant, french);
            Assert.Equal(invariant, turkish);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void FullPrecision_IsKept_WithoutPresentationRounding()
    {
        // D7 : valeur brute convertie, aucun arrondi (ex. 1 ft² = 0,09290304 m²).
        var room = Rooms.Complete(412345) with { AreaM2 = 0.09290304, UnboundedHeightM = 0.1 + 0.2 };

        _service.Write(Single(room), OutputPath);

        var r = JsonNode.Parse(ReadUtf8WithoutBom(OutputPath))!["rooms"]![0]!;
        Assert.Equal(0.09290304, r["areaM2"]!.GetValue<double>());
        Assert.Equal(0.1 + 0.2, r["unboundedHeightM"]!.GetValue<double>());
    }

    [Fact]
    public void SharedParameterValues_KeepTheirJsonType()
    {
        var room = Rooms.Complete(412345) with
        {
            SharedParameters = new Dictionary<string, SharedParameterValue>
            {
                ["codeLocal"] = new(Rooms.CodeLocalGuid, "A-001"),
                ["surfaceUtile"] = new(Guid.Parse("0f1e2d3c-4b5a-4968-8776-a5b4c3d2e1f0"), 17.5),
                ["Lot"] = new(Guid.Parse("9a8b7c6d-5e4f-4a3b-8c2d-1e0f9a8b7c6d"), null),
            },
        };

        _service.Write(Single(room), OutputPath);

        var json = ReadUtf8WithoutBom(OutputPath);
        ResultSchema.AssertValid(json);
        var shared = JsonNode.Parse(json)!["rooms"]![0]!["sharedParameters"]!;
        Assert.Equal("A-001", shared["codeLocal"]!["value"]!.GetValue<string>());
        Assert.Equal(17.5, shared["surfaceUtile"]!["value"]!.GetValue<double>());
        Assert.Null(shared["Lot"]!["value"]);
        Assert.Equal("3f2b8c1e-6d4a-4b7e-9a51-2c0d8e7f4a10", shared["codeLocal"]!["guid"]!.GetValue<string>());
        Assert.Equal(["codeLocal", "surfaceUtile", "Lot"], shared.AsObject().Select(p => p.Key));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void NonFiniteNumber_IsRefused_AndPreviousFileIsLeftUntouched(double area)
    {
        File.WriteAllText(OutputPath, "ancien contenu");
        var result = Single(Rooms.Complete(412345));
        result = result with { Rooms = [result.Rooms[0] with { AreaM2 = area }] };

        Assert.Throws<NotSupportedException>(() => _service.Write(result, OutputPath));

        Assert.Equal("ancien contenu", File.ReadAllText(OutputPath));
        Assert.Equal(["result.json"], Directory.GetFiles(_directory).Select(Path.GetFileName));
    }

    [Fact]
    public void PublishFailure_RemovesTheTemporaryFile()
    {
        // Le chemin final est un dossier : le renommage échoue après l'écriture du fichier temporaire.
        Directory.CreateDirectory(OutputPath);

        Assert.NotNull(Record.Exception(() => _service.Write(Result("succeeded"), OutputPath)));

        Assert.Empty(Directory.GetFiles(_directory));
        Assert.True(Directory.Exists(OutputPath));
    }

    [Fact]
    public void Write_RejectsMissingArguments()
    {
        Assert.Throws<ArgumentNullException>(() => _service.Write(null!, OutputPath));
        Assert.ThrowsAny<ArgumentException>(() => _service.Write(Result("succeeded"), " "));
        Assert.Empty(Directory.GetFiles(_directory));
    }

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
