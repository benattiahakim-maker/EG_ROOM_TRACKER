using DesignAutomationFramework;
using EgRoomTracker.Application;
using EgRoomTracker.Application.Configuration;
using EgRoomTracker.Application.Quality;
using EgRoomTracker.Export;
using EgRoomTracker.Logging;
using EgRoomTracker.Revit;

namespace EgRoomTracker.EntryPoint;

/// <summary>Injection simple des dépendances, sans conteneur.</summary>
internal static class CompositionRoot
{
    public static RoomExtractionService CreateExtractionService(DesignAutomationData data, ILogService log)
    {
        var context = new RevitExecutionContext(data.RevitApp, data.RevitDoc);

        return new RoomExtractionService(
            new InputConfigurationService(),
            new RevitDocumentService(context, log),
            new VolumeCalculationService(context),
            new RoomCollectionService(context),
            new RoomParameterReader(context, new RevitUnitConversionService()),
            new QualityCheckService(),
            new ResultExportService(),
            log);
    }
}
