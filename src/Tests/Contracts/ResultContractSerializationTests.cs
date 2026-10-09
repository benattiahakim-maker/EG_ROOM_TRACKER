using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EgRoomTracker.Application;
using EgRoomTracker.Application.Quality;
using EgRoomTracker.Application.Revit;
using EgRoomTracker.Contracts.Codes;
using EgRoomTracker.Contracts.Configuration;
using EgRoomTracker.Contracts.Results;
using EgRoomTracker.Tests.Support;

namespace EgRoomTracker.Tests.Contracts;

/// <summary>
/// Les DTO du contrat, remplis par <see cref="RoomExtractionService"/>, se sérialisent en un <c>result.json</c>
/// conforme au schéma dans les trois statuts (section 8), de façon déterministe et sans perte
/// (null / "" / 0, nombres invariants, tri par elementId numérique). Sérialisation de référence des tests
/// (<see cref="ContractJson"/>) en attendant <c>ResultExportService</c> (EGR-12).
/// </summary>
public class ResultContractSerializationTests
{
    [Fact]
    public void Example_RoundTripsThroughDtosWithoutLoss()
    {
        var example = ResultSchema.LoadExample();

        var result = ContractJson.Deserialize(example.ToJsonString());
        var reserialized = JsonNode.Parse(ContractJson.Serialize(result))!;

        Assert.True(
            JsonNode.DeepEquals(NormalizeDates(example), NormalizeDates(reserialized)),
            "Écart entre l'exemple et sa sérialisation via les DTO :\n" + reserialized.ToJsonString());
    }

    [Fact]
    public void Succeeded_PipelineResult_IsSchemaValid()
    {
        var pipeline = new TestPipeline { RoomIds = [412398, 412345] };

        var result = pipeline.Run();

        Assert.Equal(ExtractionStatus.Succeeded, result.Summary.Status);
        var json = JsonNode.Parse(ContractJson.Serialize(result))!;
        ResultSchema.AssertValid(json);
        Assert.Equal("succeeded", json["summary"]!["status"]!.GetValue<string>());
        Assert.Empty(json["summary"]!["errors"]!.AsArray());
        Assert.Equal("2026-10-09T12:00:03+00:00", json["summary"]!["startedAtUtc"]!.GetValue<string>());
    }

    [Fact]
    public void SucceededWithWarnings_PipelineResult_IsSchemaValidAndCountsRoomsWithFlags()
    {
        var pipeline = new TestPipeline
        {
            RoomIds = [415002, 412345, 412398],
            ReadRoom = id => id == 415002
                ? new RoomReadResult(
                    Rooms.Complete(id, "001") with { AreaM2 = 0, QualityFlags = [QualityFlagCodes.Unplaced] },
                    [Rooms.RoomAnomaly(QualityFlagCodes.Unplaced, "415002")])
                : new RoomReadResult(Rooms.Complete(id), []),
            Evaluate = (rooms, anomalies, _) => new QualityReport(
                rooms,
                [Rooms.VolumeNotComputed(rooms.Count), .. anomalies],
                ExtractionStatus.SucceededWithWarnings),
        };

        var result = pipeline.Run();

        ResultSchema.AssertValid(ContractJson.Serialize(result));
        Assert.Equal(ExtractionStatus.SucceededWithWarnings, result.Summary.Status);
        Assert.Equal(new RoomCounts(3, 3, 1), result.Summary.Counts);
        Assert.Equal(["412345", "412398", "415002"], result.Rooms.Select(r => r.ElementId));
        Assert.Equal(2, result.Anomalies.Count);
    }

    public static TheoryData<string, string> GlobalErrors => new()
    {
        { ErrorCodes.ConfigInvalid, Stages.Configuration },
        { ErrorCodes.ConfigSchemaUnsupported, Stages.Configuration },
        { ErrorCodes.PathOutsideInput, Stages.Configuration },
        { ErrorCodes.RvtNotFound, Stages.Input },
        { ErrorCodes.RvtAmbiguous, Stages.Input },
        { ErrorCodes.RevitVersionIncompatible, Stages.Document },
        { ErrorCodes.DocumentOpenFailed, Stages.Document },
        { ErrorCodes.DocumentCorrupted, Stages.Document },
    };

