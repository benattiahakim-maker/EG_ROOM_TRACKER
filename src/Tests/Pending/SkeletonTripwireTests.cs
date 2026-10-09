using EgRoomTracker.Application.Quality;
using EgRoomTracker.Application.Revit;
using EgRoomTracker.Contracts.Configuration;

namespace EgRoomTracker.Tests.Pending;

/// <summary>
/// Garde-fou : ces tests échouent dès qu'un squelette est implémenté, pour rappeler d'activer
/// (retirer <c>Skip</c>) les tests de spécification correspondants dans <c>Pending/</c>, puis de supprimer le cas ici.
/// </summary>
public class SkeletonTripwireTests
{
    private const string Reminder = "Service implémenté : activer les tests de Pending/ correspondants et retirer ce garde-fou.";

    [Fact]
    public void QualityCheckService_IsStillASkeleton_Egr11() =>
        AssertSkeleton(() => new QualityCheckService().Evaluate([], [], new VolumeComputationState(VolumeMode.Preserve, false)));

    private static void AssertSkeleton(Action action)
    {
        var ex = Record.Exception(action);
        Assert.True(ex is NotImplementedException, Reminder);
    }
}
