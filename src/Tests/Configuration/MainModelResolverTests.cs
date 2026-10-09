using EgRoomTracker.Application;
using EgRoomTracker.Application.Configuration;
using EgRoomTracker.Application.Revit;
using EgRoomTracker.Contracts.Codes;

namespace EgRoomTracker.Tests.Configuration;

/// <summary>
/// Résolution du RVT principal dans le ZIP décompressé (section 2, décision D4) : RVT unique détecté, plusieurs RVT
/// sans chemin refusés (jamais de choix arbitraire), confinement au répertoire d'entrée (<c>..</c>, absolus, liens symboliques).
/// </summary>
public sealed class MainModelResolverTests : IDisposable
{
    private readonly InputFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Theory]
    [InlineData("Projet/EG_Batiment_A.rvt")]
    [InlineData("A.rvt")]
    [InlineData("Projet/Sous dossier/Été 2026.RVT")]
    public void SingleRvt_IsResolvedAutomatically(string path)
    {
        _fixture.AddFiles(path, "Projet/notes.txt", "Projet/plan.dwg");

        Assert.Equal(path, Resolve(null));
    }

    [Fact]
    public void SingleRvt_IgnoresRevitBackupsAndMacOsMetadata()
    {
        _fixture.AddFiles(
            "Projet/EG_Batiment_A.rvt",
            "Projet/EG_Batiment_A.0001.rvt",
            "Projet/EG_Batiment_A.0002.RVT",
            "Projet/._EG_Batiment_A.rvt",
            "__MACOSX/Projet/._EG_Batiment_A.rvt");

        Assert.Equal("Projet/EG_Batiment_A.rvt", Resolve(null));
    }

    [Fact]
    public void NoRvt_IsRvtNotFound()
    {
        _fixture.AddFiles("Projet/notes.txt", "Projet/EG_Batiment_A.0001.rvt", "Projet/A.rvt.zip");

        AssertFails(ErrorCodes.RvtNotFound, Stages.Input, null);
    }

    [Fact]
    public void MissingInputDirectory_IsRvtNotFound()
    {
        Directory.Delete(_fixture.InputDirectory);

        AssertFails(ErrorCodes.RvtNotFound, Stages.Input, null);
    }

    [Fact]
    public void SeveralRvtWithoutPath_IsRvtAmbiguous_ListingCandidatesInStableOrder()
    {
        _fixture.AddFiles("Projet/Liens/Structure.rvt", "Projet/EG_Batiment_A.rvt", "Archive/EG_Batiment_A_v1.rvt");

        var ex = AssertFails(ErrorCodes.RvtAmbiguous, Stages.Input, null);

        Assert.Contains("3 fichiers .rvt", ex.Error.Message);
        Assert.Contains("Archive/EG_Batiment_A_v1.rvt, Projet/EG_Batiment_A.rvt, Projet/Liens/Structure.rvt", ex.Error.Message);
        Assert.Contains("mainModelPath", ex.Error.Message);
    }

    [Fact]
    public void AmbiguityMessage_IsTruncated()
    {
        for (var i = 0; i < MainModelResolver.MaxListedCandidates + 5; i++)
        {
            _fixture.AddFile($"M{i:00}.rvt");
        }

        var ex = AssertFails(ErrorCodes.RvtAmbiguous, Stages.Input, null);

        Assert.Contains("M09.rvt, …", ex.Error.Message);
        Assert.DoesNotContain("M10.rvt", ex.Error.Message);
    }

    [Fact]
    public void ExplicitPath_SelectsAmongSeveralRvt()
    {
        _fixture.AddFiles("Projet/EG_Batiment_A.rvt", "Projet/Liens/Structure.rvt");

        Assert.Equal("Projet/EG_Batiment_A.rvt", Resolve("Projet/EG_Batiment_A.rvt"));
    }

    [Theory]
    [InlineData("Projet\\EG_Batiment_A.rvt")]
    [InlineData("./Projet/EG_Batiment_A.rvt")]
    [InlineData("Projet/Liens/../EG_Batiment_A.rvt")]
    [InlineData("Projet//EG_Batiment_A.rvt")]
    public void ExplicitPath_IsNormalized(string mainModelPath)
    {
        _fixture.AddFiles("Projet/EG_Batiment_A.rvt", "Projet/Liens/Structure.rvt");

        Assert.Equal("Projet/EG_Batiment_A.rvt", Resolve(mainModelPath));
    }

    [Fact]
    public void ExplicitPath_MayTargetABackupOnPurpose()
    {
        _fixture.AddFiles("A.rvt", "A.0001.rvt");

        Assert.Equal("A.0001.rvt", Resolve("A.0001.rvt"));
    }

    [Theory]
    [InlineData("Projet/Absent.rvt")]
    [InlineData("Absent/EG_Batiment_A.rvt")]
    [InlineData("Projet/notes.txt")]
    [InlineData("Projet")]
    [InlineData(".")]
    public void ExplicitPath_NotAnExistingRvt_IsRvtNotFound(string mainModelPath)
    {
        _fixture.AddFiles("Projet/EG_Batiment_A.rvt", "Projet/notes.txt");

        AssertFails(ErrorCodes.RvtNotFound, Stages.Input, mainModelPath);
    }

    [Theory]
    [InlineData("../outside/A.rvt")]
    [InlineData("..")]
    [InlineData("Projet/../../outside/A.rvt")]
    [InlineData("..\\outside\\A.rvt")]
    [InlineData("/etc/A.rvt")]
    [InlineData("\\outside\\A.rvt")]
    [InlineData("C:\\Projets\\A.rvt")]
    [InlineData("c:A.rvt")]
    [InlineData("\\\\serveur\\partage\\A.rvt")]
    [InlineData("//serveur/partage/A.rvt")]
    public void ExplicitPath_OutsideInput_IsPathOutsideInput(string mainModelPath)
    {
        _fixture.AddFile("Projet/EG_Batiment_A.rvt");
        _fixture.AddOutsideFile("A.rvt");

        AssertFails(ErrorCodes.PathOutsideInput, Stages.Configuration, mainModelPath);
    }

    [Fact]
    public void ExplicitPath_ToSiblingDirectorySharingThePrefix_IsPathOutsideInput()
    {
        // input-evil commence comme input : le confinement compare des chemins, pas des préfixes de chaîne.
        var sibling = _fixture.InputDirectory + "-evil";
        Directory.CreateDirectory(sibling);
        File.WriteAllBytes(Path.Combine(sibling, "A.rvt"), [1]);

        AssertFails(ErrorCodes.PathOutsideInput, Stages.Configuration, "../input-evil/A.rvt");
    }

    [Theory]
    [InlineData("Projet/A*.rvt")]
    [InlineData("Projet/A?.rvt")]
    [InlineData("Projet/A\0.rvt")]
    public void ExplicitPath_WithForbiddenCharacters_IsConfigInvalid(string mainModelPath) =>
        AssertFails(ErrorCodes.ConfigInvalid, Stages.Configuration, mainModelPath);

    [SkippableUnixFact]
    public void ExplicitPath_ThroughFileSymlinkToOutside_IsPathOutsideInput()
    {
        var target = _fixture.AddOutsideFile("Secret.rvt");
        _fixture.Link("Projet/EG_Batiment_A.rvt", target);

        AssertFails(ErrorCodes.PathOutsideInput, Stages.Configuration, "Projet/EG_Batiment_A.rvt");
    }

    [SkippableUnixFact]
    public void ExplicitPath_ThroughDirectorySymlinkToOutside_IsPathOutsideInput()
    {
        _fixture.AddOutsideFile("Secret.rvt");
        _fixture.Link("Projet", _fixture.OutsideDirectory, directory: true);

        AssertFails(ErrorCodes.PathOutsideInput, Stages.Configuration, "Projet/Secret.rvt");
    }

    [SkippableUnixFact]
    public void ExplicitPath_ThroughSymlinkStayingInside_IsAccepted()
    {
        _fixture.AddFile("Projet/EG_Batiment_A.rvt");
        _fixture.Link("Raccourci.rvt", Path.Combine(_fixture.InputDirectory, "Projet", "EG_Batiment_A.rvt"));

        Assert.Equal("Raccourci.rvt", Resolve("Raccourci.rvt"));
    }

    [SkippableUnixFact]
    public void AutomaticDetection_DoesNotFollowSymlinks()
    {
        _fixture.AddFile("Projet/EG_Batiment_A.rvt");
        var target = _fixture.AddOutsideFile("Secret.rvt");
        _fixture.Link("Projet/Lien.rvt", target);
        _fixture.Link("Externe", _fixture.OutsideDirectory, directory: true);

        Assert.Equal("Projet/EG_Batiment_A.rvt", Resolve(null));
    }

    [SkippableUnixFact]
    public void InputDirectoryItselfBehindASymlink_IsSupported()
    {
        _fixture.AddFile("Projet/EG_Batiment_A.rvt");
        var alias = Path.Combine(_fixture.Root, "alias");
        Directory.CreateSymbolicLink(alias, _fixture.InputDirectory);

        Assert.Equal("Projet/EG_Batiment_A.rvt", MainModelResolver.Resolve(alias, "Projet/EG_Batiment_A.rvt"));
        Assert.Equal("Projet/EG_Batiment_A.rvt", MainModelResolver.Resolve(alias, null));
    }

    [Theory]
    [InlineData("A.rvt", true)]
    [InlineData("Projet/A.RVT", true)]
    [InlineData("Projet\\A.rvt", true)]
    [InlineData("A.0001.rvt", false)]
    [InlineData("A.2026.rvt", false)]
    [InlineData("A.001.rvt", true)]
    [InlineData("A.v0001.rvt", true)]
    [InlineData("._A.rvt", false)]
    [InlineData("__MACOSX/A.rvt", false)]
    [InlineData("A.rfa", false)]
    [InlineData("A.rvt.bak", false)]
    public void IsCandidate(string relativePath, bool expected) =>
        Assert.Equal(expected, MainModelResolver.IsCandidate(relativePath));

    [Fact]
    public void ResolvedPath_IsAcceptedByDocumentService()
    {
        // Contrat avec EGR-06 : MainModelLocation (contrôle défensif) accepte le chemin résolu tel quel.
        _fixture.AddFile("Projet/EG_Batiment_A.rvt");

        var resolved = Resolve(null);
        var (fullPath, relativePath) = MainModelLocation.Resolve(_fixture.InputDirectory, resolved);

        Assert.Equal(resolved, relativePath);
        Assert.True(File.Exists(fullPath));
    }

    private string Resolve(string? mainModelPath) => MainModelResolver.Resolve(_fixture.InputDirectory, mainModelPath);

    private ExtractionFailedException AssertFails(string code, string stage, string? mainModelPath)
    {
        var ex = Assert.Throws<ExtractionFailedException>(() => Resolve(mainModelPath));
        Assert.Equal(code, ex.Error.Code);
        Assert.Equal(stage, ex.Error.Stage);
        Assert.DoesNotContain(_fixture.Root, ex.Error.Message);
        return ex;
    }
}

/// <summary>Test ignoré hors Linux/macOS : la création de liens symboliques exige des privilèges sous Windows.</summary>
public sealed class SkippableUnixFactAttribute : FactAttribute
{
    public SkippableUnixFactAttribute()
    {
        if (OperatingSystem.IsWindows())
        {
            Skip = "Liens symboliques : test exécuté sous Linux/macOS uniquement.";
        }
    }
}
