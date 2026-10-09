using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;

namespace EgRoomTracker.Revit;

/// <summary>
/// Politique des avertissements et erreurs Revit pendant l'ouverture (décision D2) :
/// avertissements (<see cref="FailureSeverity.Warning"/>) tolérés, retirés et consignés pour l'anomalie
/// REVIT_WARNING_ON_OPEN ; erreurs et corruptions bloquantes : annulation, puis échec DOCUMENT_CORRUPTED.
/// Aucune résolution silencieuse d'une erreur, aucun dialogue.
/// Seuls les échecs du document principal sont traités : ceux d'un document lié (chargement d'un lien) sont
/// laissés au traitement par défaut de Revit et comptés, sans faire échouer le document principal.
/// </summary>
internal sealed class OpenFailurePolicy
{
    public List<string> Warnings { get; } = [];

    public List<string> BlockingFailures { get; } = [];

    /// <summary>Nombre de messages d'échec émis par des documents liés, non traités par cette politique.</summary>
    public int LinkedDocumentFailures { get; private set; }

    public void OnFailuresProcessing(object? sender, FailuresProcessingEventArgs e)
    {
        var accessor = e.GetFailuresAccessor();
        if (accessor.GetDocument() is { IsLinked: true })
        {
            LinkedDocumentFailures += accessor.GetFailureMessages().Count;
            e.SetProcessingResult(FailureProcessingResult.Continue);
            return;
        }

        var blocking = false;
        foreach (var failure in accessor.GetFailureMessages())
        {
            var description = failure.GetDescriptionText();
            if (failure.GetSeverity() == FailureSeverity.Warning)
            {
                Warnings.Add(description);
                accessor.DeleteWarning(failure);
            }
            else
            {
                BlockingFailures.Add($"{failure.GetSeverity()} : {description}");
                blocking = true;
            }
        }

        e.SetProcessingResult(blocking ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue);
    }
}
