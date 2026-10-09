using System.Text.Json;

namespace EgRoomTracker.Application;

/// <summary>
/// Contexte du WorkItem Design Automation exposé à l'add-in (APS DA Field Guide, « Expanded WorkItem Payload ») :
/// variable d'environnement <c>DAS_WORKITEM_ID</c> et fichier <c>&lt;id&gt;_job.das</c> (JSON : <c>Id</c>,
/// <c>EngineId</c>, …) dans le répertoire de travail. Lecture tolérante : toute absence ou erreur donne null.
/// </summary>
public sealed record WorkItemContext(string? WorkItemId, string? EngineId)
{
    public const string WorkItemIdVariable = "DAS_WORKITEM_ID";

    public static WorkItemContext Empty { get; } = new(null, null);

    /// <summary>Identifiant de corrélation : identifiant du WorkItem, sinon <c>run-&lt;guid&gt;</c> (exécution locale).</summary>
    public string CorrelationId(Func<Guid> newGuid) => WorkItemId ?? $"run-{newGuid():N}";

    public static WorkItemContext Read(Func<string, string?> getEnvironmentVariable, string workingDirectory)
    {
        try
        {
            var id = Normalize(getEnvironmentVariable(WorkItemIdVariable));
            if (id is null || !IsSafeFileToken(id))
            {
                return Empty;
            }

            return new WorkItemContext(id, ReadEngineId(workingDirectory, id));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Empty;
        }
    }

    private static string? ReadEngineId(string workingDirectory, string id)
    {
        // Le Field Guide écrit « _Job.das » dans le texte et « _job.das » dans son exemple de code : les deux sont essayés.
        foreach (var suffix in new[] { "_job.das", "_Job.das" })
        {
            var path = Path.Combine(workingDirectory, id + suffix);
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                using var json = JsonDocument.Parse(File.ReadAllText(path));
                return json.RootElement.ValueKind == JsonValueKind.Object
                    && TryGetString(json.RootElement, "EngineId", out var engine) ? engine : null;
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        return null;
    }

    private static bool TryGetString(JsonElement root, string name, out string? value)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String)
            {
                value = Normalize(property.Value.GetString());
                return value is not null;
            }
        }

        value = null;
        return false;
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsSafeFileToken(string value) =>
        value.Length <= 128 && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.') && !value.Contains("..", StringComparison.Ordinal);
}
