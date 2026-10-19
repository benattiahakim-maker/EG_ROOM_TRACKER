using System.Text.Json.Nodes;
using EgRoomTracker.Application;
using EgRoomTracker.Application.Revit;
using EgRoomTracker.Application.Trc;
using EgRoomTracker.Contracts.Configuration;
using EgRoomTracker.Contracts.Results;

namespace EgRoomTracker.Tests.Application;

/// <summary>Sortie au format TRC (<c>TRCValidationDict.json</c>) : fiches, codes, valeurs, annexes, pk stables.</summary>
public sealed class TrcExportTests
{
    private const string CodeFile = "8bfafb1f-bf94-4493-a738-6c9dc2c1d302£65MAU-SOE-DC2102-05-PRO-BIM-002924";

    private static RoomElementDto Door(string id, string number, double? width, double? height) => new()
    {
        ElementId = id,
        UniqueId = $"u-{id}",
        Category = "Portes",
        Family = "Simple",
        Type = "0915 x 2134",
        Number = number,
        WidthM = width,
        HeightM = height,
    };

    private static RoomElementDto Equipment(string id, string category, string family, string discipline, string? link = null) => new()
    {
        ElementId = id,
        UniqueId = $"u-{id}",
        Category = category,
        Family = family,
        Discipline = discipline,
        LinkName = link,
    };

    private static RoomDto Room(string id, string name, string number, string level = "S04") => new()
    {
        ElementId = id,
        UniqueId = $"uid-{id}",
        Name = name,
        Number = number,
        AreaM2 = 30.034,
        Level = new LevelRef("1", "ul", level),
        VolumeStatus = VolumeStatus.NotComputed,
    };

    private static ExtractionResult Result(params RoomDto[] rooms) => new()
    {
        Summary = new ExtractionSummary
        {
            CorrelationId = "wi",
            ExtractorVersion = "0.1.0",
            Revit = new RevitInfo("2025", null),
            Source = new SourceInfo(CodeFile.Replace("-", "_") + ".rvt", "x.rvt", false),
            StartedAtUtc = DateTimeOffset.UnixEpoch,
            CompletedAtUtc = DateTimeOffset.UnixEpoch,
            Options = new AppliedOptions(null, VolumeMode.Preserve, []),
            Counts = new RoomCounts(rooms.Length, rooms.Length, 0),
            Status = ExtractionStatus.Succeeded,
        },
        Rooms = rooms,
    };

    private static List<JsonObject> Fiches(TrcDocument document) => document.Fiches.OfType<JsonObject>().ToList();

    private static Dictionary<string, List<string?>> Values(TrcDocument document, string bimId)
    {
        var fiches = Fiches(document);
        var pk = fiches.Single(f => f["model"]!.ToString() == "trc.bimobject" && f["fields"]!["bim_id"]!.ToString() == bimId)["pk"]!.ToString();
        return fiches
            .Where(f => f["model"]!.ToString() == "trc.bimobjectpropertyvalue" && f["fields"]!["bim_object_id"]!.ToString() == pk)
            .GroupBy(f => f["fields"]!["bim_property_code_property"]!.ToString())
            .ToDictionary(g => g.Key, g => g.Select(f => f["fields"]!["latest_value"]?.ToString()).ToList());
    }

    [Fact]
    public void Document_has_objects_values_and_setcodefile_only_and_no_status()
    {
        var document = TrcMapper.Map(Result(Room("1", "Local courants faibles", "CFAO-002")));

        var models = Fiches(document).Select(f => f["model"]!.ToString()).Distinct().Order().ToList();
        Assert.Equal(["special.setcodefile", "trc.bimobject", "trc.bimobjectpropertyvalue"], models);
        var setCode = Fiches(document).Last();
        Assert.Equal("special.setcodefile", setCode["model"]!.ToString());
        Assert.Equal(CodeFile + ".rvt", setCode["fields"]!["file_name"]!.ToString());
        Assert.Equal(CodeFile, setCode["fields"]!["code_file"]!.ToString());
        var bimObject = Fiches(document).First();
        Assert.Equal("trc.bimobject", bimObject["model"]!.ToString());
        Assert.Equal(("uid-1", CodeFile), (bimObject["fields"]!["bim_id"]!.ToString(), bimObject["fields"]!["code_file"]!.ToString()));
        Assert.StartsWith("trc.bimobject_", bimObject["pk"]!.ToString());
    }

