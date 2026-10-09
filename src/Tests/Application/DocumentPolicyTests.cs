using EgRoomTracker.Application;
using EgRoomTracker.Application.Revit;
using EgRoomTracker.Contracts.Codes;
using EgRoomTracker.Contracts.Results;

namespace EgRoomTracker.Tests.Application;

/// <summary>Règles d'accès au document indépendantes de Revit (EGR-06).</summary>
public sealed class DocumentPolicyTests : IDisposable
{
    private readonly string _input = Directory.CreateTempSubdirectory("egr06-").FullName;

    public void Dispose() => Directory.Delete(_input, recursive: true);

    [Fact]
    public void MainModelLocation_ResolvesExistingRvtInsideInput()
    {
        Directory.CreateDirectory(Path.Combine(_input, "Projet"));
        File.WriteAllText(Path.Combine(_input, "Projet", "EG_Batiment_A.rvt"), "x");

        var (fullPath, relativePath) = MainModelLocation.Resolve(_input, "Projet/EG_Batiment_A.rvt");

        Assert.Equal(Path.Combine(_input, "Projet", "EG_Batiment_A.rvt"), fullPath);
        Assert.Equal("Projet/EG_Batiment_A.rvt", relativePath);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Projet/absent.rvt")]
    [InlineData("notes.txt")]
    public void MainModelLocation_MissingOrUnresolved_FailsWithRvtNotFound(string? mainModelPath)
    {
        File.WriteAllText(Path.Combine(_input, "notes.txt"), "x");

        var ex = Assert.Throws<ExtractionFailedException>(() => MainModelLocation.Resolve(_input, mainModelPath));

        Assert.Equal(ErrorCodes.RvtNotFound, ex.Error.Code);
        Assert.Equal(Stages.Input, ex.Error.Stage);
    }

    [Theory]
    [InlineData("../autre.rvt")]
    [InlineData("Projet/../../autre.rvt")]
    public void MainModelLocation_OutsideInput_FailsWithPathOutsideInput(string mainModelPath)
    {
        var ex = Assert.Throws<ExtractionFailedException>(() => MainModelLocation.Resolve(_input, mainModelPath));

        Assert.Equal(ErrorCodes.PathOutsideInput, ex.Error.Code);
    }

    [Theory]
    [InlineData("2026", false, "2026", false)]
    [InlineData("2025", false, "2027", true)]
    [InlineData("Autodesk Revit 2024", false, "2026", true)]
    [InlineData(null, false, "2026", false)]
    public void VersionCheck_SameOrOlderFile_IsAccepted(string? format, bool later, string engine, bool upgraded)
    {
        var check = RevitVersionCompatibility.Check(format, later, engine);

        Assert.Equal(upgraded, check.UpgradedInMemory);
    }

    [Theory]
    [InlineData("2027", false, "2026")]
    [InlineData("2026", true, "2026")]
    [InlineData(null, true, "2025")]
    public void VersionCheck_LaterFile_FailsWithRevitVersionIncompatible(string? format, bool later, string engine)
    {
        var ex = Assert.Throws<ExtractionFailedException>(() => RevitVersionCompatibility.Check(format, later, engine));

        Assert.Equal(ErrorCodes.RevitVersionIncompatible, ex.Error.Code);
        Assert.Equal(Stages.Document, ex.Error.Stage);
    }

    [Fact]
    public void LinkMissing_IsDocumentWarningWithoutElementId()
    {
        var anomaly = DocumentAnomalies.LinkMissing("Structure.rvt", "Liens/Structure.rvt");

        Assert.Equal(DocumentAnomalyCodes.LinkMissing, anomaly.Code);
        Assert.Equal(AnomalySeverity.Warning, anomaly.Severity);
        Assert.Equal(AnomalyScope.Document, anomaly.Scope);
        Assert.Null(anomaly.ElementId);
        Assert.Equal("Liens/Structure.rvt", anomaly.Details["linkPath"]);
    }

    [Fact]
    public void RevitWarningsOnOpen_NoWarning_ReturnsNull() =>
        Assert.Null(DocumentAnomalies.RevitWarningsOnOpen([]));

    [Fact]
    public void RevitWarningsOnOpen_AggregatesAndLimitsDetail()
    {
        var warnings = Enumerable.Range(0, 30).Select(i => $"Avertissement {i}")
            .Concat(Enumerable.Repeat("Pièce non fermée", 5))
            .ToList();

        var anomaly = DocumentAnomalies.RevitWarningsOnOpen(warnings)!;

        Assert.Equal(DocumentAnomalyCodes.RevitWarningOnOpen, anomaly.Code);
        Assert.Equal(AnomalyScope.Document, anomaly.Scope);
        Assert.Equal(35, anomaly.Details["warningCount"]);
        Assert.Equal(31, anomaly.Details["distinctCount"]);
        var detail = Assert.IsAssignableFrom<IReadOnlyList<object?>>(anomaly.Details["warnings"]);
        Assert.Equal(DocumentAnomalies.MaxDetailedMessages, detail.Count);
        var first = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(detail[0]);
        Assert.Equal("Pièce non fermée", first["description"]);
        Assert.Equal(5, first["count"]);
    }
}
