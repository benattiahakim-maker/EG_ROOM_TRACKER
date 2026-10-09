namespace EgRoomTracker.Logging;

/// <summary>Implémentation sans effet, pour les tests. L'implémentation console relève d'EGR-04.</summary>
public sealed class NullLogService(string correlationId = "none") : ILogService
{
    public string CorrelationId { get; } = correlationId;

    public void Log(LogEvent logEvent)
    {
    }

    public void Flush()
    {
    }
}
