using System.IO.Compression;
using System.Text;

namespace EgRoomTracker.Aps.Tests;

internal static class Support
{
    public const string Da = "https://da.test/v3";
    public const string Oss = "https://oss.test/oss/v2";
    public const string Token = "jeton-de-test";

    public static string DefinitionsDirectory => Path.Combine(AppContext.BaseDirectory, "Data", "definitions");

    public static (FakeHttpHandler Handler, HttpClient Client) Http()
    {
        var handler = new FakeHttpHandler();
        return (handler, new HttpClient(handler));
    }

    /// <summary>ZIP conforme à appbundle.py check pour <paramref name="version"/>.</summary>
    public static void WriteValidBundle(string path, string version, Action<Dictionary<string, byte[]>>? mutate = null)
    {
        const string contents = "EgRoomTracker.bundle/Contents";
        var framework = version == "2027" ? ".NETCoreApp,Version=v10.0" : ".NETCoreApp,Version=v8.0";
        var entries = new Dictionary<string, byte[]>
        {
            ["EgRoomTracker.bundle/PackageContents.xml"] = Encoding.UTF8.GetBytes(
                $"""
                <ApplicationPackage>
                  <Components>
                    <RuntimeRequirements OS="Win64" Platform="Revit" SeriesMin="R{version}" SeriesMax="R{version}" />
                    <ComponentEntry AppName="EgRoomTracker" ModuleName="./Contents/EgRoomTracker.addin" />
                  </Components>
                </ApplicationPackage>
                """),
            [$"{contents}/EgRoomTracker.addin"] = Encoding.UTF8.GetBytes(
                """
                <RevitAddIns>
                  <AddIn Type="DBApplication">
                    <Assembly>.\EgRoomTracker.dll</Assembly>
                    <AddInId>2f3c6c1e-6d1a-4e3b-9a55-0e8a9c4b7d21</AddInId>
                    <FullClassName>EgRoomTracker.EntryPoint.AutomationApplication</FullClassName>
                  </AddIn>
                </RevitAddIns>
                """),
            [$"{contents}/EgRoomTracker.dll"] = Encoding.UTF8.GetBytes("MZ…" + framework),
        };
        foreach (var name in new[] { "Application", "Contracts", "Export", "Logging", "Revit" })
        {
            entries[$"{contents}/EgRoomTracker.{name}.dll"] = [0x4D, 0x5A];
        }

        entries[$"{contents}/DesignAutomationBridge.dll"] = [0x4D, 0x5A];
        mutate?.Invoke(entries);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, data) in entries.OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            using var stream = archive.CreateEntry(name).Open();
            stream.Write(data);
        }
    }
}

/// <summary>Dossier temporaire supprimé en fin de test.</summary>
internal sealed class TempDirectory : IDisposable
{
    public string Path { get; } = Directory.CreateTempSubdirectory("egr-aps-").FullName;

    public void Dispose() => Directory.Delete(Path, recursive: true);
}
