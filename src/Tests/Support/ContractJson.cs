using System.Text.Json;
using System.Text.Json.Serialization;
using EgRoomTracker.Contracts.Results;

namespace EgRoomTracker.Tests.Support;

/// <summary>
/// Sérialisation de référence des DTO, propre aux tests : conventions du schéma (camelCase, énumérations
/// en camelCase, null écrits, clés de dictionnaire conservées). Elle vérifie que les DTO du contrat peuvent
/// produire un <c>result.json</c> conforme. Ce n'est pas l'export de production (<c>ResultExportService</c>,
/// EGR-12) : celui-ci est couvert par <c>ResultExportServiceSpecTests</c>, à activer avec EGR-12.
/// </summary>
internal static class ContractJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    public static string Serialize(ExtractionResult result) => JsonSerializer.Serialize(result, Options);

    public static byte[] SerializeToUtf8Bytes(ExtractionResult result) => JsonSerializer.SerializeToUtf8Bytes(result, Options);

    public static ExtractionResult Deserialize(string json) =>
        JsonSerializer.Deserialize<ExtractionResult>(json, Options) ?? throw new InvalidOperationException("result.json vide.");
}
