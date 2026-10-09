using EgRoomTracker.Application.Revit;
using EgRoomTracker.Contracts.Configuration;

namespace EgRoomTracker.Revit;

/// <summary>Accès au document, contrôle de version et politique de liens. Squelette ; implémentation en EGR-06 (#6), décisions D1/D2.</summary>
public sealed class RevitDocumentService(RevitExecutionContext context) : IRevitDocumentService
{
    public DocumentDescriptor Open(ExtractionSettings settings, string inputDirectory)
    {
        _ = context;
        throw new NotImplementedException("EGR-06 : accès au document en environnement Automation.");
    }
}
