using EgRoomTracker.Aps.DesignAutomation;
using EgRoomTracker.Aps.Tests;
using EgRoomTracker.DaManager.Services;
using static EgRoomTracker.DaManager.Tests.FakeApsServer;

namespace EgRoomTracker.DaManager.Tests;

public sealed class JournalTests
{
    private const string Json = """
        {
          "format": "egr-publication-journal",
          "version": 1,
          "entries": [
            {
              "date": "2026-10-02",
              "commit": "9a9bbb2",
              "revit": "2026",
              "appBundle": { "test": 3, "prod": 3 },
              "activity": { "test": 5, "prod": 6 },
              "previousProd": { "appBundle": 2, "activity": 4 }
            },
            {
              "date": "2026-09-20",
              "commit": null,
              "revit": "2025",
              "appBundle": { "test": 1, "prod": 1 },
              "activity": { "test": 1, "prod": 2 },
              "previousProd": null
            }
          ]
        }
        """;

    private const string Markdown = """
        Publication du 2 octobre :

        | Date | Commit | Revit | AppBundle test / prod | Activity test / prod | Prod précédente (AppBundle / Activity) |
        | --- | --- | --- | --- | --- | --- |
        | 2026-10-02 | `9a9bbb2` | 2026 | v3 / v3 | 5 / 6 | v2 / v4 |
        | 2026-09-20 | — | 2025 | v1 / v1 | v1 / v2 | — |
        """;

    [Fact]
    public void Json_and_markdown_describe_the_same_journal()
    {
        var fromJson = PublicationJournal.ParseJson(Json);
        var fromMarkdown = PublicationJournal.ParseMarkdown(Markdown);

        Assert.Equal(fromJson.Entries, fromMarkdown.Entries);
        Assert.Equal(["2025", "2026"], fromJson.Entries.Select(e => e.Revit)); // triées par date
        var entry = fromJson.Entries[1];
        Assert.Equal(new AliasVersions(5, 6), entry.Activity);
        Assert.Equal(new PreviousProd(2, 4), entry.PreviousProd);
        Assert.Null(fromJson.Entries[0].Commit);
        Assert.Null(fromJson.Entries[0].PreviousProd);
    }

    [Fact]
    public void Export_is_readable_and_round_trips()
    {
        var journal = PublicationJournal.ParseJson(Json);

        var json = journal.ToJson();
        var markdown = journal.ToMarkdown();

        Assert.Contains("\n  \"entries\": [\n", json, StringComparison.Ordinal); // indenté
        Assert.Contains("\"date\": \"2026-10-02\"", json, StringComparison.Ordinal);
        Assert.Equal(journal.Entries, PublicationJournal.ParseJson(json).Entries);
        Assert.StartsWith("| Date | Commit | Revit | AppBundle test / prod | Activity test / prod | Prod précédente (AppBundle / Activity) |\n| --- |", markdown, StringComparison.Ordinal);
        Assert.Contains("| 2026-10-02 | 9a9bbb2 | 2026 | v3 / v3 | v5 / v6 | v2 / v4 |", markdown, StringComparison.Ordinal);
        Assert.Contains("| 2026-09-20 | — | 2025 | v1 / v1 | v1 / v2 | — |", markdown, StringComparison.Ordinal);
        Assert.Equal(journal.Entries, PublicationJournal.ParseMarkdown(markdown).Entries);
    }

    [Fact]
    public void Save_and_load_follow_the_extension()
    {
        using var temp = new TempDirectory();
        var journal = PublicationJournal.ParseJson(Json);
        var json = Path.Combine(temp.Path, "sub", "journal.json");
        var markdown = Path.Combine(temp.Path, "journal.md");

        journal.Save(json);
        journal.Save(markdown);

        Assert.StartsWith("{", File.ReadAllText(json), StringComparison.Ordinal);
        Assert.StartsWith("| Date", File.ReadAllText(markdown), StringComparison.Ordinal);
        Assert.Equal(journal.Entries, PublicationJournal.Load(json).Entries);
        Assert.Equal(journal.Entries, PublicationJournal.Load(markdown).Entries);
        Assert.Empty(PublicationJournal.Load(Path.Combine(temp.Path, "absent.json")).Entries);
    }

    [Fact]
    public void Protected_versions_are_per_revit_version_and_kind()
    {
        var journal = PublicationJournal.ParseJson(Json);

        Assert.Equal([2, 3], journal.ProtectedVersions("2026", DaResourceKind.AppBundle));
        Assert.Equal([4, 5, 6], journal.ProtectedVersions("2026", DaResourceKind.Activity));
        Assert.Equal([1, 2], journal.ProtectedVersions("2025", DaResourceKind.Activity));
        Assert.Empty(journal.ProtectedVersions("2027", DaResourceKind.Activity));
    }

