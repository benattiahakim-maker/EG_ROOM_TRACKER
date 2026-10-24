using DesignAutomationFramework;
using EgRoomTracker.Application;
using EgRoomTracker.Application.Configuration;
using EgRoomTracker.Application.Generic;
using EgRoomTracker.Application.Quality;
using EgRoomTracker.Application.Trc;
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
        var units = new RevitUnitConversionService();

        return new RoomExtractionService(
            new InputConfigurationService(),
            new RevitDocumentService(context, log),
            new VolumeCalculationService(context),
            new RoomCollectionService(context, log),
            new RoomParameterReader(context, units),
            new QualityCheckService(),
            new ResultExportService(),
            log,
            elementServices: [new DoorService(context, units), new EquipmentService(context, units), new TerminalService(context, units)],
            imageService: new RoomImageService(context),
            heightService: new RoomHeightService(context, units),
            trcExport: new TrcExportService(),
            genericExport: new GenericExportService());
    }
}
