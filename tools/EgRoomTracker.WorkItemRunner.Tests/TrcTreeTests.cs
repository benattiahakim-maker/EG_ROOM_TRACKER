using EgRoomTracker.Application.Trc;
using EgRoomTracker.WorkItemRunner.Services;

namespace EgRoomTracker.WorkItemRunner.Tests;

/// <summary>Arbre TRC de WorkItemRunner : pièces, un sous-tableau par catégorie avec ses colonnes, paramètres, filtres.</summary>
public sealed class TrcTreeTests
{
    private static TrcObject Room(string pk, string name, string surface = "12.87") => new(pk, "uid-" + pk, new Dictionary<string, List<string>>
    {
        [TrcCodes.Category] = [TrcCategories.Space],
        [TrcCodes.Username] = [name],
        [TrcCodes.Storey] = ["Niveau 1"],
        [TrcCodes.Surface] = [surface],
        [TrcCodes.Building] = ["A"],
        ["c:parametres revit_finition du sol"] = ["beton"],
    }, []);

    private static TrcObject Element(string pk, string category, string family, string[] parents, params (string Code, string Value)[] extra)
    {
        var values = new Dictionary<string, List<string>>
        {
            [TrcCodes.Category] = [category],
            [TrcCodes.SpaceList] = [$"[{string.Join(",", parents.Select(p => $"\"{p}\""))}]"],
            [TrcCodes.ElementFamily] = [family],
            [TrcCodes.Username] = ["Niveau1_" + family],
            ["c:parametres revit_ifcguid"] = ["guid-" + pk],
        };
        foreach (var (code, value) in extra)
        {
            values[code] = [value];
        }

        return new TrcObject(pk, "uid-" + pk, values, parents);
    }

    private static TrcTree Sample() => TrcTree.Build(
    [
        Room("r1", "Niveau1_Pièce_1"),
        Room("r2", "Niveau1_Pièce_2", "17.94"),
        Element("d1", TrcCategories.Door, "Simple", ["r1", "r2"], (TrcCodes.Numero, "1"), (TrcCodes.DoorWidth, "0.92m"), (TrcCodes.DoorHeight, "2.13")),
        Element("e1", TrcCategories.Equipment("cvc"), "CTA", ["r1"], (TrcCodes.ElementLink, "CVC_Lien")),
        Element("t1", TrcCategories.Terminal("electricite"), "Luminaire", ["r1"], (TrcCodes.ElementMark, "L1")),
    ]);

    [Fact]
    public void Each_category_is_a_sub_table_with_its_own_columns()
    {
        var room = Sample().Rooms[0];

        Assert.Equal(["Portes", "Équipements CVC", "Terminaux électricité"], room.Groups.Select(g => g.Title));
        Assert.Equal(["Numéro", "Famille", "Largeur", "Hauteur", "Pièces"], room.Groups[0].Headers.Select(h => h.Text));
        Assert.Equal(["Famille", "Modèle lié", "Pièces"], room.Groups[1].Headers.Select(h => h.Text));
        Assert.Equal(["Repère", "Famille", "Pièces"], room.Groups[2].Headers.Select(h => h.Text));
        Assert.Equal(["1", "Simple", "0.92 m", "2.13 m", "Niveau1_Pièce_1 · Niveau1_Pièce_2"], room.Groups[0].Rows[0].Cells.Select(c => c.Text));
        Assert.Equal("1 porte · 1 équipement · 1 terminal", room.Content);
        Assert.All(room.Groups[0].Headers.Zip(room.Groups[0].Rows[0].Cells), pair => Assert.Equal(pair.First.Width, pair.Second.Width));
    }

    [Fact]
    public void A_door_between_two_rooms_is_shown_under_both()
    {
        var tree = Sample();

        Assert.Equal(["Niveau1_Pièce_1", "Niveau1_Pièce_2"], tree.Rooms.Select(r => r.Name));
        Assert.Equal("uid-d1", Assert.Single(Assert.Single(tree.Rooms[1].Groups).Rows).BimId);
        Assert.Equal("17.94", tree.Rooms[1].Surface);
    }

    [Fact]
    public void Each_object_has_its_own_parameters()
    {
        var tree = Sample();
        var room = tree.Rooms[0];
        var door = room.Groups[0].Rows[0];

        Assert.Equal(["bim_id", "building", "finition du sol"], room.Parameters.Parameters.Select(p => p.Name));
        Assert.Equal(["bim_id", "Nom", "ifcguid"], door.Parameters.Parameters.Select(p => p.Name));
        Assert.Equal("guid-d1", door.Parameters.Parameters[^1].Value);
        Assert.Equal("Paramètres (3)", room.Parameters.Title);
    }

    [Fact]
    public void Filters_hide_categories_and_parameters()
    {
        var tree = Sample();
        var room = tree.Rooms[0];

        Assert.Equal(["Portes", "CVC", "Électricité", "Paramètres Revit"], tree.Filters.Select(f => f.Label));
        tree.Filters.Single(f => f.Key == TrcTree.DoorKey).IsChecked = false;
        tree.Filters.Single(f => f.Key == TrcTree.ParametersKey).IsChecked = false;

        Assert.False(room.Groups[0].IsVisible);
        Assert.True(room.Groups[1].IsVisible);
        Assert.False(room.Parameters.IsVisible);
        Assert.False(room.Groups[1].Rows[0].Parameters.IsVisible);
    }

    [Fact]
    public void Expand_and_collapse_all()
    {
        var tree = Sample();
        Assert.All(tree.Rooms, r => Assert.Equal("▶", r.Glyph));

        tree.ExpandAll();
        Assert.All(tree.Rooms, r => Assert.True(r.IsExpanded));
        Assert.Equal("▼", tree.Rooms[0].Glyph);

        tree.CollapseAll();
        Assert.All(tree.Rooms, r => Assert.False(r.IsExpanded));
    }

    [Fact]
    public void Objects_without_a_known_room_are_grouped_under_no_room()
    {
        var tree = TrcTree.Build([Room("r1", "P1"), Element("e9", TrcCategories.Equipment("cvc"), "CTA", ["absente"])]);

        Assert.Equal(["P1", TrcTree.NoRoom], tree.Rooms.Select(r => r.Name));
        Assert.Equal("uid-e9", tree.Rooms[1].Groups[0].Rows[0].BimId);
    }
}
