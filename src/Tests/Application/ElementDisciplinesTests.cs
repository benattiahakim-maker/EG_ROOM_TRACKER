using EgRoomTracker.Application.Revit;

namespace EgRoomTracker.Tests.Application;

/// <summary>Discipline d'un équipement ou terminal d'après sa catégorie Revit.</summary>
public sealed class ElementDisciplinesTests
{
    [Theory]
    [InlineData("OST_MechanicalEquipment", "cvc")]
    [InlineData("OST_DuctTerminal", "cvc")]
    [InlineData("OST_MechanicalControlDevices", "cvc")]
    [InlineData("OST_PlumbingEquipment", "plomberie")]
    [InlineData("OST_PlumbingFixtures", "plomberie")]
    [InlineData("OST_Sprinklers", "plomberie")]
    [InlineData("OST_ElectricalEquipment", "electricite")]
    [InlineData("OST_ElectricalFixtures", "electricite")]
    [InlineData("OST_LightingFixtures", "electricite")]
    [InlineData("OST_LightingDevices", "electricite")]
    [InlineData("OST_CommunicationDevices", "electricite")]
    [InlineData("OST_DataDevices", "electricite")]
    [InlineData("OST_TelephoneDevices", "electricite")]
    [InlineData("OST_SecurityDevices", "electricite")]
    [InlineData("OST_FireAlarmDevices", "electricite")]
    [InlineData("OST_NurseCallDevices", "electricite")]
    [InlineData("OST_SpecialityEquipment", "autres")]
    [InlineData("OST_FireProtection", "autres")]
    public void DefaultCategories_AreRangedByDiscipline(string category, string expected) =>
        Assert.Equal(expected, ElementDisciplines.Of(category));

    [Theory]
    [InlineData("OST_PipeAccessory")]
    [InlineData("OST_Furniture")]
    [InlineData("OST_Inconnue")]
    [InlineData("")]
    public void AnyOtherCategory_IsOther(string category) =>
        Assert.Equal(ElementDisciplines.Other, ElementDisciplines.Of(category));

    [Fact]
    public void All_ListsTheFourDisciplines() =>
        Assert.Equal(["cvc", "plomberie", "electricite", "autres"], ElementDisciplines.All);
}
