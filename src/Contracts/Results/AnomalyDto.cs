namespace EgRoomTracker.Contracts.Results;

/// <summary>Anomalie non bloquante (section 9.1), au niveau pièce ou document.</summary>
public sealed record AnomalyDto
{
    /// <summary>Code issu de <see cref="Codes.QualityFlagCodes"/> (pièce) ou <see cref="Codes.DocumentAnomalyCodes"/> (document).</summary>
    public required string Code { get; init; }

    public required AnomalySeverity Severity { get; init; }

    public required AnomalyScope Scope { get; init; }

    /// <summary>ElementId de la pièce ; null pour une anomalie de document.</summary>
    public string? ElementId { get; init; }

    public required string Message { get; init; }

    public IReadOnlyDictionary<string, object?> Details { get; init; } = new Dictionary<string, object?>();
}

public enum AnomalySeverity
{
    Info,
    Warning,
    Error,
}

public enum AnomalyScope
{
    Room,
    Document,
}
