using System.IO.Compression;
using System.Text.Json.Nodes;
using EgRoomTracker.Aps;
using EgRoomTracker.Aps.Tests;
using EgRoomTracker.WorkItemRunner.Services;

namespace EgRoomTracker.WorkItemRunner.Tests;

public sealed class ResultAndHistoryTests
{
    [Fact]
    public void Example_result_is_displayed_and_valid()
    {
        using var env = new TestEnvironment();

        var view = ResultReader.ForRepository(env.Root).Read(env.ExampleResult);

        Assert.True(view.IsSchemaValid, string.Join("\n", view.SchemaErrors));
        Assert.Equal("succeededWithWarnings", view.Status);
        Assert.Equal(3, view.Rooms.Count);
        Assert.Equal(4, view.Anomalies.Count);
        Assert.Contains(view.Rooms, r => r is { Number: "001", Name: "Bureau", Level: "R+0", Phase: "Nouvelle construction" });
        Assert.Contains(view.Summary, kv => kv.Value.Contains("EG_Batiment_A.rvt", StringComparison.Ordinal));
    }

    [Fact]
    public void Non_conforming_result_lists_schema_errors()
    {
        using var env = new TestEnvironment();
        var json = JsonNode.Parse(env.ExampleResult)!.AsObject();
        json.Remove("anomalies");
        json["schemaVersion"] = 13;

        var view = ResultReader.ForRepository(env.Root).Read(json.ToJsonString());

        Assert.False(view.IsSchemaValid);
        Assert.Equal(3, view.Rooms.Count);
        Assert.NotEmpty(ResultReader.ForRepository(env.Root).Validate("{}"));
    }

    [Fact]
    public void Unreadable_result_is_reported_not_thrown()
    {
        using var env = new TestEnvironment();

        var view = ResultReader.ForRepository(env.Root).Read("{ tronqué");

        Assert.False(view.IsSchemaValid);
        Assert.Empty(view.Rooms);
    }

    [Fact]
    public void History_saves_loads_newest_first_and_exports()
    {
        using var temp = new TempDirectory();
        var history = new RunHistory(temp.Path);
        RunRecord Record(string id, int minute) => new()
        {
            RunId = id,
            CreatedAtUtc = new DateTimeOffset(2026, 10, 9, 10, minute, 0, TimeSpan.Zero),
            RevitVersion = "2026",
            Alias = "test",
            ActivityId = "nick.ExtractRooms_R2026+test",
            ZipFileName = "model.zip",
            BucketKey = "b",
        };
        history.Save(Record("old", 1));
        history.Save(Record("new", 2) with { WorkItemId = "wi", WorkItemStatus = "success", Outputs = new Dictionary<string, bool> { ["result.json"] = true } });
        File.WriteAllText(Path.Combine(history.DirectoryOf("new"), "result.json"), "{}");
        Directory.CreateDirectory(Path.Combine(history.RunsDirectory, "debris"));

        var loaded = history.Load();

        Assert.Equal(["new", "old"], loaded.Select(r => r.RunId));
        Assert.True(loaded[0].Outputs["result.json"]);
        Assert.Contains("success", loaded[0].Summary, StringComparison.Ordinal);

        var zip = Path.Combine(temp.Path, "export.zip");
        history.Export("new", zip);
        using var archive = ZipFile.OpenRead(zip);
        Assert.Equal(["result.json", RunHistory.RecordFileName], archive.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Options_come_from_environment_with_sanitized_default_bucket()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(temp.Path, "src", "Packaging", "definitions"));
        File.WriteAllText(Path.Combine(temp.Path, "EgRoomTracker.sln"), string.Empty);
        var nested = Directory.CreateDirectory(Path.Combine(temp.Path, "tools", "bin")).FullName;

        var options = RunnerOptions.FromEnvironment(new Dictionary<string, string?> { ["APS_NICKNAME"] = "Mon Nick", ["APS_TOKEN"] = "jeton-env-secret", ["EGR_RUNNER_DATA"] = temp.Path }, nested);

        Assert.Equal(temp.Path, options.RepositoryRoot);
        Assert.Equal("egr-workitems-mon-nick", options.BucketKey);
        Assert.Equal(temp.Path, options.DataDirectory);
        Assert.Contains(ApsSettings.TokenVariable, options.CredentialSource, StringComparison.Ordinal);
        Assert.DoesNotContain("jeton-env-secret", options.CredentialSource, StringComparison.Ordinal);

        var explicitBucket = RunnerOptions.FromEnvironment(new Dictionary<string, string?> { ["APS_BUCKET"] = "mon-bucket", ["EGR_REPO_ROOT"] = "/r", ["EGR_RUNNER_DATA"] = temp.Path });
        Assert.Equal("mon-bucket", explicitBucket.BucketKey);
        Assert.Equal("/r", explicitBucket.RepositoryRoot);
        Assert.Null(RunnerOptions.DefaultBucketKey(null));
    }
}
