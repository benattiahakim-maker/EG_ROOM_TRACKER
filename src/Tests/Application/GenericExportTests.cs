using System.Text.Json.Nodes;
using EgRoomTracker.Application;
using EgRoomTracker.Application.Generic;
using EgRoomTracker.Application.Revit;
using EgRoomTracker.Application.Trc;
using EgRoomTracker.Contracts.Configuration;
using EgRoomTracker.Contracts.Results;
using EgRoomTracker.Logging;
using EgRoomTracker.Tests.Support;

namespace EgRoomTracker.Tests.Application;

/// <summary>Sortie <c>outputFormat: generic</c> : écriture de <c>result.generic.json</c> et branchement dans l'extraction.</summary>
public sealed class GenericExportTests
{
    private static readonly BoundingBoxMm Box = new(0, 0, 0, 5000, 4000, 2700);

    // ---- Service d'écriture ----

    [Fact]
    public void Export_service_writes_a_valid_envelope_atomically_in_utf8_without_bom_and_lists_only_existing_images()
    {
        var dir = Directory.CreateTempSubdirectory("egr-generic-").FullName;
        try
        {
            var result = GenericFixtures.Result();
            var output = Path.Combine(dir, "result");
            var images = Path.Combine(output, "Images");
            Directory.CreateDirectory(images);
            var existing = TrcNaming.RoomImageFileName("room-uid-100", GenericFixtures.CodeFile, 100);
            File.WriteAllBytes(Path.Combine(images, existing), [0xFF, 0xD8]);

            var file = new GenericExportService().Write(result, output, images);

            Assert.Equal("result.generic.json", file);
            var path = Path.Combine(output, file);
            Assert.NotEqual(0xEF, File.ReadAllBytes(path)[0]);
            var envelope = JsonNode.Parse(File.ReadAllText(path))!;
            GenericEnvelopeChecks.AssertValid(envelope);
            var urls = GenericEnvelopeChecks.AllObjects(envelope)
                .SelectMany(o => (JsonArray)o["properties"]!)
                .Where(p => p!["type"]!.GetValue<string>() == "file")
                .Select(p => p!["value"]!["url"]!.GetValue<string>())
                .ToList();
            Assert.Equal([$"/Images/{existing}"], urls);
            Assert.Empty(Directory.EnumerateFiles(output, ".*.tmp"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Export_service_without_an_images_directory_writes_no_file_property()
    {
        var dir = Directory.CreateTempSubdirectory("egr-generic-").FullName;
        try
        {
            new GenericExportService().Write(GenericFixtures.Result(), dir, null);

            var text = File.ReadAllText(Path.Combine(dir, "result.generic.json"));
            Assert.DoesNotContain("\"type\": \"file\"", text);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ---- Orchestration ----

    private sealed class FakeGeneric : IGenericExportService
    {
        public int Calls { get; private set; }

        public ExtractionResult? Written { get; private set; }

        public string? Directory { get; private set; }

        public string? ImagesDirectory { get; private set; }

        public bool? IncludeSecondary { get; private set; }

        public string Write(ExtractionResult result, string directory, string? imagesDirectory, bool includeSecondaryDesignOptions = false)
        {
            IncludeSecondary = includeSecondaryDesignOptions;
            Calls++;
            Written = result;
            Directory = directory;
            ImagesDirectory = imagesDirectory;
            return "result.generic.json";
        }
    }

    private static Pipeline Create(OutputFormat format, FakeGeneric? generic) => new()
    {
        LoadSettings = () => new ExtractionSettings { SchemaVersion = "1.0", OutputFormat = format },
        GenericExport = generic,
        NamedParameters = (_, names) => names.ToDictionary(n => n, n => (string?)$"v-{n}"),
        Rooms = [new(415002, "u-415002", Box), new(412345, "u-412345")],
    };

    [Fact]
    public void Generic_format_writes_only_the_generic_file_and_keeps_the_room_boxes()
    {
        var generic = new FakeGeneric();
        var pipeline = Create(OutputFormat.Generic, generic);
        var request = Pipeline.Request with { ImagesDirectory = "/out/Images" };

        var result = pipeline.CreateService().Run(request);

        Assert.DoesNotContain("export", pipeline.Calls);
        Assert.Equal(1, generic.Calls);
        Assert.Equal("/out", generic.Directory);
        Assert.Equal("/out/Images", generic.ImagesDirectory);
        Assert.Equal("generic", result.Summary.Options.OutputFormat);
        Assert.Equal(Box, generic.Written!.Rooms.Single(r => r.ElementId == "415002").BoundingBox);
        Assert.Null(generic.Written.Rooms.Single(r => r.ElementId == "412345").BoundingBox);
        Assert.All(generic.Written.Rooms, room => Assert.Equal("v-SGP-DI-Ouvrage", room.NamedParameters![TrcIdentification.Ouvrage]));
    }

    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 3)]
    public void Generic_format_counts_in_roomsExported_only_the_rooms_present_in_the_result(bool include, int expected)
    {
        var pipeline = new Pipeline
        {
            LoadSettings = () => new ExtractionSettings { SchemaVersion = "1.0", OutputFormat = OutputFormat.Generic, IncludeSecondaryDesignOptions = include },
            GenericExport = new FakeGeneric(),
            Rooms = [new(415002, "u-415002"), new(412345, "u-412345"), new(412346, "u-412346")],
            ReadRoom = room => new RoomReadResult(
                new RoomDto
                {
                    ElementId = room.ElementId.ToString(),
                    UniqueId = room.UniqueId,
                    VolumeStatus = VolumeStatus.NotComputed,
                    DesignOption = room.ElementId == 412346 ? new ElementRef("701", "Occupied") : null,
                    DesignOptionIsPrimary = room.ElementId == 412346 ? false : null,
                },
                []),
        };

        var result = pipeline.CreateService().Run(Pipeline.Request);

        Assert.Equal(3, result.Summary.Counts.RoomsCollected);
        Assert.Equal(expected, result.Summary.Counts.RoomsExported);
        var summary = Assert.Single(pipeline.Logs, l => l.EventCode == LogEventCodes.ExtractionSummary);
        Assert.Equal(3, summary.Data!["roomsCollected"]);
        Assert.Equal(expected, summary.Data["roomsExported"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Generic_format_passes_the_secondary_design_options_setting(bool include)
    {
        var generic = new FakeGeneric();
        var pipeline = new Pipeline
        {
            LoadSettings = () => new ExtractionSettings { SchemaVersion = "1.0", OutputFormat = OutputFormat.Generic, IncludeSecondaryDesignOptions = include },
            GenericExport = generic,
            NamedParameters = (_, names) => names.ToDictionary(n => n, n => (string?)$"v-{n}"),
            Rooms = [new(415002, "u-415002", Box)],
        };

        pipeline.CreateService().Run(Pipeline.Request);

        Assert.Equal(include, generic.IncludeSecondary);
    }

    [Fact]
    public void A_failed_extraction_in_generic_format_still_writes_result_json_and_no_generic_file()
    {
        var generic = new FakeGeneric();
        var pipeline = Create(OutputFormat.Generic, generic);
        pipeline.CollectException = new InvalidOperationException("collecte");

        var result = pipeline.CreateService().Run(Pipeline.Request);

        Assert.Equal(ExtractionStatus.Failed, result.Summary.Status);
        Assert.Contains("export", pipeline.Calls);
        Assert.Equal(0, generic.Calls);
        Assert.Equal("generic", result.Summary.Options.OutputFormat);
    }

    [Theory]
    [InlineData(OutputFormat.Contract)]
    [InlineData(OutputFormat.Trc)]
    [InlineData(OutputFormat.Both)]
    public void The_other_formats_never_write_the_generic_file(OutputFormat format)
    {
        var generic = new FakeGeneric();

        Create(format, generic).CreateService().Run(Pipeline.Request);

        Assert.Equal(0, generic.Calls);
    }

    [Theory]
    [InlineData(OutputFormat.Contract, "détail dans result.json.")]
    [InlineData(OutputFormat.Both, "détail dans result.json.")]
    [InlineData(OutputFormat.Generic, "détail dans result.generic.json.")]
    [InlineData(OutputFormat.Trc, "aucun fichier de détail avec le format trc.")]
    public void The_anomalies_log_line_names_the_file_of_the_output_format(OutputFormat format, string expectedEnd)
    {
        var pipeline = Create(format, new FakeGeneric());

        pipeline.CreateService().Run(Pipeline.Request);

        var log = Assert.Single(pipeline.Logs, l => l.EventCode == LogEventCodes.AnomaliesDetected);
        Assert.EndsWith(expectedEnd, log.Message, StringComparison.Ordinal);
        Assert.StartsWith("1 anomalie(s) ; ", log.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Generic_format_without_the_service_falls_back_to_a_failed_contract_result()
    {
        var pipeline = Create(OutputFormat.Generic, null);

        var result = pipeline.CreateService().Run(Pipeline.Request);

        Assert.Equal(ExtractionStatus.Failed, result.Summary.Status);
        Assert.Contains(result.Summary.Errors, e => e.Code == EgRoomTracker.Contracts.Codes.ErrorCodes.ExportFailed);
    }
}
