using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using DesignAutomationFramework;
using EgRoomTracker.Contracts.Codes;
using EgRoomTracker.Contracts.Results;
using EgRoomTracker.Logging;

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
        using var log = new JsonConsoleLogService(request.CorrelationId);
        try
        {
            var service = CompositionRoot.CreateExtractionService(e.DesignAutomationData, log);
            var result = service.Run(request);
            e.Succeeded = result.Summary.Status != ExtractionStatus.Failed;
        }
        catch (Exception ex)
        {
            // Filet de sécurité : RoomExtractionService ne lève pas ; ceci couvre la composition.
            log.Log(new LogEvent(LogLevel.Error, LogEventCodes.ExtractionFailed, Stages.Startup, $"{ErrorCodes.UnexpectedError} : échec d'initialisation.")
            {
                Exception = ex,
                Data = new Dictionary<string, object?> { ["errorCode"] = ErrorCodes.UnexpectedError },
            });
        }
        finally
        {
            log.Flush();
        }
    }
}
