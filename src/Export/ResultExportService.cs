using EgRoomTracker.Contracts.Results;

namespace EgRoomTracker.Export;

/// <summary>Squelette ; sérialisation et finalisation atomique à implémenter en EGR-12 (#12).</summary>
public sealed class ResultExportService : IExportService
{
    public void Write(ExtractionResult result, string outputPath) =>
        throw new NotImplementedException("EGR-12 : export de result.json.");
}
