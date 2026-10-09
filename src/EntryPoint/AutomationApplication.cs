using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using DesignAutomationFramework;
using EgRoomTracker.Contracts.Results;

namespace EgRoomTracker.EntryPoint;

/// <summary>
/// Point d'entrée Design Automation : cycle de vie, abonnement à <c>DesignAutomationReadyEvent</c>,
/// adaptation du contexte APS et résultat d'exécution (<c>Succeeded</c>). Aucun traitement métier ici.
/// </summary>
public sealed class AutomationApplication : IExternalDBApplication
{
    public ExternalDBApplicationResult OnStartup(ControlledApplication application)
    {
        DesignAutomationBridge.DesignAutomationReadyEvent += OnDesignAutomationReady;
        return ExternalDBApplicationResult.Succeeded;
    }

    public ExternalDBApplicationResult OnShutdown(ControlledApplication application)
    {
        DesignAutomationBridge.DesignAutomationReadyEvent -= OnDesignAutomationReady;
        return ExternalDBApplicationResult.Succeeded;
    }

    private static void OnDesignAutomationReady(object? sender, DesignAutomationReadyEventArgs e)
    {
        e.Succeeded = false;
        var request = ActivityContract.CreateRequest(e.DesignAutomationData);
        var service = CompositionRoot.CreateExtractionService(e.DesignAutomationData, request.CorrelationId);
        var result = service.Run(request);
        e.Succeeded = result.Summary.Status != ExtractionStatus.Failed;
    }
}
