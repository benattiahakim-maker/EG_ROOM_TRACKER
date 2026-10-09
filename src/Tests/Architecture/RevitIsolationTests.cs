using System.Reflection;
using EgRoomTracker.Application;
using EgRoomTracker.Contracts.Results;
using EgRoomTracker.Export;
using EgRoomTracker.Logging;

namespace EgRoomTracker.Tests.Architecture;

/// <summary>Le cœur (Contracts, Application, Export, Logging) ne dépend ni de Revit ni de Design Automation.</summary>
public class RevitIsolationTests
{
    public static TheoryData<string> CoreAssemblies => new()
    {
        typeof(ExtractionResult).Assembly.GetName().Name!,
        typeof(RoomExtractionService).Assembly.GetName().Name!,
        typeof(IExportService).Assembly.GetName().Name!,
        typeof(ILogService).Assembly.GetName().Name!,
    };

    [Theory]
    [MemberData(nameof(CoreAssemblies))]
    public void CoreAssembly_DoesNotReferenceRevit(string assemblyName)
    {
        var references = Assembly.Load(assemblyName).GetReferencedAssemblies().Select(r => r.Name ?? string.Empty);

        Assert.DoesNotContain(references, name =>
            name.StartsWith("RevitAPI", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("DesignAutomationBridge", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("EgRoomTracker.Revit", StringComparison.OrdinalIgnoreCase));
    }
}
