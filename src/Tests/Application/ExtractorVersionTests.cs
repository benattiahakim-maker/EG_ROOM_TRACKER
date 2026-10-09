using System.Text.Json.Nodes;
using System.Xml.Linq;
using EgRoomTracker.Application;

namespace EgRoomTracker.Tests.Application;

/// <summary>
/// Source unique de version : <c>&lt;Version&gt;</c> de Directory.Build.props, propagée à l'assembly (attribut
/// informationnel) et lue à l'exécution pour <c>summary.extractorVersion</c> ; l'exemple documenté reste aligné.
/// </summary>
public class ExtractorVersionTests
{
    private static string PropsVersion()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        var version = XDocument.Load(Path.Combine(dir!.FullName, "Directory.Build.props")).Descendants("Version").Single().Value.Trim();
        Assert.NotEmpty(version);
        return version;
    }

    [Fact]
    public void ExtractorVersion_IsTheDirectoryBuildPropsVersion() =>
        Assert.Equal(PropsVersion(), RoomExtractionService.ExtractorVersion);

    [Fact]
    public void ResultExample_UsesTheCurrentExtractorVersion()
    {
        var example = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "result.example.json")))!;

        Assert.Equal(RoomExtractionService.ExtractorVersion, example["summary"]!["extractorVersion"]!.GetValue<string>());
    }
}