    [Fact]
    public void Identification_username_and_dimensions_follow_trc()
    {
        var room = Room("1", "Local courants faibles opérateurs", "65MAU-CFAO-002") with
        {
            NamedParameters = new Dictionary<string, string?>
            {
                [TrcIdentification.Ouvrage] = "OA1",
                [TrcIdentification.Typologie] = "CFAO",
                [TrcIdentification.LocalCode] = "L-12",
            },
        };

        var values = Values(TrcMapper.Map(Result(room)), "uid-1");

        Assert.Equal(["OA1"], values[TrcCodes.Ouvrage]);
        Assert.Equal(["A"], values[TrcCodes.Building]);
        Assert.Equal(["S04"], values[TrcCodes.Storey]);
        Assert.Equal(["CFAO"], values[TrcCodes.Classification]);
        Assert.Equal(["L-12"], values[TrcCodes.Numero]);
        Assert.Equal(["S04_Localcourantsfaiblesopérateurs_65MAU-CFAO-002"], values[TrcCodes.Username]);
        Assert.Equal(["30.03"], values[TrcCodes.Surface]);
        Assert.DoesNotContain(TrcCodes.Volume, values.Keys); // volume non calculé : aucune valeur, jamais 0
        Assert.DoesNotContain(TrcCodes.Height, values.Keys);
    }

    [Fact]
    public void Volume_is_written_only_when_computed()
    {
        var computed = Room("1", "A", "1") with { VolumeStatus = VolumeStatus.Computed, VolumeM3 = 74.9564 };
        var notComputed = Room("2", "B", "2") with { VolumeStatus = VolumeStatus.NotComputed, VolumeM3 = null };

        var document = TrcMapper.Map(Result(computed, notComputed));

        Assert.Equal(["74.96"], Values(document, "uid-1")[TrcCodes.Volume]);
        Assert.DoesNotContain(TrcCodes.Volume, Values(document, "uid-2").Keys);
    }

    [Fact]
    public void All_room_parameters_become_one_value_each_in_the_revit_parameters_section()
    {
        var room = Room("1", "A", "1") with
        {
            Parameters = new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["Commentaires"] = "Salle serveurs",
                ["Hauteur limite"] = 2.4384,
                ["Numéro d'étage"] = 12L,
                ["Occupation"] = null,
                ["Remarques"] = "   ",
                ["Zone Technique"] = 1L,
            },
        };

        var values = Values(TrcMapper.Map(Result(room)), "uid-1");

