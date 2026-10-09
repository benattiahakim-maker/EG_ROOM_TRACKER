using EgRoomTracker.Application.Revit;
using EgRoomTracker.Contracts.Results;

namespace EgRoomTracker.Application.Quality;

/// <summary>
/// Indicateurs qualité, anomalies et statut final calculés à partir des DTO, sans appel Revit (sections 9.1 et 9.3).
/// </summary>
public interface IQualityCheckService
{
    QualityReport Evaluate(
        IReadOnlyList<RoomDto> rooms,
        IReadOnlyList<AnomalyDto> anomalies,
        VolumeComputationState volumeState);
}

/// <summary>Pièces complétées de leurs <c>qualityFlags</c>, anomalies détaillées et statut (hors échec global).</summary>
public sealed record QualityReport(
    IReadOnlyList<RoomDto> Rooms,
    IReadOnlyList<AnomalyDto> Anomalies,
    ExtractionStatus Status);

/// <summary>Squelette ; implémentation en EGR-11 (#11).</summary>
public sealed class QualityCheckService : IQualityCheckService
{
    public QualityReport Evaluate(
        IReadOnlyList<RoomDto> rooms,
        IReadOnlyList<AnomalyDto> anomalies,
        VolumeComputationState volumeState) =>
        throw new NotImplementedException("EGR-11 : indicateurs qualité et statut.");
}
