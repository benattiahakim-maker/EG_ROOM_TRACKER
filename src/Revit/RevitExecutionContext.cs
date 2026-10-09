using Autodesk.Revit.DB;
using RevitApplication = Autodesk.Revit.ApplicationServices.Application;

namespace EgRoomTracker.Revit;

/// <summary>
/// Contexte Revit de l'exécution, fourni par Design Automation. Utilisé uniquement sur le thread
/// de l'événement Design Automation : aucun Task.Run ni accès parallèle aux objets Revit.
/// </summary>
public sealed class RevitExecutionContext
{
    public RevitExecutionContext(RevitApplication application, Document? providedDocument)
    {
        Application = application;
        ProvidedDocument = providedDocument;
    }

    public RevitApplication Application { get; }

    /// <summary>
    /// Document éventuellement ouvert par RevitCoreConsole (<c>DesignAutomationData.RevitDoc</c>, option <c>/i</c>).
    /// Selon D1, l'Activity ne fournit pas de document ; s'il est fourni et correspond au RVT principal, il est réutilisé
    /// sans être fermé par l'application.
    /// </summary>
    public Document? ProvidedDocument { get; }

    /// <summary>Document principal accessible après <see cref="RevitDocumentService.Open"/>.</summary>
    public Document? Document { get; private set; }

    /// <summary>Vrai si le document a été ouvert par l'application, qui doit le fermer.</summary>
    public bool OwnsDocument { get; private set; }

    public Document RequireDocument() =>
        Document ?? throw new InvalidOperationException("Document Revit non ouvert.");

    internal void Attach(Document document, bool owned)
    {
        Document = document;
        OwnsDocument = owned;
    }

    internal void Detach()
    {
        Document = null;
        OwnsDocument = false;
    }
}
