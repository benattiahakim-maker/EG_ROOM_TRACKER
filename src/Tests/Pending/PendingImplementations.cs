namespace EgRoomTracker.Tests.Pending;

/// <summary>
/// Raisons de mise en attente des tests de spécification des services encore à l'état de squelette.
/// Chaque carte qui implémente le service retire le <c>Skip</c> correspondant (ou met la constante à <c>null!</c>)
/// et adapte au besoin.
/// </summary>
internal static class PendingImplementations
{
    public const string Egr05 = "En attente d'EGR-05 (#5) : InputConfigurationService est un squelette (NotImplementedException).";
    public const string Egr11 = "En attente d'EGR-11 (#11) : QualityCheckService est un squelette (NotImplementedException).";
    public const string Egr12 = "En attente d'EGR-12 (#12) : ResultExportService est un squelette (NotImplementedException).";
}
