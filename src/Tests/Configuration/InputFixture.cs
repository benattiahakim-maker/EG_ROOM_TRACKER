namespace EgRoomTracker.Tests.Configuration;

/// <summary>Arborescence temporaire d'un WorkItem : <c>input/</c> (ZIP décompressé), <c>settings.json</c>, et un dossier extérieur.</summary>
internal sealed class InputFixture : IDisposable
{
    public InputFixture()
    {
        Root = Directory.CreateTempSubdirectory("egr05-").FullName;
        Directory.CreateDirectory(InputDirectory);
        Directory.CreateDirectory(OutsideDirectory);
    }

    public string Root { get; }

    public string InputDirectory => Path.Combine(Root, "input");

    public string OutsideDirectory => Path.Combine(Root, "outside");

    public string SettingsPath => Path.Combine(Root, "settings.json");

    /// <summary>Crée un fichier (contenu factice) à un chemin relatif à <c>input/</c> ; séparateur <c>/</c>.</summary>
    public InputFixture AddFile(string relativePath)
    {
        var path = Path.Combine(InputDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [0x52, 0x56, 0x54]);
        return this;
    }

    public InputFixture AddFiles(params string[] relativePaths)
    {
        foreach (var path in relativePaths)
        {
            AddFile(path);
        }

        return this;
    }

    public string AddOutsideFile(string name)
    {
        var path = Path.Combine(OutsideDirectory, name);
        File.WriteAllBytes(path, [0x52, 0x56, 0x54]);
        return path;
    }

    public void Link(string relativeLinkPath, string target, bool directory = false)
    {
        var path = Path.Combine(InputDirectory, relativeLinkPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (directory)
        {
            Directory.CreateSymbolicLink(path, target);
        }
        else
        {
            File.CreateSymbolicLink(path, target);
        }
    }

    public InputFixture WriteSettings(string json)
    {
        File.WriteAllText(SettingsPath, json);
        return this;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
