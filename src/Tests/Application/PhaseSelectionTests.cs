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
    public void Parse_Empty_FailsWithConfigInvalid()
    {
        var ex = Assert.Throws<ExtractionFailedException>(() => PhaseSelection.Parse([]));

        Assert.Equal(ErrorCodes.ConfigInvalid, ex.Error.Code);
        Assert.Equal(Stages.Configuration, ex.Error.Stage);
    }

    [Fact]
    public void EnsureKnown_AllPhasesKnown_Passes() =>
        PhaseSelection.EnsureKnown(new HashSet<long> { 86961 }, new HashSet<long> { 86961, 86962 });

    [Fact]
    public void EnsureKnown_NullMeansAllPhases_Passes() =>
        PhaseSelection.EnsureKnown(null, new HashSet<long>());

    [Fact]
    public void EnsureKnown_UnknownPhases_FailsWithConfigInvalidListingIds()
    {
        var ex = Assert.Throws<ExtractionFailedException>(() =>
            PhaseSelection.EnsureKnown(new HashSet<long> { 99, 86961, 12 }, new HashSet<long> { 86961 }));

        Assert.Equal(ErrorCodes.ConfigInvalid, ex.Error.Code);
        Assert.Equal(Stages.Collection, ex.Error.Stage);
        Assert.Contains("12, 99", ex.Error.Message);
        Assert.DoesNotContain("86961", ex.Error.Message);
    }

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
