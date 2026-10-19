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
    public void Room_table_columns_follow_the_json_keys()
    {
        using var env = new TestEnvironment();
        var json = JsonNode.Parse(env.ExampleResult)!.AsObject();
        var room = json["rooms"]!.AsArray()[0]!.AsObject();
        room["champInconnuDuFutur"] = 42.5;

        var view = ResultReader.ForRepository(env.Root).Read(json.ToJsonString());

        Assert.Contains("champInconnuDuFutur", view.RoomColumns);
        Assert.Contains("niveau", view.RoomColumns);
        Assert.Contains("finitions.sol", view.RoomColumns);
        Assert.Equal(view.Rooms.Count, view.RoomValues.Count);
        Assert.All(view.RoomValues, row => Assert.Equal(view.RoomColumns.Count, row.Count));
        var column = view.RoomColumns.ToList().IndexOf("champInconnuDuFutur");
        Assert.Equal("42.5", view.RoomValues[0][column]);
        Assert.Null(view.RoomValues[1][column]);
        var level = view.RoomColumns.ToList().IndexOf("niveau");
        Assert.Equal("R+0", view.RoomValues[0][level]);
    }

    [Fact]
    public async Task Last_config_is_saved_on_confirmed_run_and_restored_at_startup()
    {
        using var env = new TestEnvironment();
        env.ViewModel.SelectedAlias = "prod";

        await env.ViewModel.RunCommand.ExecuteAsync();

        var saved = new LastConfigStore(env.DataDirectory).Load();
        Assert.NotNull(saved);
        Assert.Equal("prod", saved!.Alias);
        Assert.Equal(env.ZipPath, saved.ZipPath);
        Assert.Contains("schemaVersion", saved.SettingsText, StringComparison.Ordinal);
    }

    [Fact]
    public void Room_table_summarizes_element_arrays_and_parameters()
    {
        using var env = new TestEnvironment();
        var json = JsonNode.Parse(env.ExampleResult)!.AsObject();
        var room = json["rooms"]!.AsArray()[0]!.AsObject();
        room["portes"] = new JsonArray(
            new JsonObject { ["id_revit"] = "1", ["repere"] = "P1", ["type"] = "0915" },
            new JsonObject { ["id_revit"] = "2", ["repere"] = null, ["type"] = "0915" });
        room["parametres"] = new JsonArray(new JsonObject { ["name"] = "Commentaires", ["value"] = "x" });

        var view = ResultReader.ForRepository(env.Root).Read(json.ToJsonString());

        var columns = view.RoomColumns.ToList();
        Assert.Equal("2 : P1, 0915", view.RoomValues[0][columns.IndexOf("portes")]);
        Assert.Equal("1 élément(s)", view.RoomValues[0][columns.IndexOf("parametres")]);
    }

    [Fact]
    public void Equipment_and_terminals_are_split_by_discipline_columns()
    {
        using var env = new TestEnvironment();
        var json = JsonNode.Parse(env.ExampleResult)!.AsObject();
        var room = json["rooms"]!.AsArray()[0]!.AsObject();
        room["equipements"] = new JsonArray(
            new JsonObject { ["id_revit"] = "1", ["type"] = "CTA", ["discipline"] = "cvc" },
            new JsonObject { ["id_revit"] = "2", ["type"] = "Tableau", ["discipline"] = "electricite" },
            new JsonObject { ["id_revit"] = "3", ["type"] = "TD", ["discipline"] = "electricite" });
        room["terminaux"] = new JsonArray(new JsonObject { ["id_revit"] = "4", ["type"] = "Luminaire", ["discipline"] = "electricite" });

        var view = ResultReader.ForRepository(env.Root).Read(json.ToJsonString());

        var columns = view.RoomColumns.ToList();
        Assert.Equal("1 : CTA", view.RoomValues[0][columns.IndexOf("equipements.cvc")]);
        Assert.Equal("2 : Tableau, TD", view.RoomValues[0][columns.IndexOf("equipements.electricite")]);
        Assert.Equal("0", view.RoomValues[0][columns.IndexOf("equipements.plomberie")]);
        Assert.Equal("1 : Luminaire", view.RoomValues[0][columns.IndexOf("terminaux.electricite")]);
        Assert.DoesNotContain("equipements", columns);
    }

    [Fact]
    public void Room_parameters_become_one_column_each_like_area_and_volume()
    {
        using var env = new TestEnvironment();
        var json = JsonNode.Parse(env.ExampleResult)!.AsObject();
        var room = json["rooms"]!.AsArray()[0]!.AsObject();
        room["parametres"] = new JsonObject { ["Commentaires"] = "Salle serveurs", ["Hauteur limite"] = 2.44, ["Occupant"] = null };

        var view = ResultReader.ForRepository(env.Root).Read(json.ToJsonString());

        var columns = view.RoomColumns.ToList();
        Assert.Contains("surface_m2", columns);
        Assert.Equal("Salle serveurs", view.RoomValues[0][columns.IndexOf("parametres.Commentaires")]);
        Assert.Equal("2.44", view.RoomValues[0][columns.IndexOf("parametres.Hauteur limite")]);
        Assert.Null(view.RoomValues[0][columns.IndexOf("parametres.Occupant")]);
        Assert.Null(view.RoomValues[1][columns.IndexOf("parametres.Commentaires")]);
    }

    [Fact]
    public void Trc_file_is_displayed_as_one_row_per_bim_object_and_one_column_per_code()
    {
        using var env = new TestEnvironment();
        const string json = """
        [
          { "model": "trc.bimobject", "pk": "trc.bimobject_a", "fields": { "bim_id": "uid-1", "code_file": "F" } },
          { "model": "trc.bimobject", "pk": "trc.bimobject_b", "fields": { "bim_id": "uid-2", "code_file": "F" } },
          { "model": "trc.bimobjectpropertyvalue", "pk": "p1", "fields": { "latest_value": "0.90m", "original_value": "0.90m", "bim_object_id": "trc.bimobject_a", "bim_property_code_property": "b:porte_largeur" } },
          { "model": "trc.bimobjectpropertyvalue", "pk": "p2", "fields": { "latest_value": "1.40m", "original_value": "1.40m", "bim_object_id": "trc.bimobject_a", "bim_property_code_property": "b:porte_largeur" } },
          { "model": "trc.bimobjectpropertyvalue", "pk": "p3", "fields": { "latest_value": "30.03", "original_value": "30.03", "bim_object_id": "trc.bimobject_b", "bim_property_code_property": "b:dimensions_surface" } },
          { "model": "special.setcodefile", "fields": { "file_name": "F.rvt", "code_file": "F" } }
        ]
        """;

        var view = ResultReader.ForRepository(env.Root).ReadTrc(json);

        Assert.True(view.IsSchemaValid);
        Assert.Null(view.Status);
        Assert.Equal(["bim_id", "b:porte_largeur", "b:dimensions_surface"], view.RoomColumns);
        Assert.Equal(["uid-1", "0.90m | 1.40m", null], view.RoomValues[0]);
        Assert.Equal(["uid-2", null, "30.03"], view.RoomValues[1]);
        Assert.Contains(view.Summary, kv => kv.Key == "Objets BIM / valeurs" && kv.Value == "2 / 3");
    }

    [Fact]
    public void Trc_elements_are_shown_under_each_room_of_their_space_list_and_the_legacy_space_code()
    {
        using var env = new TestEnvironment();
        const string json = """
        [
          { "model": "trc.bimobject", "pk": "trc.bimobject_r1", "fields": { "bim_id": "room-1", "code_file": "F" } },
          { "model": "trc.bimobject", "pk": "trc.bimobject_r2", "fields": { "bim_id": "room-2", "code_file": "F" } },
          { "model": "trc.bimobject", "pk": "trc.bimobject_d", "fields": { "bim_id": "door-1", "code_file": "F" } },
          { "model": "trc.bimobject", "pk": "trc.bimobject_e", "fields": { "bim_id": "equip-1", "code_file": "F" } },
          { "model": "trc.bimobject", "pk": "trc.bimobject_o", "fields": { "bim_id": "orphan", "code_file": "F" } },
          { "model": "trc.bimobjectpropertyvalue", "pk": "p1", "fields": { "latest_value": "Space", "original_value": "Space", "bim_object_id": "trc.bimobject_r1", "bim_property_code_property": "Category" } },
          { "model": "trc.bimobjectpropertyvalue", "pk": "p2", "fields": { "latest_value": "Door", "original_value": "Door", "bim_object_id": "trc.bimobject_d", "bim_property_code_property": "Category" } },
          { "model": "trc.bimobjectpropertyvalue", "pk": "p3", "fields": { "latest_value": "[\"trc.bimobject_r1\",\"trc.bimobject_r2\"]", "original_value": "[\"trc.bimobject_r1\",\"trc.bimobject_r2\"]", "bim_object_id": "trc.bimobject_d", "bim_property_code_property": "ListeSpaces" } },
          { "model": "trc.bimobjectpropertyvalue", "pk": "p5", "fields": { "latest_value": "cvc equipement", "original_value": "cvc equipement", "bim_object_id": "trc.bimobject_e", "bim_property_code_property": "Category" } },
          { "model": "trc.bimobjectpropertyvalue", "pk": "p6", "fields": { "latest_value": "room-2", "original_value": "room-2", "bim_object_id": "trc.bimobject_e", "bim_property_code_property": "Space" } },
          { "model": "trc.bimobjectpropertyvalue", "pk": "p7", "fields": { "latest_value": "[\"trc.bimobject_x\"]", "original_value": "[\"trc.bimobject_x\"]", "bim_object_id": "trc.bimobject_o", "bim_property_code_property": "ListeSpaces" } }
        ]
        """;

        var view = ResultReader.ForRepository(env.Root).ReadTrc(json);

        Assert.Equal(["room-1", "room-2", "orphan"], view.RoomTree.Select(row => row[0]));
        Assert.Equal(["door-1"], view.RoomTree[0].Children.Select(row => row[0]));
        Assert.Equal(["door-1", "equip-1"], view.RoomTree[1].Children.Select(row => row[0]));
        Assert.All(view.RoomTree[1].Children, child => Assert.Equal(1, child.Depth));
        Assert.Equal("[\"trc.bimobject_r1\",\"trc.bimobject_r2\"]", view.RoomTree[0].Children[0][view.RoomColumns.ToList().IndexOf("ListeSpaces")]);
        Assert.False(view.RoomTree[2].HasChildren);
        Assert.Equal(3, view.RoomValues.Count);
    }

    [Fact]
    public void Result_json_elements_are_child_rows_of_their_room()
    {
        using var env = new TestEnvironment();
        var json = JsonNode.Parse(env.ExampleResult)!.AsObject();
        var room = json["rooms"]!.AsArray()[0]!.AsObject();
        room["portes"] = new JsonArray(new JsonObject { ["id_revit"] = "10", ["famille"] = "Simple", ["largeur_m"] = 0.9 });
        room["equipements"] = new JsonArray(new JsonObject { ["id_revit"] = "20", ["famille"] = "CTA", ["discipline"] = "cvc" });

        var view = ResultReader.ForRepository(env.Root).Read(json.ToJsonString());

        var columns = view.RoomColumns.ToList();
        Assert.Equal(ResultReader.ObjectColumn, columns[0]);
        Assert.Equal("pièce", view.RoomTree[0][0]);
        Assert.Equal(["porte", "équipement cvc"], view.RoomTree[0].Children.Select(row => row[0]));
        Assert.Equal("Simple", view.RoomTree[0].Children[0][columns.IndexOf("famille")]);
        Assert.Equal("0.9", view.RoomTree[0].Children[0][columns.IndexOf("largeur_m")]);
        Assert.False(view.RoomTree[1].HasChildren);
    }

    [Fact]
    public void Visible_rows_follow_the_expanded_rooms()
    {
        var child = new TableRow(["porte"], depth: 1);
        var room = new TableRow(["pièce"], [child]);
        var empty = new TableRow(["pièce vide"]);

        Assert.Equal([room, empty], TableRow.Visible([room, empty]));
        Assert.Equal("▶", room.Glyph);
        Assert.Equal(string.Empty, empty.Glyph);

        room.IsExpanded = true;

        Assert.Equal([room, child, empty], TableRow.Visible([room, empty]));
        Assert.Equal("▼", room.Glyph);
        Assert.Equal(TableRow.IndentPerLevel, child.Indent);
    }

    [Fact]
    public void Unreadable_trc_file_is_reported_not_thrown()
    {
        using var env = new TestEnvironment();

        var view = ResultReader.ForRepository(env.Root).ReadTrc("[ tronqué");

        Assert.False(view.IsSchemaValid);
        Assert.Empty(view.RoomColumns);
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
