using System.Text.Json.Nodes;
using EgRoomTracker.Application;
using EgRoomTracker.Application.Revit;
using EgRoomTracker.Contracts.Codes;
using EgRoomTracker.Contracts.Configuration;
using EgRoomTracker.Contracts.Results;
using EgRoomTracker.Logging;
using EgRoomTracker.Tests.Support;

namespace EgRoomTracker.Tests.Application;

/// <summary>Vues 3D des pièces (<c>roomImages</c>) : orchestration, anomalies, nommage TRC.</summary>
public sealed class RoomImagesTests
{
    private sealed class FakeImages(Func<IReadOnlyList<RoomImageRequest>, ImageExportResult> export) : IRoomImageService
    {
        public IReadOnlyList<RoomImageRequest>? Rooms { get; private set; }

        public string? Directory { get; private set; }

        public string? CodeFile { get; private set; }

        public int? WidthPx { get; private set; }

        public ImageExportResult Export(IReadOnlyList<RoomImageRequest> rooms, string imagesDirectory, string codeFile, int widthPx)
        {
            (Rooms, Directory, CodeFile, WidthPx) = (rooms, imagesDirectory, codeFile, widthPx);
            return export(rooms);
        }
    }

    private static ExtractionRequest Request => Pipeline.Request with { ImagesDirectory = "/out/result/Images" };

    private static Pipeline Create(FakeImages? images, bool roomImages = true, int widthPx = 1600) => new()
    {
        LoadSettings = () => new ExtractionSettings { SchemaVersion = "1.0", RoomImages = roomImages, ImageWidthPx = widthPx },
        ImageService = images,
    };

    [Fact]
    public void Images_are_requested_for_placed_rooms_with_the_trc_code_file()
    {
        var images = new FakeImages(rooms => new ImageExportResult(rooms.Count, []));
        var pipeline = Create(images);

        var result = pipeline.CreateService().Run(Request);

        Assert.Equal(["u-412345", "u-415002"], images.Rooms!.Select(r => r.UniqueId).Order());
        Assert.Equal("/out/result/Images", images.Directory);
        Assert.Equal("A", images.CodeFile);
        Assert.Equal(1600, images.WidthPx);
        Assert.Equal(new ImagesSummary(2, 2, 0), result.Summary.Images);
        Assert.Equal(true, result.Summary.Options.RoomImages);
        Assert.Equal(1600, result.Summary.Options.ImageWidthPx);
        Assert.DoesNotContain(result.Anomalies, a => a.Code == DocumentAnomalyCodes.ImageExportFailed);
        Assert.Contains(pipeline.Logs, l => l.EventCode == LogEventCodes.RoomImagesExported);
    }

    [Fact]
    public void Unplaced_rooms_are_not_photographed()
    {
        var images = new FakeImages(rooms => new ImageExportResult(rooms.Count, []));
        var pipeline = Create(images);
        pipeline.ReadRoom = room => new RoomReadResult(
            new RoomDto
            {
                ElementId = room.ElementId.ToString(),
                UniqueId = room.UniqueId,
                VolumeStatus = VolumeStatus.NotComputed,
                QualityFlags = room.ElementId == 415002 ? [QualityFlagCodes.Unplaced] : [],
            },
            []);

        var result = pipeline.CreateService().Run(Request);

        Assert.Equal(["u-412345"], images.Rooms!.Select(r => r.UniqueId));
        Assert.Equal(new ImagesSummary(1, 1, 0), result.Summary.Images);
    }

    [Fact]
    public void Excluded_secondary_design_option_rooms_get_no_image_file_and_no_image_reference()
    {
        var output = Path.Combine(Path.GetTempPath(), "rtb-img-" + Guid.NewGuid().ToString("N"));
        var imagesDir = Path.Combine(output, "Images");
        try
        {
            var images = new FakeImages(rooms =>
            {
                Directory.CreateDirectory(imagesDir);
                foreach (var room in rooms)
                {
                    File.WriteAllText(Path.Combine(imagesDir, TrcNaming.RoomImageFileName(room.UniqueId, "A", room.ElementId)), "jpg");
                }

                return new ImageExportResult(rooms.Count, []);
            });
            var pipeline = new Pipeline
            {
                LoadSettings = () => new ExtractionSettings { SchemaVersion = "1.0", RoomImages = true, OutputFormat = OutputFormat.Generic },
                ImageService = images,
                GenericExport = new EgRoomTracker.Application.Generic.GenericExportService(),
            };
            pipeline.ReadRoom = room => new RoomReadResult(
                new RoomDto
                {
                    ElementId = room.ElementId.ToString(),
                    UniqueId = room.UniqueId,
                    VolumeStatus = VolumeStatus.NotComputed,
                    DesignOption = room.ElementId == 415002 ? new ElementRef("701", "Occupied") : new ElementRef("700", "Unoccupied  <principal>"),
                    DesignOptionIsPrimary = room.ElementId != 415002,
                },
                []);
            var request = Pipeline.Request with { OutputPath = Path.Combine(output, "result.json"), ImagesDirectory = imagesDir };

            var result = pipeline.CreateService().Run(request);

            Assert.Equal(["u-412345"], images.Rooms!.Select(r => r.UniqueId));
            Assert.Equal(new ImagesSummary(1, 1, 0), result.Summary.Images);
            Assert.Equal([TrcNaming.RoomImageFileName("u-412345", "A", 412345)], Directory.GetFiles(imagesDir).Select(Path.GetFileName));
            var json = File.ReadAllText(Path.Combine(output, "result.generic.json"));
            Assert.Contains("u-412345_A_412345", json, StringComparison.Ordinal);
            Assert.DoesNotContain("415002", json, StringComparison.Ordinal);
            Assert.DoesNotContain("u-415002", json, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(output))
            {
                Directory.Delete(output, true);
            }
        }
    }

