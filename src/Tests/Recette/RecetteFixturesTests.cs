using System.Globalization;
using System.Text.Json;
using EgRoomTracker.Application;
using EgRoomTracker.Application.Configuration;
using EgRoomTracker.Application.Quality;
using EgRoomTracker.Application.Revit;
using EgRoomTracker.Contracts.Codes;
using EgRoomTracker.Contracts.Configuration;
using EgRoomTracker.Contracts.Results;
using EgRoomTracker.Export;
using EgRoomTracker.Tests.Support;

namespace EgRoomTracker.Tests.Recette;

/// <summary>
/// Jeu de recette versionné (EGR-18, <c>docs/recette</c>) : chaque résultat attendu est conforme au schéma, représentable
/// par les contrats et l'export de production, et reproduit le comportement actuel du code hors Revit (indicateurs,
/// anomalies et messages produits par les fabriques de production, contrôle qualité, erreurs globales, lecture des
/// settings). Les valeurs lues dans Revit (quantités, textes) restent à confirmer sur les maquettes (EGR-14).
/// </summary>
public class RecetteFixturesTests
{
    private static readonly string[] ReadingCodes =
        [QualityFlagCodes.Unplaced, QualityFlagCodes.MissingParameter, QualityFlagCodes.LevelNotFound, QualityFlagCodes.ExtractionError];

    public static TheoryData<string> Cases()
    {
        var data = new TheoryData<string>();
        foreach (var id in CaseIds())
        {
            data.Add(id);
        }

        return data;
    }

    /// <summary>
    /// Jeux d'options des gabarits (<c>designOptionSets.sets</c> du manifeste : nom du jeu → ElementId des variantes),
    /// renvoyés en table variante → jeu. Le jeu n'est pas exporté dans result.json (champ interne
    /// <c>RoomDto.DesignOptionSetId</c>) : le manifeste le fournit au contrôle qualité. Variante absente : jeu inconnu.
    /// </summary>
    private static Dictionary<string, string> DesignOptionSets() =>
        Manifest().RootElement.TryGetProperty("designOptionSets", out var root) && root.TryGetProperty("sets", out var sets)
            ? sets.EnumerateObject()
                .SelectMany(set => set.Value.EnumerateArray().Select(option => (Option: option.GetString()!, Set: set.Name)))
                .ToDictionary(x => x.Option, x => x.Set)
            : [];

