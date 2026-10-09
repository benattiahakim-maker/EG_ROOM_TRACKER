using Autodesk.Revit.DB;
using RevitApplication = Autodesk.Revit.ApplicationServices.Application;

namespace EgRoomTracker.Revit;

/// <summary>
/// Contexte Revit de l'exécution, fourni par Design Automation. Utilisé uniquement sur le thread
/// de l'événement Design Automation : aucun Task.Run ni accès parallèle aux objets Revit.
/// </summary>
public sealed class RevitExecutionContext
{
    public RevitExecutionContext(RevitApplication application, Document? document)
    {
        Application = application;
        Document = document;
    }

    public RevitApplication Application { get; }

    /// <summary>
    /// Document principal : fourni par <c>DesignAutomationData.RevitDoc</c> ou ouvert explicitement
    /// par <see cref="RevitDocumentService"/> selon la décision D1 (EGR-06).
    /// </summary>
    public Document? Document { get; internal set; }

    public Document RequireDocument() =>
        Document ?? throw new InvalidOperationException("Document Revit non ouvert.");
}
