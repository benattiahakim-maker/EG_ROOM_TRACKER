using System.Text.Encodings.Web;
using System.Text.Json;
using EgRoomTracker.Contracts.Generic;

namespace EgRoomTracker.Application.Generic;

/// <summary>Sérialisation de l'enveloppe : JSON indenté de deux espaces, accents écrits tels quels, sans BOM (comme le TRC).</summary>
public static class GenericJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Serialize(GenericEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return JsonSerializer.Serialize(envelope, Options);
    }
}