    [Fact]
    public void Partial_failure_is_a_warning_and_rooms_are_still_exported()
    {
        var images = new FakeImages(rooms => new ImageExportResult(1, [rooms[0].ElementId], "export impossible"));
        var pipeline = Create(images);

        var result = pipeline.CreateService().Run(Request);

        Assert.Empty(result.Summary.Errors);
        Assert.Equal(2, result.Rooms.Count);
        Assert.Equal(new ImagesSummary(2, 1, 1), result.Summary.Images);
        var anomaly = Assert.Single(result.Anomalies, a => a.Code == DocumentAnomalyCodes.ImageExportFailed);
        Assert.Equal(AnomalySeverity.Warning, anomaly.Severity);
        Assert.Equal(1, anomaly.Details["failed"]);
        Assert.Equal("export impossible", anomaly.Details["reason"]);
        Assert.Contains(pipeline.Logs, l => l.EventCode == LogEventCodes.RoomImagesFailed);
    }

    [Fact]
    public void Failing_service_is_a_warning_not_a_failure()
    {
        var images = new FakeImages(_ => throw new InvalidOperationException("ExportImage non pris en charge"));
        var pipeline = Create(images);

        var result = pipeline.CreateService().Run(Request);

        Assert.Empty(result.Summary.Errors);
        Assert.Equal(new ImagesSummary(2, 0, 2), result.Summary.Images);
        var anomaly = Assert.Single(result.Anomalies, a => a.Code == DocumentAnomalyCodes.ImageExportFailed);
        Assert.Equal("InvalidOperationException", anomaly.Details["reason"]);
    }

    [Fact]
    public void Missing_images_directory_is_reported()
    {
        var images = new FakeImages(rooms => new ImageExportResult(rooms.Count, []));
        var pipeline = Create(images);

        var result = pipeline.CreateService().Run(Pipeline.Request);

        Assert.Null(images.Rooms);
        Assert.Contains(result.Anomalies, a => a.Code == DocumentAnomalyCodes.ImageExportFailed);
    }

    [Fact]
    public void Disabled_by_default_calls_nothing_and_writes_no_new_property()
    {
        var images = new FakeImages(rooms => new ImageExportResult(rooms.Count, []));
        var pipeline = Create(images, roomImages: false);

        var result = pipeline.CreateService().Run(Request);
        var json = JsonNode.Parse(ContractJson.Serialize(result))!;

        Assert.Null(images.Rooms);
        Assert.Null(result.Summary.Images);
        Assert.False(json["summary"]!.AsObject().ContainsKey("images"));
        Assert.False(json["summary"]!["options"]!.AsObject().ContainsKey("roomImages"));
        ResultSchema.AssertValid(json);
    }

    [Fact]
    public void Contract_WithImages_IsSchemaValid()
    {
        var images = new FakeImages(rooms => new ImageExportResult(1, [rooms[0].ElementId], "export impossible"));
        var pipeline = Create(images);

        var result = pipeline.CreateService().Run(Request);
        var json = JsonNode.Parse(ContractJson.Serialize(result))!;

        Assert.Equal(1600, json["summary"]!["options"]!["imageWidthPx"]!.GetValue<int>());
        Assert.Equal(1, json["summary"]!["images"]!["exported"]!.GetValue<int>());
        ResultSchema.AssertValid(json);
    }

    [Theory]
    [InlineData("4bc8438f-82e8-44a7-86c0-dc6e34bf741f£64SDF_SOE_DC2102_05_PRO_BIM_002923_V5.rvt", "4bc8438f-82e8-44a7-86c0-dc6e34bf741f£64SDF-SOE-DC2102-05-PRO-BIM-002923-V5")]
    [InlineData("Projet_détaché.rvt", "Projet")]
    [InlineData("A.rvt", "A")]
    public void CodeFile_FollowsTrc(string fileName, string expected) =>
        Assert.Equal(expected, TrcNaming.CodeFile(fileName));

    [Fact]
    public void RoomImageFileName_MatchesTheRealTrcFiles() =>
        Assert.Equal(
            "4e29b322-ab78-4312-9be2-dfef7a60bfb0-0028f777_4bc8438f-82e8-44a7-86c0-dc6e34bf741f£64SDF-SOE-DC2102-05-PRO-BIM-002923-V5_2684791_AdjustedCornerView_1.jpg",
            TrcNaming.RoomImageFileName(
                "4e29b322-ab78-4312-9be2-dfef7a60bfb0-0028f777",
                "4bc8438f-82e8-44a7-86c0-dc6e34bf741f£64SDF-SOE-DC2102-05-PRO-BIM-002923-V5",
                2684791));
}
