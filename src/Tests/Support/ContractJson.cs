using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using EgRoomTracker.Contracts.Results;

namespace EgRoomTracker.Tests.Support;

/// <summary>
/// Sérialisation de référence des DTO, propre aux tests : conventions du schéma (camelCase, énumérations
/// en camelCase, null écrits, clés de dictionnaire conservées) et décisions du chef de projet : dates UTC ISO 8601
/// suffixées <c>Z</c>, accents écrits tels quels en UTF-8. Elle vérifie que les DTO du contrat peuvent
/// produire un <c>result.json</c> conforme. Ce n'est pas l'export de production (<c>ResultExportService</c>,
/// EGR-12) : celui-ci est couvert par <c>Export/ResultExportServiceTests</c>, qui vérifie aussi que les deux
/// sérialisations restent équivalentes.
/// </summary>
internal static class ContractJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false),
            new UtcDateTimeOffsetConverter(),
        },
    };

    public static string Serialize(ExtractionResult result) => JsonSerializer.Serialize(result, Options);

    public static byte[] SerializeToUtf8Bytes(ExtractionResult result) => JsonSerializer.SerializeToUtf8Bytes(result, Options);

    public static ExtractionResult Deserialize(string json) =>
        JsonSerializer.Deserialize<ExtractionResult>(json, Options) ?? throw new InvalidOperationException("result.json vide.");

    /// <summary>Dates UTC au format ISO 8601 avec suffixe <c>Z</c> (ex. <c>2026-10-09T12:00:03Z</c>).</summary>
    private sealed class UtcDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
    {
        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            DateTimeOffset.Parse(reader.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture));
    }
}
