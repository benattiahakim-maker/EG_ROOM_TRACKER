namespace EgRoomTracker.Tests.Support;

/// <summary>Horloge figée : rend les dates de <c>summary</c> reproductibles.</summary>
internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public static DateTimeOffset Default { get; } = new(2026, 10, 9, 12, 0, 3, TimeSpan.Zero);

    public FixedTimeProvider() : this(Default)
    {
    }

    public override DateTimeOffset GetUtcNow() => now;
}
