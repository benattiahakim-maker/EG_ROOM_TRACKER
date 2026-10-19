using EgRoomTracker.Application.Trc;
using EgRoomTracker.WorkItemRunner.Services;

namespace EgRoomTracker.WorkItemRunner.Tests;

/// <summary>
/// Arbre TRC de WorkItemRunner : tableau des pièces, un sous-tableau par catégorie ; chaque tableau a ses colonnes sous trois bandes
/// (Identification, Paramètres d'occurrence, Paramètres de type) ; filtres de catégories et de sections.
/// </summary>
public sealed class TrcTreeTests
{
    private static TrcObject Room(string pk, string name, string surface = "12.87") => new(pk, "uid-" + pk, new Dictionary<string, List<string>>
    {
        [TrcCodes.Category] = [TrcCategories.Space],
        [TrcCodes.Username] = [name],
        [TrcCodes.Storey] = ["Niveau 1"],
        [TrcCodes.Surface] = [surface],
        [TrcCodes.Building] = ["A"],
        [TrcCodes.RevitParameter("Finition du sol")] = ["beton"],
        [TrcCodes.RevitParameter("Commentaires")] = [string.Empty],
    }, []);

    private static TrcObject Element(string pk, string category, string family, string[] parents, params (string Code, string Value)[] extra)
    {
        var values = new Dictionary<string, List<string>>
        {
            [TrcCodes.Category] = [category],
            [TrcCodes.SpaceList] = [$"[{string.Join(",", parents.Select(p => $"\"{p}\""))}]"],
            [TrcCodes.ElementFamily] = [family],
            [TrcCodes.RevitParameter("IfcGUID")] = ["guid-" + pk],
            [TrcCodes.TypeParameter("Fabricant")] = ["F-" + pk],
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
        Element("d1", TrcCategories.Door, "Simple", ["r1", "r2"], (TrcCodes.Numero, "1"), (TrcCodes.DoorWidth, "0.92m"), (TrcCodes.DoorHeight, "2.13"),
            (TrcCodes.TypeParameter("Matériau de la porte"), "Bois")),
        Element("e1", TrcCategories.Equipment("cvc"), "CTA", ["r1"], (TrcCodes.ElementLink, "CVC_Lien")),
        Element("t1", TrcCategories.Terminal("electricite"), "Luminaire", ["r1"], (TrcCodes.ElementMark, "L1")),
    ]);

    private static List<string> Texts(IEnumerable<TrcCell> cells) => cells.Select(c => c.Text ?? "(null)").ToList();

    [Fact]
    public void Each_category_is_a_sub_table_with_identification_then_occurrence_then_type_columns()
    {
        var room = Sample().Rooms[0];
        var doors = room.Groups[0];

        Assert.Equal(["Portes", "Équipements CVC", "Terminaux électricité"], room.Groups.Select(g => g.Title));
        Assert.Equal(["Identification", "Paramètres d'occurrence (1)", "Paramètres de type (2)"], doors.Header.Bands.Select(b => b.Title));
        Assert.Equal(["Numéro", "Famille", "Largeur", "Hauteur", "Pièces", "Ifcguid", "Fabricant", "Matériau de la porte"], Texts(doors.Header.Columns));
        Assert.Equal(["1", "Simple", "0.92 m", "2.13 m", "Niveau1_Pièce_1 · Niveau1_Pièce_2", "guid-d1", "F-d1", "Bois"], Texts(doors.Rows[0].Cells));
        Assert.Equal(["Famille", "Modèle lié", "Pièces", "Ifcguid", "Fabricant"], Texts(room.Groups[1].Header.Columns));
        Assert.Equal(
            [TrcSection.Identification, TrcSection.Identification, TrcSection.Identification, TrcSection.Identification, TrcSection.Identification, TrcSection.Occurrence, TrcSection.Type, TrcSection.Type],
            doors.Header.Columns.Select(c => c.Section));
        Assert.Equal(doors.Header.Columns.Where(c => c.Section == TrcSection.Type).Sum(c => c.Width), doors.Header.Bands[2].Width);
        Assert.All(doors.Header.Columns.Zip(doors.Rows[0].Cells), pair => Assert.Equal(pair.First.Width, pair.Second.Width));
    }

    [Fact]
    public void The_room_table_lists_identification_then_every_occurrence_parameter()
    {
        var tree = Sample();

        Assert.Equal(["Identification", "Paramètres d'occurrence (2)"], tree.RoomHeader.Bands.Select(b => b.Title));
        Assert.Equal(["Pièce", "Niveau", "Surface (m²)", "Building", "Contenu", "Finition du sol", "Commentaires"], Texts(tree.RoomHeader.Columns));
        Assert.Equal(["Niveau1_Pièce_1", "Niveau 1", "12.87", "A", "1 porte · 1 équipement · 1 terminal", "beton", string.Empty], Texts(tree.Rooms[0].Cells));
    }

    [Fact]
    public void A_door_between_two_rooms_is_shown_under_both()
    {
        var tree = Sample();

        Assert.Equal(["Niveau1_Pièce_1", "Niveau1_Pièce_2"], tree.Rooms.Select(r => r.Name));
        Assert.Equal("uid-d1", Assert.Single(Assert.Single(tree.Rooms[1].Groups).Rows).BimId);
    }

    [Fact]
    public void Filters_hide_categories_and_column_sections()
    {
        var tree = Sample();
        var room = tree.Rooms[0];

        Assert.Equal(["Portes", "CVC", "Électricité", "Occurrence", "Type"], tree.Filters.Select(f => f.Label));
        tree.Filters.Single(f => f.Key == TrcTree.DoorKey).IsChecked = false;
        tree.Filters.Single(f => f.Key == TrcTree.TypeKey).IsChecked = false;

        Assert.False(room.Groups[0].IsVisible);
        Assert.True(room.Groups[1].IsVisible);
        Assert.All(room.Groups[1].Header.Items.Concat(room.Groups[1].Rows[0].Cells), item => Assert.Equal(item.Section != TrcSection.Type, item.IsVisible));
        Assert.All(tree.RoomHeader.Items.Concat(room.Cells), item => Assert.True(item.IsVisible));

        tree.Filters.Single(f => f.Key == TrcTree.OccurrenceKey).IsChecked = false;
        Assert.All(room.Cells, cell => Assert.Equal(cell.Section == TrcSection.Identification, cell.IsVisible));
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
        Assert.Equal(tree.RoomHeader.Columns.Count, tree.Rooms[1].Cells.Count);
    }
}
