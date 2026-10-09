using EgRoomTracker.Application;
using EgRoomTracker.Application.Revit;
using EgRoomTracker.Contracts.Codes;

namespace EgRoomTracker.Tests.Application;

/// <summary>Filtre de phases de la collecte (EGR-07), indépendant de Revit.</summary>
public class PhaseSelectionTests
{
    [Fact]
    public void From_Null_MeansAllPhases() => Assert.Null(PhaseSelection.From(null));

    [Fact]
    public void From_Empty_DefensiveConfigInvalid()
    {
        var ex = Assert.Throws<ExtractionFailedException>(() => PhaseSelection.From([]));

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
    public void From_ValidatedIds_AreKeptAsSet()
    {
        var phases = PhaseSelection.From([86961L, 86960L, 9007199254740993L])!;

        Assert.Equal(new long[] { 86960, 86961, 9007199254740993 }, phases.Order());
    }
}