    [Theory]
    [MemberData(nameof(GlobalErrors))]
    public void Failed_PipelineResult_IsSchemaValid(string code, string stage)
    {
        var failure = new ExtractionFailedException(code, stage, $"{code} (test).");
        var pipeline = new TestPipeline { RoomIds = [412345] };
        if (stage == Stages.Configuration)
        {
            pipeline.LoadSettings = () => throw failure;
        }
        else
        {
            pipeline.OpenDocument = () => throw failure;
        }

        var result = pipeline.Run();

        var json = JsonNode.Parse(ContractJson.Serialize(result))!;
        ResultSchema.AssertValid(json);
        Assert.Equal("failed", json["summary"]!["status"]!.GetValue<string>());
        Assert.Equal(code, json["summary"]!["errors"]![0]!["code"]!.GetValue<string>());
        Assert.Equal(stage, json["summary"]!["errors"]![0]!["stage"]!.GetValue<string>());
        Assert.Empty(json["rooms"]!.AsArray());
        Assert.Empty(json["anomalies"]!.AsArray());
        Assert.Null(json["summary"]!["source"]);
    }

    [Fact]
    public void Failed_ExportResult_IsSchemaValid()
    {
        var pipeline = new TestPipeline { RoomIds = [412345], ExportException = new IOException("disque plein") };

        var result = pipeline.Run();

        ResultSchema.AssertValid(ContractJson.Serialize(result));
        Assert.Equal(ErrorCodes.ExportFailed, Assert.Single(result.Summary.Errors).Code);
    }

    [Fact]
    public void Failed_UnexpectedErrorDuringCollection_IsSchemaValidAndKeepsSource()
    {
        var pipeline = new TestPipeline { RoomIds = [412345], ReadRoom = _ => throw new InvalidOperationException("boom") };

        var result = pipeline.Run();

        var json = JsonNode.Parse(ContractJson.Serialize(result))!;
        ResultSchema.AssertValid(json);
        Assert.Equal("Projet/EG_Batiment_A.rvt", json["summary"]!["source"]!["relativePath"]!.GetValue<string>());
        Assert.Equal(ErrorCodes.UnexpectedError, Assert.Single(result.Summary.Errors).Code);
    }

    [Fact]
    public void Rooms_AreSortedByNumericElementId_NotLexicographically()
    {
        var pipeline = new TestPipeline { RoomIds = [100, 9, 1_000_000_000_000, 10, 412345] };

        var result = pipeline.Run();

        Assert.Equal(["9", "10", "100", "412345", "1000000000000"], result.Rooms.Select(r => r.ElementId));
        ResultSchema.AssertValid(ContractJson.Serialize(result));
    }

    [Fact]
    public void Serialization_IsDeterministic_AcrossRunsAndCollectionOrder()
    {
        var first = new TestPipeline { RoomIds = [415002, 412345, 412398] }.Run();
        var second = new TestPipeline { RoomIds = [412398, 415002, 412345] }.Run();

        var firstBytes = ContractJson.SerializeToUtf8Bytes(first with { Summary = first.Summary with { ExtractorVersion = "test" } });
        var secondBytes = ContractJson.SerializeToUtf8Bytes(second with { Summary = second.Summary with { ExtractorVersion = "test" } });

        Assert.Equal(firstBytes, secondBytes);
    }

    [Fact]
    public void NullEmptyAndZero_AreDistinctInJson()
    {
        var room = Rooms.Complete(412398) with
        {
            Name = "",
            Occupancy = null,
            AreaM2 = 0,
            UnboundedHeightM = null,
            Finishes = new RoomFinishes("Résine", "Peinture blanche", "", null),
            SharedParameters = new Dictionary<string, SharedParameterValue>
            {
                ["codeLocal"] = new(Rooms.CodeLocalGuid, null),
                ["surfaceUtile"] = new(Guid.Parse("0b6c1d2e-3f40-4a5b-8c6d-7e8f9a0b1c2d"), 0d),
                ["commentaire"] = new(Guid.Parse("1c2d3e4f-5a6b-4c7d-8e9f-0a1b2c3d4e5f"), ""),
            },
        };

        var json = SerializeSingleRoom(room);

        var r = json["rooms"]![0]!;
        Assert.Equal(JsonValueKind.String, r["name"]!.GetValueKind());
        Assert.Equal("", r["name"]!.GetValue<string>());
        Assert.True(r.AsObject().ContainsKey("occupancy"));
        Assert.Null(r["occupancy"]);
        Assert.Equal(JsonValueKind.Number, r["areaM2"]!.GetValueKind());
        Assert.Equal(0d, r["areaM2"]!.GetValue<double>());
        Assert.True(r.AsObject().ContainsKey("unboundedHeightM"));
        Assert.Null(r["unboundedHeightM"]);
        Assert.Equal("", r["finishes"]!["ceiling"]!.GetValue<string>());
        Assert.Null(r["finishes"]!["base"]);
        Assert.Null(r["sharedParameters"]!["codeLocal"]!["value"]);
        Assert.Equal(JsonValueKind.Number, r["sharedParameters"]!["surfaceUtile"]!["value"]!.GetValueKind());
        Assert.Equal("", r["sharedParameters"]!["commentaire"]!["value"]!.GetValue<string>());
        ResultSchema.AssertValid(json);
    }

