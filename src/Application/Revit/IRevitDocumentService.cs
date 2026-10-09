using EgRoomTracker.Contracts.Configuration;
using EgRoomTracker.Contracts.Results;

namespace EgRoomTracker.Application.Revit;

/// <summary>
/// Accès au document selon le contrat de l'Activity, contrôle de version et politique de liens.
/// Implémenté dans le projet Revit ; signature sans type Revit. Stratégie d'ouverture et
/// propriétaire de fermeture : décision D1 (EGR-06).
/// Lève <see cref="ExtractionFailedException"/> (RVT_NOT_FOUND, RVT_AMBIGUOUS, REVIT_VERSION_INCOMPATIBLE,
/// DOCUMENT_OPEN_FAILED, DOCUMENT_CORRUPTED).
/// </summary>
public interface IRevitDocumentService
{
    DocumentDescriptor Open(ExtractionSettings settings, string inputDirectory);
}

/// <summary>Description du document accessible et anomalies de document (LINK_MISSING, REVIT_WARNING_ON_OPEN, ...).</summary>
public sealed record DocumentDescriptor(SourceInfo Source, IReadOnlyList<AnomalyDto> Anomalies);
