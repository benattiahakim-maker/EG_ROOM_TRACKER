using EgRoomTracker.Application;
using EgRoomTracker.Application.Configuration;
using EgRoomTracker.Contracts.Codes;
using EgRoomTracker.Contracts.Configuration;

namespace EgRoomTracker.Tests.Configuration;

/// <summary>Options <c>roomParameters</c> et <c>elements</c> de <c>settings.json</c> (schéma 1.4 du résultat).</summary>
public sealed class SettingsElementsTests
{
    private static ExtractionSettings Parse(string body) =>
        SettingsReader.Parse($$"""{ "schemaVersion": "1.0"{{body}} }""");

    private static void AssertInvalid(string body, string expected)
    {
        var exception = Assert.Throws<ExtractionFailedException>(() => Parse(body));
        Assert.Equal(ErrorCodes.ConfigInvalid, exception.Error.Code);
        Assert.Contains(expected, exception.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Defaults_export_nothing_more()
    {
        var settings = Parse(string.Empty);

        Assert.Equal(RoomParametersMode.None, settings.RoomParameters);
        Assert.Empty(settings.Elements);
    }

    [Theory]
    [InlineData("none", RoomParametersMode.None)]
    [InlineData("all", RoomParametersMode.All)]
    public void RoomParameters_AreRead(string value, RoomParametersMode expected) =>
        Assert.Equal(expected, Parse($$""", "roomParameters": "{{value}}" """).RoomParameters);

    [Fact]
    public void RoomParameters_NullIsNone() =>
        Assert.Equal(RoomParametersMode.None, Parse(""", "roomParameters": null""").RoomParameters);

    [Theory]
    [InlineData(""", "roomParameters": "ALL" """, "roomParameters « ALL » inconnu")]
    [InlineData(""", "roomParameters": true""", "roomParameters doit être")]
    public void RoomParameters_Invalid(string body, string expected) => AssertInvalid(body, expected);

    [Fact]
    public void Elements_AreReadInOrder()
    {
        var settings = Parse(""", "elements": ["terminals", "doors", "equipment"]""");

        Assert.Equal([ElementKind.Terminals, ElementKind.Doors, ElementKind.Equipment], settings.Elements);
    }

    [Fact]
    public void Elements_EmptyOrNullMeansNone()
    {
        Assert.Empty(Parse(""", "elements": []""").Elements);
        Assert.Empty(Parse(""", "elements": null""").Elements);
    }

    [Fact]
    public void RoomImages_DefaultsToOffWithTheDefaultWidth()
    {
        var settings = Parse(string.Empty);

        Assert.False(settings.RoomImages);
        Assert.Equal(ExtractionSettings.DefaultImageWidthPx, settings.ImageWidthPx);
    }

    [Fact]
    public void RoomImages_AreRead()
    {
        var settings = Parse(""", "roomImages": true, "imageWidthPx": 1200""");

        Assert.True(settings.RoomImages);
        Assert.Equal(1200, settings.ImageWidthPx);
        Assert.False(Parse(""", "roomImages": false""").RoomImages);
        Assert.False(Parse(""", "roomImages": null""").RoomImages);
    }

    [Theory]
    [InlineData(""", "roomImages": "oui" """, "roomImages doit être true ou false")]
    [InlineData(""", "roomImages": 1""", "roomImages doit être true ou false")]
    [InlineData(""", "imageWidthPx": 399""", "imageWidthPx doit être un entier de 400 à 8000")]
    [InlineData(""", "imageWidthPx": 8001""", "imageWidthPx doit être un entier de 400 à 8000")]
    [InlineData(""", "imageWidthPx": 1200.5""", "imageWidthPx doit être un entier")]
    [InlineData(""", "imageWidthPx": "2400" """, "imageWidthPx doit être un entier")]
    public void RoomImages_Invalid(string body, string expected) => AssertInvalid(body, expected);

    [Fact]
    public void RoomHeights_DefaultToOffWithTheTrcStep()
    {
        var settings = Parse(string.Empty);

        Assert.False(settings.RoomHeights);
        Assert.Equal(0.5, settings.HeightStepM);
    }

    [Fact]
    public void RoomHeights_AreRead()
    {
        var settings = Parse(""", "roomHeights": true, "heightStepM": 0.25""");

        Assert.True(settings.RoomHeights);
        Assert.Equal(0.25, settings.HeightStepM);
        Assert.False(Parse(""", "roomHeights": false""").RoomHeights);
        Assert.False(Parse(""", "roomHeights": null""").RoomHeights);
        Assert.Equal(2, Parse(""", "heightStepM": 2""").HeightStepM);
    }

    [Theory]
    [InlineData(""", "roomHeights": "oui" """, "roomHeights doit être true ou false")]
    [InlineData(""", "roomHeights": 1""", "roomHeights doit être true ou false")]
    [InlineData(""", "heightStepM": 0.05""", "heightStepM doit être un nombre de 0.1 à 2")]
    [InlineData(""", "heightStepM": 2.5""", "heightStepM doit être un nombre de 0.1 à 2")]
    [InlineData(""", "heightStepM": "0.5" """, "heightStepM doit être un nombre")]
    public void RoomHeights_Invalid(string body, string expected) => AssertInvalid(body, expected);

    [Fact]
    public void LinkedElements_DefaultToTrue()
    {
        Assert.True(Parse(string.Empty).LinkedElements);
        Assert.True(Parse(""", "linkedElements": null""").LinkedElements);
        Assert.True(Parse(""", "linkedElements": true""").LinkedElements);
        Assert.False(Parse(""", "linkedElements": false""").LinkedElements);
    }

    [Theory]
    [InlineData(""", "linkedElements": "non" """, "linkedElements doit être true ou false")]
    [InlineData(""", "linkedElements": 0""", "linkedElements doit être true ou false")]
    public void LinkedElements_Invalid(string body, string expected) => AssertInvalid(body, expected);

    [Theory]
    [InlineData(""", "outputFormat": "contract" """, OutputFormat.Contract)]
    [InlineData(""", "outputFormat": "trc" """, OutputFormat.Trc)]
    [InlineData(""", "outputFormat": "both" """, OutputFormat.Both)]
    [InlineData(""", "outputFormat": null""", OutputFormat.Contract)]
    [InlineData("", OutputFormat.Contract)]
    public void OutputFormat_IsRead(string body, OutputFormat expected) => Assert.Equal(expected, Parse(body).OutputFormat);

    [Theory]
    [InlineData(""", "outputFormat": "TRC" """, "outputFormat « TRC » inconnu")]
    [InlineData(""", "outputFormat": true""", "outputFormat doit être")]
    public void OutputFormat_Invalid(string body, string expected) => AssertInvalid(body, expected);

    [Fact]
    public void ElementCategories_AreReadPerFamily()
    {
        var settings = Parse(""", "elementCategories": { "terminals": ["OST_PlumbingFixtures", "OST_Sprinklers"], "equipment": ["OST_ElectricalEquipment"] }""");

        Assert.Equal(["OST_PlumbingFixtures", "OST_Sprinklers"], settings.ElementCategories[ElementKind.Terminals]);
        Assert.Equal(["OST_ElectricalEquipment"], settings.ElementCategories[ElementKind.Equipment]);
        Assert.False(settings.ElementCategories.ContainsKey(ElementKind.Doors));
    }

    [Fact]
    public void ElementCategories_DefaultAndNullAreEmpty()
    {
        Assert.Empty(Parse(string.Empty).ElementCategories);
        Assert.Empty(Parse(""", "elementCategories": null""").ElementCategories);
    }

    [Theory]
    [InlineData(""", "elementCategories": ["OST_Doors"]""", "elementCategories doit être un objet")]
    [InlineData(""", "elementCategories": { "windows": ["OST_Windows"] }""", "famille « windows » inconnue")]
    [InlineData(""", "elementCategories": { "doors": [] }""", "elementCategories.doors doit être un tableau non vide")]
    [InlineData(""", "elementCategories": { "doors": "OST_Doors" }""", "elementCategories.doors doit être un tableau non vide")]
    [InlineData(""", "elementCategories": { "doors": ["Doors"] }""", "elementCategories.doors[0] : nom de catégorie attendu")]
    [InlineData(""", "elementCategories": { "doors": ["OST_Doors", 3] }""", "elementCategories.doors[1] : nom de catégorie attendu")]
    [InlineData(""", "elementCategories": { "doors": ["OST_Doors", "OST_Doors"] }""", "elementCategories.doors[1] : « OST_Doors » en double")]
    public void ElementCategories_Invalid(string body, string expected) => AssertInvalid(body, expected);

    [Theory]
    [InlineData(""", "elements": "doors" """, "elements doit être un tableau")]
    [InlineData(""", "elements": ["windows"]""", "elements[0] : nom inconnu")]
    [InlineData(""", "elements": ["doors", 3]""", "elements[1] : nom inconnu")]
    [InlineData(""", "elements": ["doors", "doors"]""", "elements[1] : « doors » en double")]
    public void Elements_Invalid(string body, string expected) => AssertInvalid(body, expected);
}
