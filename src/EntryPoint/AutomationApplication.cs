using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using DesignAutomationFramework;
using EgRoomTracker.Application;
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
        // Corrélation créée avant toute adaptation du contexte : le journal existe même si celle-ci échoue.
        // Identifiant du WorkItem (DAS_WORKITEM_ID) sur APS, sinon run-<guid> (exécution locale).
        var workItem = WorkItemContext.Read(Environment.GetEnvironmentVariable, Directory.GetCurrentDirectory());
        var correlationId = workItem.CorrelationId(Guid.NewGuid);
        // Même journal sur la console (rapport APS) et dans result/log.jsonl (ZIP de sortie), vidé et fermé avant le zip.
        using var log = new JsonConsoleLogService(correlationId, filePath: ActivityContract.LogFilePath(Directory.GetCurrentDirectory()));
        try
        {
            // Le dossier de sortie (zippé par l'Activity) doit exister même si l'extraction échoue avant d'y écrire.
            Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), ActivityContract.ResultDirectoryName));
            var request = ActivityContract.CreateRequest(e.DesignAutomationData) with
            {
                CorrelationId = correlationId,
                ApsEngine = workItem.EngineId,
            };
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
