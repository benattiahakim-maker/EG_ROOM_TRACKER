using EgRoomTracker.Application.Revit;

namespace EgRoomTracker.Tests.Application;

/// <summary>Passage libre d'une porte à partir des croisements d'une ligne de coupe (sans Revit).</summary>
public sealed class OpeningMeasureTests
{
    // Positions en mètres le long de la ligne, de gauche à droite.
    private static readonly double[] SingleLeafWithFrame = [-0.55, -0.45, -0.44, 0.44, 0.45, 0.55];
    private static readonly double[] DoubleLeafWithFrame = [-0.95, -0.90, -0.89, -0.001, 0.001, 0.89, 0.90, 0.95];

    [Fact]
    public void Width_IsBetweenTheInnerFacesOfTheFrame_ForOneLeaf() =>
        Assert.Equal(0.90, OpeningMeasure.ClearWidth(SingleLeafWithFrame)!.Value, 6);

    [Fact]
    public void Width_IsTheWholePassage_ForTwoLeaves() =>
        Assert.Equal(1.80, OpeningMeasure.ClearWidth(DoubleLeafWithFrame)!.Value, 6);

    [Fact]
    public void Width_OfAFrameWithoutLeaf_IsBetweenInnerFaces() =>
        Assert.Equal(0.90, OpeningMeasure.ClearWidth([-0.55, -0.45, 0.45, 0.55])!.Value, 6);

    [Fact]
    public void Width_WithoutFrame_IsTheSpanBetweenFirstAndLastFace() =>
        Assert.Equal(0.88, OpeningMeasure.ClearWidth([-0.44, 0.44])!.Value, 6);

    [Fact]
    public void Width_IsIndependentOfTheOrderOfTheCrossings() =>
        Assert.Equal(0.90, OpeningMeasure.ClearWidth(SingleLeafWithFrame.Reverse())!.Value, 6);

    [Fact]
    public void Coincident_faces_count_once()
    {
        // Deux croisements confondus (faces du dormant et du mur) ne décalent pas la mesure.
        double[] crossings = [-0.55, -0.45, -0.449, 0.44, 0.45, 0.55, 0.5505];

        Assert.Equal(0.90, OpeningMeasure.ClearWidth(crossings)!.Value, 6);
    }

    [Theory]
    [InlineData(new double[] { })]
    [InlineData(new[] { 0.2 })]
    [InlineData(new[] { 0.0, 0.1 })]
    [InlineData(new[] { 0.0, 9.0 })]
    public void Width_IsNull_WhenNotMeasurable(double[] crossings) =>
        Assert.Null(OpeningMeasure.ClearWidth(crossings));

    [Fact]
    public void Width_IgnoresNonFiniteValues() =>
        Assert.Equal(0.90, OpeningMeasure.ClearWidth([double.NaN, ..SingleLeafWithFrame, double.PositiveInfinity])!.Value, 6);

    [Fact]
    public void Height_GoesFromTheBottomToTheUndersideOfTheHead() =>
        Assert.Equal(2.04, OpeningMeasure.ClearHeight([0.01, 2.04, 2.05, 2.15])!.Value, 6);

    [Fact]
    public void Height_WithoutHead_IsTheLeafHeight() =>
        Assert.Equal(2.04, OpeningMeasure.ClearHeight([0.0, 2.04])!.Value, 6);

    [Theory]
    [InlineData(new double[] { })]
    [InlineData(new[] { 1.0 })]
    [InlineData(new[] { 0.0, 0.2 })]
    [InlineData(new[] { 0.0, 7.0 })]
    public void Height_IsNull_WhenNotMeasurable(double[] crossings) =>
        Assert.Null(OpeningMeasure.ClearHeight(crossings));

    [Fact]
    public void Median_IgnoresMissingLines()
    {
        Assert.Equal(0.90, OpeningMeasure.Median([0.88, null, 0.90, 0.92])!.Value, 6);
        Assert.Equal(0.91, OpeningMeasure.Median([0.90, 0.92])!.Value, 6);
        Assert.Null(OpeningMeasure.Median([null, null]));
        Assert.Null(OpeningMeasure.Median([]));
    }
}
