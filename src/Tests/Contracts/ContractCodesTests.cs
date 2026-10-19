using System.Text.Json;
using EgRoomTracker.Contracts.Codes;

namespace EgRoomTracker.Tests.Contracts;

/// <summary>Les codes du contrat C# correspondent aux énumérations de docs/schema/result.schema.json.</summary>
public class ContractCodesTests
{
    private static readonly JsonElement Defs = LoadSchemaDefs();

    [Fact]
    public void QualityFlagCodes_MatchSchema() =>
        Assert.Equal(SchemaEnum("qualityFlag"), QualityFlagCodes.All.Order());

    [Fact]
    public void DocumentAnomalyCodes_MatchSchema() =>
        Assert.Equal(SchemaEnum("documentAnomalyCode"), DocumentAnomalyCodes.All.Order());

    [Fact]
    public void ErrorCodes_MatchSchema() =>
        Assert.Equal(SchemaEnum("errorCode"), ErrorCodes.All.Order());

    [Fact]
    public void ResultSchemaVersion_MatchesSchemaPattern() =>
        Assert.Matches(Defs.GetProperty("__root_pattern").GetString()!, SchemaVersions.Result);

    [Fact]
    public void LinkNotLoaded_IsDocumentAnomalyInSchema() =>
        Assert.Contains(DocumentAnomalyCodes.LinkNotLoaded, SchemaEnum("documentAnomalyCode"));

    [Fact]
    public void UnsupportedParameterType_IsDocumentAnomalyInSchema()
    {
        Assert.Contains(DocumentAnomalyCodes.UnsupportedParameterType, SchemaEnum("documentAnomalyCode"));
        Assert.DoesNotContain(DocumentAnomalyCodes.UnsupportedParameterType, SchemaEnum("qualityFlag"));
    }

    [Fact]
    public void ResultExample_UsesCurrentResultSchemaVersion()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Data", "result.example.json");
        using var example = JsonDocument.Parse(File.ReadAllText(path));

        Assert.Equal(SchemaVersions.Result, example.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal("1.4", SchemaVersions.Result);
    }

    private static IEnumerable<string> SchemaEnum(string name) =>
        Defs.GetProperty(name).GetProperty("enum").EnumerateArray().Select(e => e.GetString()!).Order();

    private static JsonElement LoadSchemaDefs()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Data", "result.schema.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        var defs = JsonSerializer.SerializeToNode(root.GetProperty("$defs"))!.AsObject();
        defs["__root_pattern"] = root.GetProperty("properties").GetProperty("schemaVersion").GetProperty("pattern").GetString();
        return JsonSerializer.SerializeToElement(defs);
    }
}
