using EgRoomTracker.Application;
using EgRoomTracker.Application.Revit;
using EgRoomTracker.Contracts.Codes;

namespace EgRoomTracker.Tests.Application;

/// <summary>Filtre de phases de la collecte (EGR-07), indépendant de Revit.</summary>
public class PhaseSelectionTests
{
    [Fact]
    public void Parse_Null_MeansAllPhases() => Assert.Null(PhaseSelection.Parse(null));

    [Fact]
    public void Parse_Empty_SelectsNoPhase() => Assert.Empty(PhaseSelection.Parse([])!);

    [Fact]
    public void Parse_ElementIds_AreConvertedAndDeduplicated()
    {
        var phases = PhaseSelection.Parse(["86961", "86961", "9007199254740993", "-1"])!;

        Assert.Equal(new long[] { -1, 86961, 9007199254740993 }, phases.Order());
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("12.5")]
    [InlineData("")]
    public void Parse_InvalidElementId_FailsWithConfigInvalid(string value)
    {
        var ex = Assert.Throws<ExtractionFailedException>(() => PhaseSelection.Parse(["86961", value]));

        Assert.Equal(ErrorCodes.ConfigInvalid, ex.Error.Code);
        Assert.Equal(Stages.Configuration, ex.Error.Stage);
    }
}
