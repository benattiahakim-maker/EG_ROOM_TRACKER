using EgRoomTracker.Contracts.Results;

namespace EgRoomTracker.Application;

/// <summary>Erreur globale (section 9.2) : interrompt le traitement avec le statut <c>failed</c>.</summary>
public sealed class ExtractionFailedException : Exception
{
    public ExtractionFailedException(string code, string stage, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Error = new GlobalError(code, stage, message);
    }

    public GlobalError Error { get; }
}