        Assert.Equal(["Salle serveurs"], values["c:parametres revit_commentaires"]);
        Assert.Equal(["2.4384"], values["c:parametres revit_hauteur limite"]);
        Assert.Equal(["12"], values["c:parametres revit_numéro d'étage"]);
        Assert.Equal(["1"], values["c:parametres revit_zone technique"]);
        Assert.DoesNotContain("c:parametres revit_occupation", values.Keys);
        Assert.DoesNotContain("c:parametres revit_remarques", values.Keys);
        Assert.Equal(4, values.Keys.Count(code => code.StartsWith("c:parametres revit_", StringComparison.Ordinal)));
    }

    [Fact]
    public void Without_all_parameters_there_is_no_revit_parameter_value()
    {
        var values = Values(TrcMapper.Map(Result(Room("1", "A", "1"))), "uid-1");

        Assert.DoesNotContain(values.Keys, code => code.StartsWith("c:", StringComparison.Ordinal));
    }

    [Fact]
    public void A_long_parameter_value_goes_to_an_annex()
    {
        var room = Room("1", "A", "1") with { Parameters = new Dictionary<string, object?> { ["Notes"] = new string('x', 400) } };

        var document = TrcMapper.Map(Result(room));

        Assert.Equal([TrcMapper.AnnexNote], Values(document, "uid-1")["c:parametres revit_notes"]);
        Assert.Equal(400, Assert.Single(document.Annexes).Text.Length);
    }

    [Fact]
    public void Usernames_are_unique()
    {
        var document = TrcMapper.Map(Result(Room("1", "Bureau", "1"), Room("2", "Bureau", "1")));

        Assert.Equal(["S04_Bureau_1"], Values(document, "uid-1")[TrcCodes.Username]);
        Assert.Equal(["S04_Bureau_1_1"], Values(document, "uid-2")[TrcCodes.Username]);
    }

    [Fact]
    public void Heights_are_separate_and_a_missing_measure_writes_nothing()
    {
        var withObstacle = Room("1", "A", "1") with
        {
            UnboundedHeightM = 3.5,
            Heights = new RoomHeightsDto(3.1, 2.456, 2.456, HeightSources.Measured, 12) { LimitingCategory = "Plafonds", LimitingDocument = "CVC_Reseau" },
        };
        var withoutObstacle = Room("2", "B", "2") with { Heights = new RoomHeightsDto(3.1, null, 3.1, HeightSources.Declared, 9) };

        var document = TrcMapper.Map(Result(withObstacle, withoutObstacle));
        var a = Values(document, "uid-1");
        var b = Values(document, "uid-2");

        Assert.Equal(["2.46"], a[TrcCodes.Height]);
        Assert.Equal(["2.46"], a[TrcCodes.HeightMeasured]);
        Assert.Equal(["3.10"], a[TrcCodes.HeightDeclared]);
        Assert.Equal(["3.50"], a[TrcCodes.HeightUnbounded]);
        Assert.Equal(["Plafonds (CVC_Reseau)"], a[TrcCodes.HeightLimiting]);
        Assert.Equal(["3.10"], b[TrcCodes.Height]);
        Assert.DoesNotContain(TrcCodes.HeightMeasured, b.Keys);
        Assert.DoesNotContain(TrcCodes.HeightLimiting, b.Keys);
    }

    [Fact]
    public void Doors_give_one_value_per_door_in_the_same_order_and_no_zero()
    {
        var room = Room("1", "A", "1") with
        {
            Doors = [Door("11", "1.02", 0.9, 2.04), Door("12", "1.03", null, 0)],
        };

        var values = Values(TrcMapper.Map(Result(room)), "uid-1");

        Assert.Equal(["2"], values[TrcCodes.DoorCount]);
        Assert.Equal(["1.02", "1.03"], values[TrcCodes.DoorNumber]);
        Assert.Equal(["11", "12"], values[TrcCodes.DoorId]);
        Assert.Equal(["Simple", "Simple"], values[TrcCodes.DoorFamily]);
        Assert.Equal(["0915 x 2134", "0915 x 2134"], values[TrcCodes.DoorType]);
        Assert.Equal(["0.90m", null], values[TrcCodes.DoorWidth]);
        Assert.Equal(["2.04", null], values[TrcCodes.DoorHeight]);
    }

    [Fact]
    public void Room_without_door_has_a_count_of_zero_and_no_door_value()
    {
        var values = Values(TrcMapper.Map(Result(Room("1", "A", "1") with { Doors = [] })), "uid-1");

        Assert.Equal(["0"], values[TrcCodes.DoorCount]);
        Assert.DoesNotContain(TrcCodes.DoorWidth, values.Keys);
    }

    [Fact]
    public void Equipment_and_terminals_are_ranged_by_discipline_with_the_link_name()
    {
        var room = Room("1", "A", "1") with
        {
            Equipment =
            [
                Equipment("1", "Équipements mécaniques", "CTA", ElementDisciplines.Cvc),
                Equipment("2", "Équipements électriques", "Armoire", ElementDisciplines.Electrical, "ELEC_Reseau"),
                Equipment("3", "Équipements spécialisés", "Extincteur", ElementDisciplines.Other),
            ],
            Terminals =
            [
                Equipment("4", "Terminaux d'air", "Diffuseur", ElementDisciplines.Cvc),
                Equipment("5", "Terminaux d'air", "Diffuseur", ElementDisciplines.Cvc),
                Equipment("6", "Luminaires", "LED 600", ElementDisciplines.Electrical, "ELEC_Reseau"),
            ],
        };

        var values = Values(TrcMapper.Map(Result(room)), "uid-1");

        Assert.Equal(["6 équipements présents dans le local"], values[TrcCodes.EquipmentTotal]);
        Assert.Equal(["3 équipements présents dans le local"], values[TrcCodes.EquipmentQuantity("cvc")]);
        Assert.Equal(["Terminaux d'air : Diffuseur × 2 | Équipements mécaniques : CTA × 1"], values[TrcCodes.EquipmentList("cvc")]);
        Assert.Equal(["0 équipements présents dans le local"], values[TrcCodes.EquipmentQuantity("plomberie")]);
        Assert.DoesNotContain(TrcCodes.EquipmentList("plomberie"), values.Keys);
        Assert.Equal(["Luminaires : LED 600 [ELEC_Reseau] × 1 | Équipements électriques : Armoire [ELEC_Reseau] × 1"], values[TrcCodes.EquipmentList("electricite")]);
        Assert.Equal(["Équipements spécialisés : Extincteur × 1"], values[TrcCodes.EquipmentList("autres")]);
    }

    [Fact]
    public void Long_values_go_to_an_annex()
    {
        var items = Enumerable.Range(0, 30).Select(i => Equipment(i.ToString(), "Luminaires", $"Famille numéro {i}", ElementDisciplines.Electrical)).ToList();
        var room = Room("1", "A", "1") with { Equipment = items };

        var document = TrcMapper.Map(Result(room));
        var values = Values(document, "uid-1");

        Assert.Equal([TrcMapper.AnnexNote], values[TrcCodes.EquipmentList("electricite")]);
        var annex = Assert.Single(document.Annexes);
        Assert.EndsWith(".txt", annex.FileName);
        Assert.Contains("Famille numéro 29", annex.Text);
        Assert.True(annex.Text.Length > TrcMapper.MaxValueLength);
        Assert.All(Fiches(document).Where(f => f["model"]!.ToString() == "trc.bimobjectpropertyvalue"),
            f => Assert.True((f["fields"]!["latest_value"]?.ToString().Length ?? 0) <= TrcMapper.MaxValueLength));
    }

    [Fact]
    public void Primary_keys_are_stable_between_runs_and_distinct_per_value()
    {
        var room = Room("1", "A", "1") with { Doors = [Door("11", "1", 0.9, 2.0), Door("12", "2", 0.9, 2.0)] };

        var first = Fiches(TrcMapper.Map(Result(room))).Select(f => f["pk"]?.ToString()).Where(pk => pk is not null).ToList();
        var second = Fiches(TrcMapper.Map(Result(room))).Select(f => f["pk"]?.ToString()).Where(pk => pk is not null).ToList();

        Assert.Equal(first, second);
        Assert.Equal(first.Count, first.Distinct().Count());
        Assert.All(first, pk => Assert.Matches("^trc\\.(bimobject|bimobjectpropertyvalue)_[0-9a-f]{8}-[0-9a-f]{4}-5[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$", pk!));
    }

    [Fact]
    public void Missing_source_is_refused()
    {
        var result = Result(Room("1", "A", "1"));
        result = result with { Summary = result.Summary with { Source = null } };

        Assert.Throws<InvalidOperationException>(() => TrcMapper.Map(result));
    }

    [Fact]
    public void Export_service_writes_the_file_atomically_with_annexes_and_utf8_without_bom()
    {
        var dir = Directory.CreateTempSubdirectory("egr-trc-").FullName;
        try
        {
            var items = Enumerable.Range(0, 30).Select(i => Equipment(i.ToString(), "Luminaires", $"Famille {i}", ElementDisciplines.Electrical)).ToList();
            var room = Room("1", "Local opérateurs", "1") with { Equipment = items };

            var file = new TrcExportService().Write(Result(room), Path.Combine(dir, "result"));

            Assert.Equal("TRCValidationDict.json", file);
            var path = Path.Combine(dir, "result", file);
            var bytes = File.ReadAllBytes(path);
            Assert.NotEqual(0xEF, bytes[0]);
            var text = File.ReadAllText(path);
            Assert.Contains("S04_Localopérateurs_1", text);
            Assert.Contains("£", text);
            Assert.True(JsonNode.Parse(text) is JsonArray);
            Assert.Single(Directory.EnumerateFiles(Path.Combine(dir, "result", "Details_Annexes")));
            Assert.Empty(Directory.EnumerateFiles(Path.Combine(dir, "result"), ".*.tmp"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ---- Orchestration ----

    private sealed class FakeTrc : ITrcExportService
    {
        public int Calls { get; private set; }

        public ExtractionResult? Written { get; private set; }

        public string Write(ExtractionResult result, string directory)
        {
            Calls++;
            Written = result;
            return "TRCValidationDict.json";
        }
    }

    private static Pipeline Create(OutputFormat format, FakeTrc? trc) => new()
    {
        LoadSettings = () => new ExtractionSettings { SchemaVersion = "1.0", OutputFormat = format },
        TrcExport = trc,
        NamedParameters = (_, names) => names.ToDictionary(n => n, n => (string?)$"v-{n}"),
    };

    [Fact]
    public void Contract_format_writes_only_result_json_and_reads_no_named_parameter()
    {
        var trc = new FakeTrc();
        var pipeline = Create(OutputFormat.Contract, trc);
        var namedReads = 0;
        pipeline.NamedParameters = (_, _) =>
        {
            namedReads++;
            return null;
        };

        var result = pipeline.CreateService().Run(Pipeline.Request);

        Assert.Contains("export", pipeline.Calls);
        Assert.Equal(0, trc.Calls);
        Assert.Equal(0, namedReads);
        Assert.Null(result.Summary.Options.OutputFormat);
    }

    [Fact]
    public void Trc_format_writes_only_the_trc_file_and_reads_the_identification_parameters()
    {
        var trc = new FakeTrc();
        var pipeline = Create(OutputFormat.Trc, trc);

        var result = pipeline.CreateService().Run(Pipeline.Request);

        Assert.DoesNotContain("export", pipeline.Calls);
        Assert.Equal(1, trc.Calls);
        Assert.All(trc.Written!.Rooms, room => Assert.Equal("v-SGP-DI-Ouvrage", room.NamedParameters![TrcIdentification.Ouvrage]));
        Assert.Equal("trc", result.Summary.Options.OutputFormat);
    }

    [Fact]
    public void Both_formats_write_both_files()
    {
        var trc = new FakeTrc();
        var pipeline = Create(OutputFormat.Both, trc);

        var result = pipeline.CreateService().Run(Pipeline.Request);

        Assert.Contains("export", pipeline.Calls);
        Assert.Equal(1, trc.Calls);
        Assert.Equal("both", result.Summary.Options.OutputFormat);
    }

    [Fact]
    public void A_failed_extraction_in_trc_format_still_writes_result_json_and_no_trc_file()
    {
        var trc = new FakeTrc();
        var pipeline = Create(OutputFormat.Trc, trc);
        pipeline.CollectException = new InvalidOperationException("collecte");

        var result = pipeline.CreateService().Run(Pipeline.Request);

        Assert.Equal(ExtractionStatus.Failed, result.Summary.Status);
        Assert.Contains("export", pipeline.Calls);
        Assert.Equal(0, trc.Calls);
    }

    [Fact]
    public void Trc_format_without_the_service_falls_back_to_a_failed_contract_result()
    {
        var pipeline = Create(OutputFormat.Trc, null);

        var result = pipeline.CreateService().Run(Pipeline.Request);

        Assert.Equal(ExtractionStatus.Failed, result.Summary.Status);
        Assert.Contains(result.Summary.Errors, e => e.Code == EgRoomTracker.Contracts.Codes.ErrorCodes.ExportFailed);
    }
}