    [Fact]
    public void Merge_ignores_duplicates()
    {
        var journal = PublicationJournal.ParseJson(Json);

        var merged = journal.Merge(PublicationJournal.ParseMarkdown(Markdown).Entries);

        Assert.Equal(2, merged.Entries.Count);
    }

    [Theory]
    [InlineData("{ pas du json", "illisible")]
    [InlineData("""{ "format": "autre", "version": 1, "entries": [] }""", "format")]
    [InlineData("""{ "format": "egr-publication-journal", "version": 2, "entries": [] }""", "non prise en charge")]
    [InlineData("""{ "format": "egr-publication-journal", "version": 1, "entries": [ { "date": "02/10/2026", "revit": "2026", "appBundle": {}, "activity": {} } ] }""", "entrée 1 : date")]
    [InlineData("""{ "format": "egr-publication-journal", "version": 1, "entries": [ { "date": "2026-10-02", "revit": "2019", "appBundle": {}, "activity": {} } ] }""", "Revit « 2019 »")]
    [InlineData("""{ "format": "egr-publication-journal", "version": 1, "entries": [ { "date": "2026-10-02", "revit": "2026", "appBundle": { "test": 0 }, "activity": {} } ] }""", "version 0")]
    [InlineData("""{ "format": "egr-publication-journal", "version": 1, "entries": [ { "date": "2026-10-02", "revit": "2026", "appBundle": { "test": "3" }, "activity": {} } ] }""", "entier attendu")]
    public void Invalid_json_is_rejected_with_a_clear_message(string json, string expected)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => PublicationJournal.ParseJson(json));

        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("| 2026-10-02 | abc | 2026 | v3 | v5 / v6 | — |", "ligne 1 : « v3 »")]
    [InlineData("| 2026-10-02 | abc | 2026 | v3 / x | v5 / v6 | — |", "version « x »")]
    [InlineData("| 2026-10-02 | abc | 2026 | v3 / v3 |", "6 colonnes")]
    public void Invalid_markdown_is_rejected_with_the_line(string markdown, string expected)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => PublicationJournal.ParseMarkdown(markdown));

        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Options_read_the_journal_path()
    {
        var options = DaManagerOptions.FromEnvironment(new Dictionary<string, string?> { ["EGR_PUBLICATION_JOURNAL"] = "/data/journal.md", ["EGR_REPO_ROOT"] = "/r" });
        var fallback = DaManagerOptions.FromEnvironment(new Dictionary<string, string?> { ["EGR_REPO_ROOT"] = "/r" });

        Assert.Equal("/data/journal.md", options.JournalPath);
        Assert.Equal(DaManagerOptions.DefaultJournalPath(), fallback.JournalPath);
        Assert.EndsWith("publication-journal.json", fallback.JournalPath, StringComparison.Ordinal);
    }

    // ---- ViewModel -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Cleanup_automatically_protects_journal_versions()
    {
        using var env = new TestEnvironment();
        PublicationJournal.ParseJson(Json).Save(env.JournalPath);
        env.Server.AppBundles["EgRoomTracker_R2026"] = Resource.With(Enumerable.Range(1, 12), ("prod", 12), ("test", 12));
        await env.ViewModel.RefreshCommand.ExecuteAsync();
        env.ViewModel.SelectedResource = env.ViewModel.Resources.Single(r => r.Id == "EgRoomTracker_R2026");
        env.ViewModel.KeepLatest = 1;
        env.ViewModel.ProtectedVersions = "7";
        env.Confirmation.Answer = true;

        await env.ViewModel.CleanupCommand.ExecuteAsync();

        var plan = env.Confirmation.Plans.Single().Text;
        Assert.Contains("conserver v2 (protégée (journal))", plan, StringComparison.Ordinal);
        Assert.Contains("conserver v3 (protégée (journal))", plan, StringComparison.Ordinal);
        Assert.Contains("Journal de publication : v2, v3 protégée(s) automatiquement.", plan, StringComparison.Ordinal);
        Assert.Equal([2, 3, 7, 12], env.Server.AppBundles["EgRoomTracker_R2026"].Versions);
    }

    [Fact]
    public async Task Unreadable_journal_blocks_cleanup()
    {
        using var env = new TestEnvironment();
        Directory.CreateDirectory(Path.GetDirectoryName(env.JournalPath)!);
        File.WriteAllText(env.JournalPath, "{ cassé");
        env.Server.AppBundles["EgRoomTracker_R2026"] = Resource.With(Enumerable.Range(1, 12), ("test", 12));
        await env.ViewModel.RefreshCommand.ExecuteAsync();
        env.ViewModel.SelectedResource = env.ViewModel.Resources.Single(r => r.Id == "EgRoomTracker_R2026");
        env.ViewModel.KeepLatest = 1;
        env.Confirmation.Answer = true;

        await env.ViewModel.CleanupCommand.ExecuteAsync();

        Assert.Empty(env.Confirmation.Plans);
        Assert.Empty(env.Server.Writes);
        Assert.Contains("Journal JSON illisible", env.LogText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Promotion_is_recorded_in_the_journal()
    {
        using var env = new TestEnvironment();
        env.Server.AppBundles["EgRoomTracker_R2026"] = Resource.With([1, 2, 3], ("test", 3), ("prod", 2));
        env.Server.Activities["ExtractRooms_R2026"] = Resource.With([1, 2, 3], ("test", 3), ("prod", 2));
        env.Confirmation.Answer = true;

        await env.ViewModel.PromoteCommand.ExecuteAsync();

        var entry = Assert.Single(PublicationJournal.Load(env.JournalPath).Entries);
        Assert.Equal("2026", entry.Revit);
        Assert.Equal(new AliasVersions(3, 3), entry.AppBundle);
        Assert.Equal(new AliasVersions(3, 4), entry.Activity);
        Assert.Equal(new PreviousProd(2, 2), entry.PreviousProd);
        Assert.Equal(DateTimeOffset.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), entry.Date);
        Assert.Equal(entry, Assert.Single(env.ViewModel.Journal));
        Assert.Contains("Journal de publication : ", env.LogText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Declined_promotion_records_nothing()
    {
        using var env = new TestEnvironment();
        env.Server.AppBundles["EgRoomTracker_R2026"] = Resource.With([1, 2, 3], ("test", 3), ("prod", 2));
        env.Server.Activities["ExtractRooms_R2026"] = Resource.With([1, 2, 3], ("test", 3), ("prod", 2));

        await env.ViewModel.PromoteCommand.ExecuteAsync();

        Assert.False(File.Exists(env.JournalPath));
        Assert.Empty(env.ViewModel.Journal);
    }

    [Fact]
    public async Task Import_merges_markdown_and_export_writes_json()
    {
        using var env = new TestEnvironment();
        var source = Path.Combine(env.Root, "pr-description.md");
        File.WriteAllText(source, Markdown);
        env.Files.NextOpen = source;

        await env.ViewModel.ImportJournalCommand.ExecuteAsync();
        await env.ViewModel.ImportJournalCommand.ExecuteAsync(); // second import : aucun doublon

        Assert.Equal(2, PublicationJournal.Load(env.JournalPath).Entries.Count);
        Assert.Equal(2, env.ViewModel.Journal.Count);
        Assert.Equal("2026", env.ViewModel.Journal[0].Revit); // plus récente en tête
        Assert.Contains("2 ligne(s) lue(s), 2 nouvelle(s)", env.LogText, StringComparison.Ordinal);
        Assert.Contains("2 ligne(s) lue(s), 0 nouvelle(s)", env.LogText, StringComparison.Ordinal);

        env.Files.NextSave = Path.Combine(env.Root, "export.json");
        await env.ViewModel.ExportJournalCommand.ExecuteAsync();

        Assert.Equal(PublicationJournal.ParseMarkdown(Markdown).Entries, PublicationJournal.ParseJson(File.ReadAllText(env.Files.NextSave)).Entries);
        Assert.Empty(env.Server.Requests);
    }

    [Fact]
    public async Task Invalid_import_leaves_the_journal_untouched()
    {
        using var env = new TestEnvironment();
        PublicationJournal.ParseJson(Json).Save(env.JournalPath);
        var before = File.ReadAllText(env.JournalPath);
        var source = Path.Combine(env.Root, "bad.md");
        File.WriteAllText(source, "| 2026-10-02 | abc | 2026 | v3 / x | v5 / v6 | — |");
        env.Files.NextOpen = source;

        await env.ViewModel.ImportJournalCommand.ExecuteAsync();

        Assert.Equal(before, File.ReadAllText(env.JournalPath));
        Assert.Contains("Journal Markdown, ligne 1", env.LogText, StringComparison.Ordinal);
    }
}