    [Fact]
    public void Numbers_AreInvariant_AndNotRounded_UnderFrenchCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
        try
        {
            var room = Rooms.Complete(412345) with
            {
                AreaM2 = 18.4251234567891,
                UnboundedHeightM = 2.7,
                VolumeStatus = VolumeStatus.Computed,
                VolumeM3 = 1e-7,
            };

            var text = ContractJson.Serialize(Wrap(room));

            Assert.Contains("\"areaM2\":18.4251234567891", text);
            Assert.Contains("\"unboundedHeightM\":2.7", text);
            Assert.DoesNotContain("18,425", text);
            var json = JsonNode.Parse(text)!;
            Assert.Equal(1e-7, json["rooms"]![0]!["volumeM3"]!.GetValue<double>());
            ResultSchema.AssertValid(json);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void ElementIdsAndNumbers_KeepTheirStringForm()
    {
        var room = Rooms.Complete(9_007_199_254_740_993, number: "001");

        var json = SerializeSingleRoom(room);

        Assert.Equal("9007199254740993", json["rooms"]![0]!["elementId"]!.GetValue<string>());
        Assert.Equal("001", json["rooms"]![0]!["number"]!.GetValue<string>());
    }

    [Fact]
    public void Accents_SurviveUtf8RoundTrip()
    {
        var room = Rooms.Complete(415002) with { Name = "Réserve", Occupancy = "Bureau d'études — 2ᵉ étage" };

        var bytes = ContractJson.SerializeToUtf8Bytes(Wrap(room));

        Assert.False(bytes.AsSpan().StartsWith(Encoding.UTF8.GetPreamble()), "BOM UTF-8 inattendu.");
        var parsed = ContractJson.Deserialize(new UTF8Encoding(false, true).GetString(bytes));
        Assert.Equal("Réserve", parsed.Rooms[0].Name);
        Assert.Equal("Bureau d'études — 2ᵉ étage", parsed.Rooms[0].Occupancy);
    }

    [Fact]
    public void Options_ReflectAppliedSettings()
    {
        var pipeline = new TestPipeline
        {
            RoomIds = [412345],
            LoadSettings = () => new ExtractionSettings
            {
                SchemaVersion = "1.0",
                PhaseIds = ["86961", "86960"],
                VolumeMode = VolumeMode.Compute,
                SharedParameters =
                [
                    new SharedParameterMapping("codeLocal", Rooms.CodeLocalGuid),
                    new SharedParameterMapping("surfaceUtile", Guid.Parse("0b6c1d2e-3f40-4a5b-8c6d-7e8f9a0b1c2d")),
                ],
            },
        };

        var json = JsonNode.Parse(ContractJson.Serialize(pipeline.Run()))!;

        var options = json["summary"]!["options"]!;
        Assert.Equal(["86961", "86960"], options["phaseIds"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.Equal("compute", options["volumeMode"]!.GetValue<string>());
        Assert.Equal(["codeLocal", "surfaceUtile"], options["sharedParameterKeys"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.Equal(VolumeMode.Compute, pipeline.QualityVolumeState?.Mode);
        ResultSchema.AssertValid(json);
    }

    [Fact]
    public void Options_WhenConfigurationFails_AreSchemaDefaults()
    {
        var pipeline = new TestPipeline
        {
            LoadSettings = () => throw new ExtractionFailedException(ErrorCodes.ConfigInvalid, Stages.Configuration, "settings.json illisible."),
        };

        var json = JsonNode.Parse(ContractJson.Serialize(pipeline.Run()))!;

        var options = json["summary"]!["options"]!;
        Assert.Null(options["phaseIds"]);
        Assert.Equal("preserve", options["volumeMode"]!.GetValue<string>());
        Assert.Empty(options["sharedParameterKeys"]!.AsArray());
        Assert.Equal(new RoomCounts(0, 0, 0), ContractJson.Deserialize(json.ToJsonString()).Summary.Counts);
    }

    private static JsonNode SerializeSingleRoom(RoomDto room) => JsonNode.Parse(ContractJson.Serialize(Wrap(room)))!;

    private static ExtractionResult Wrap(RoomDto room)
    {
        var pipeline = new TestPipeline { RoomIds = [long.Parse(room.ElementId, CultureInfo.InvariantCulture)], ReadRoom = _ => new RoomReadResult(room, []) };
        return pipeline.Run();
    }

    private static JsonNode NormalizeDates(JsonNode result)
    {
        var copy = result.DeepClone();
        foreach (var name in new[] { "startedAtUtc", "completedAtUtc" })
        {
            var value = DateTimeOffset.Parse(copy["summary"]![name]!.GetValue<string>(), CultureInfo.InvariantCulture);
            copy["summary"]![name] = value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
        }

        return copy;
    }
}
