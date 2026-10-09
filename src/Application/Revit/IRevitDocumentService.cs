using EgRoomTracker.Contracts.Configuration;
using EgRoomTracker.Contracts.Results;

namespace EgRoomTracker.Application.Revit;

/// <summary>
/// Accès au document selon le contrat de l'Activity, contrôle de version et politique de liens (EGR-06).
/// Implémenté dans le projet Revit ; signature sans type Revit.
/// Stratégie (D1) : ouverture explicite du RVT principal par l'application, qui en est propriétaire et le ferme
/// sans sauvegarde (<see cref="Close"/>). Avertissements Revit à l'ouverture (D2) : tolérés et signalés
/// (REVIT_WARNING_ON_OPEN) ; erreurs bloquantes : DOCUMENT_CORRUPTED.
/// Lève <see cref="ExtractionFailedException"/> (RVT_NOT_FOUND, PATH_OUTSIDE_INPUT, REVIT_VERSION_INCOMPATIBLE,
/// DOCUMENT_OPEN_FAILED, DOCUMENT_CORRUPTED).
/// </summary>
public interface IRevitDocumentService
{
    /// <summary>
    /// Ouvre le RVT principal. <see cref="ExtractionSettings.MainModelPath"/> doit être résolu par
    /// <c>IInputConfigurationService</c> (EGR-05), y compris pour un ZIP à RVT unique.
    /// </summary>
    DocumentDescriptor Open(ExtractionSettings settings, string inputDirectory);

    /// <summary>Ferme sans sauvegarde le document ouvert par ce service ; sans effet sinon. Idempotent.</summary>
    void Close();
}

/// <summary>Description du document accessible et anomalies de document (LINK_MISSING, REVIT_WARNING_ON_OPEN, ...).</summary>
public sealed record DocumentDescriptor(SourceInfo Source, IReadOnlyList<AnomalyDto> Anomalies);