    [Fact]
    public void Manifest_ListsEveryExpectedFile()
    {
        var listed = CaseIds().Order(StringComparer.Ordinal).ToList();
        var files = Directory.EnumerateFiles(RecettePath("expected"), "*.json")
            .Select(f => Path.GetFileNameWithoutExtension(f)!).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(files, listed);
        Assert.True(listed.Count >= 10);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Expected_IsSchemaValid_WithCurrentSchemaVersion(string id)
    {
        var json = File.ReadAllText(ExpectedPath(id));
        ResultSchema.AssertValid(json);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(SchemaVersions.Result, document.RootElement.GetProperty("schemaVersion").GetString());
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Expected_RoundTripsThroughContracts_AndProductionExporter(string id)
    {
        var json = File.ReadAllText(ExpectedPath(id));
        var exported = System.Text.Encoding.UTF8.GetString(ResultExportService.Serialize(ContractJson.Deserialize(json)));
        AssertJsonEquivalent(json, exported);
    }

    /// <summary>
    /// Rejoue le vrai contrôle qualité sur tous les cas, y compris R01, R02, R06 et R11 (règle DUPLICATE_NUMBER par
    /// contexte phase / variante, ex-<c>pendingImplementation</c>, implémentée par la PR #45).
    /// </summary>
    [Theory]
    [MemberData(nameof(Cases))]
    public void Expected_ReproducesQualityCheck_AndStatus(string id) => AssertQualityCheckReproduces(id);

    [Fact]
    public void NoCaseIsPendingImplementation() =>
        Assert.DoesNotContain(Manifest().RootElement.GetProperty("cases").EnumerateArray(), c => c.TryGetProperty("pendingImplementation", out _));

    [Fact]
    public void DesignOptionSets_CoverEveryVariantOfTheCase()
    {
        var sets = DesignOptionSets();
        foreach (var id in CaseIds())
        {
            var expected = ContractJson.Deserialize(File.ReadAllText(ExpectedPath(id)));
            Assert.All(expected.Rooms.Where(r => r.DesignOption is not null), r => Assert.True(sets.ContainsKey(r.DesignOption!.ElementId), $"{id} : jeu de la variante {r.DesignOption.ElementId} non déclaré"));
        }
    }

    private static void AssertQualityCheckReproduces(string id)
    {
        var expected = ContractJson.Deserialize(File.ReadAllText(ExpectedPath(id)));
        if (expected.Summary.Status == ExtractionStatus.Failed)
        {
            Assert.Empty(expected.Rooms);
            Assert.Empty(expected.Anomalies);
            Assert.Equal(new RoomCounts(0, 0, 0), expected.Summary.Counts);
            return;
        }

        // Entrée du contrôle qualité : sortie de la lecture (indicateurs et anomalies de lecture, anomalies document
        // hors VOLUME_NOT_COMPUTED), état du volume déduit de l'anomalie VOLUME_NOT_COMPUTED.
        var volumesComputed = expected.Anomalies.All(a => a.Code != DocumentAnomalyCodes.VolumeNotComputed);
        var sets = DesignOptionSets();
        var rooms = expected.Rooms.Select(r => r with
        {
            QualityFlags = r.QualityFlags.Where(ReadingCodes.Contains).ToList(),
            DesignOptionSetId = r.DesignOption is { } option ? sets.GetValueOrDefault(option.ElementId) : null,
        }).ToList();
        var input = expected.Anomalies
            .Where(a => a.Scope == AnomalyScope.Document ? a.Code != DocumentAnomalyCodes.VolumeNotComputed : ReadingCodes.Contains(a.Code))
            .ToList();

        var report = new QualityCheckService().Evaluate(rooms, input, new VolumeComputationState(expected.Summary.Options.VolumeMode, volumesComputed));

        Assert.Equal(expected.Summary.Status, report.Status);
        AssertJsonEquivalent(JsonSerializer.Serialize(expected.Anomalies, ContractJson.Options), JsonSerializer.Serialize(report.Anomalies, ContractJson.Options));
        var sorted = RoomExtractionService.SortByElementId(report.Rooms);
        Assert.Equal(expected.Rooms.Select(r => r.ElementId), sorted.Select(r => r.ElementId));
        Assert.Equal(expected.Rooms.Select(r => string.Join(",", r.QualityFlags)), sorted.Select(r => string.Join(",", r.QualityFlags)));
        Assert.Equal(expected.Rooms.Count, expected.Summary.Counts.RoomsCollected);
        Assert.Equal(expected.Rooms.Count, expected.Summary.Counts.RoomsExported);
        Assert.Equal(expected.Rooms.Count(r => r.QualityFlags.Count > 0), expected.Summary.Counts.RoomsWithAnomalies);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Expected_Anomalies_MatchProductionFactories(string id)
    {
        var expected = ContractJson.Deserialize(File.ReadAllText(ExpectedPath(id)));
        var settings = TryReadSettings(id);
        foreach (var anomaly in expected.Anomalies.Where(a => a.Code != DocumentAnomalyCodes.VolumeNotComputed))
        {
            var details = anomaly.Details.ToDictionary(kv => kv.Key, kv => kv.Value is JsonElement e ? (e.ValueKind == JsonValueKind.Null ? null : e.ToString()) : kv.Value?.ToString());
            var produced = anomaly.Code switch
            {
                DocumentAnomalyCodes.LinkMissing => DocumentAnomalies.LinkMissing(details["linkName"]!, details["linkPath"]),
                DocumentAnomalyCodes.LinkNotLoaded => DocumentAnomalies.LinkNotLoaded(details["linkName"]!, details["linkPath"], details["linkStatus"]!),
                DocumentAnomalyCodes.SharedParameterNotFound => SharedParameterAnomalies.NotFound(Mapping(settings!, details["key"]!)),
                DocumentAnomalyCodes.UnsupportedParameterType => SharedParameterAnomalies.UnsupportedType(Mapping(settings!, details["key"]!), details["typeId"]!),
                QualityFlagCodes.Unplaced => ReadingAnomaly(anomaly, b => b.Placement(isPlaced: false)),
                QualityFlagCodes.MissingParameter => ReadingAnomaly(anomaly, b => b.SharedParameters([(Mapping(settings!, details["parameter"]!), SharedParameterRead.Absent)])),
                _ => null,
            };
            if (produced is null)
            {
                Assert.Contains(anomaly.Code, new[] { QualityFlagCodes.ZeroArea, QualityFlagCodes.DuplicateNumber }); // contrôle qualité
                continue;
            }

            AssertJsonEquivalent(JsonSerializer.Serialize(anomaly, ContractJson.Options), JsonSerializer.Serialize(produced, ContractJson.Options));
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Settings_AreReadAsExpected_AndResolveTheExpectedModel(string id)
    {
        var expected = ContractJson.Deserialize(File.ReadAllText(ExpectedPath(id)));
        var error = expected.Summary.Errors.FirstOrDefault();
        var json = File.ReadAllText(SettingsPath(id));
        if (error?.Code == ErrorCodes.ConfigSchemaUnsupported)
        {
            var ex = Assert.Throws<ExtractionFailedException>(() => SettingsReader.Parse(json));
            AssertError(error, ex.Error);
            return;
        }

        var settings = SettingsReader.Parse(json);
        if (expected.Summary.Source is null && error?.Code is ErrorCodes.RvtAmbiguous)
        {
            using var dir = ZipTree(id);
            var ex = Assert.Throws<ExtractionFailedException>(() => MainModelResolver.Resolve(dir.Path, settings.MainModelPath));
            AssertError(error, ex.Error);
            return;
        }

        Assert.Equal(expected.Summary.Options.PhaseIds, settings.PhaseIds);
        Assert.Equal(expected.Summary.Options.VolumeMode, settings.VolumeMode);
        Assert.Equal(expected.Summary.Options.SharedParameterKeys, settings.SharedParameters.Select(m => m.Key));
        using (var dir = ZipTree(id))
        {
            var resolved = MainModelResolver.Resolve(dir.Path, settings.MainModelPath);
            Assert.Equal(Model(id), resolved);
            if (expected.Summary.Source is { } source)
            {
                Assert.Equal(source.RelativePath, resolved);
                Assert.Equal(Path.GetFileName(resolved), source.FileName);
            }
        }
    }

    [Fact]
    public void FailureMessages_AreTheOnesProducedByTheCode()
    {
        var unknownPhase = Assert.Throws<ExtractionFailedException>(() => PhaseSelection.EnsureKnown(new HashSet<long> { 999999999 }, new HashSet<long> { 86961 }));
        AssertError(ExpectedError("R04"), unknownPhase.Error);

        var incompatible = Assert.Throws<ExtractionFailedException>(() => RevitVersionCompatibility.Check("2027", savedInLaterVersion: false, engineVersion: "2026"));
        AssertError(ExpectedError("R09"), incompatible.Error);
    }

    // ------------------------------------------------------------------------------------------------ outils

    private static List<string> CaseIds() =>
        Manifest().RootElement.GetProperty("cases").EnumerateArray().Select(c => c.GetProperty("id").GetString()!).ToList();

    private static JsonDocument Manifest() => JsonDocument.Parse(File.ReadAllText(RecettePath("cases.json")));

    private static JsonElement Case(string id) =>
        Manifest().RootElement.GetProperty("cases").EnumerateArray().Single(c => c.GetProperty("id").GetString() == id).Clone();

    private static string RecettePath(params string[] parts) => Path.Combine([AppContext.BaseDirectory, "Data", "recette", .. parts]);

    private static string ExpectedPath(string id) => RecettePath("expected", $"{id}.json");

    private static string SettingsPath(string id) => RecettePath("settings", $"{id}.json");

    private static string? Model(string id) => Case(id).GetProperty("model").GetString();

    private static GlobalError ExpectedError(string id) =>
        Assert.Single(ContractJson.Deserialize(File.ReadAllText(ExpectedPath(id))).Summary.Errors);

    private static ExtractionSettings? TryReadSettings(string id)
    {
        try
        {
            return SettingsReader.Parse(File.ReadAllText(SettingsPath(id)));
        }
        catch (ExtractionFailedException)
        {
            return null;
        }
    }

    private static SharedParameterMapping Mapping(ExtractionSettings settings, string key) =>
        settings.SharedParameters.Single(m => m.Key == key);

    private static AnomalyDto ReadingAnomaly(AnomalyDto expected, Action<RoomReadBuilder> read)
    {
        var builder = new RoomReadBuilder(long.Parse(expected.ElementId!, CultureInfo.InvariantCulture), "uid", VolumeStatus.NotComputed);
        read(builder);
        return Assert.Single(builder.Build().Anomalies);
    }

    private static void AssertError(GlobalError expected, GlobalError actual)
    {
        Assert.Equal(expected.Code, actual.Code);
        Assert.Equal(expected.Stage, actual.Stage);
        Assert.Equal(expected.Message, actual.Message);
    }

    /// <summary>Arborescence du ZIP du cas (fichiers vides) : sert à la résolution du RVT principal.</summary>
    private static TempTree ZipTree(string id)
    {
        var tree = new TempTree();
        foreach (var entry in Case(id).GetProperty("zip").EnumerateArray())
        {
            var path = Path.Combine(tree.Path, entry.GetString()!);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, []);
        }

        return tree;
    }

    private sealed class TempTree : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("egr18-").FullName;

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    /// <summary>Égalité JSON sémantique : mêmes propriétés (ordre indifférent), nombres comparés en valeur (20 = 20.0).</summary>
    private static void AssertJsonEquivalent(string expected, string actual)
    {
        using var e = JsonDocument.Parse(expected);
        using var a = JsonDocument.Parse(actual);
        var differences = new List<string>();
        Compare(e.RootElement, a.RootElement, "$", differences);
        Assert.True(differences.Count == 0, string.Join("\n", differences.Take(20)));
    }

    private static void Compare(JsonElement e, JsonElement a, string path, List<string> differences)
    {
        if (e.ValueKind != a.ValueKind)
        {
            differences.Add($"{path} : {e} ≠ {a}");
            return;
        }

        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                var keys = e.EnumerateObject().Select(p => p.Name).Union(a.EnumerateObject().Select(p => p.Name));
                foreach (var key in keys)
                {
                    if (!e.TryGetProperty(key, out var ev) || !a.TryGetProperty(key, out var av))
                    {
                        differences.Add($"{path}.{key} : présent d'un seul côté");
                        continue;
                    }

                    Compare(ev, av, $"{path}.{key}", differences);
                }

                break;
            case JsonValueKind.Array:
                if (e.GetArrayLength() != a.GetArrayLength())
                {
                    differences.Add($"{path} : {e.GetArrayLength()} élément(s) ≠ {a.GetArrayLength()}");
                    break;
                }

                for (var i = 0; i < e.GetArrayLength(); i++)
                {
                    Compare(e[i], a[i], $"{path}[{i}]", differences);
                }

                break;
            case JsonValueKind.Number:
                if (e.GetDouble() != a.GetDouble())
                {
                    differences.Add($"{path} : {e} ≠ {a}");
                }

                break;
            default:
                if (e.ToString() != a.ToString())
                {
                    differences.Add($"{path} : {e} ≠ {a}");
                }

                break;
        }
    }
}
