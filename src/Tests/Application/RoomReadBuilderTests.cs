using System.Text.Json;
using EgRoomTracker.Application.Revit;
using EgRoomTracker.Contracts.Codes;
using EgRoomTracker.Contracts.Results;

namespace EgRoomTracker.Tests.Application;

/// <summary>Lecture de l'identité des pièces (EGR-08) : représentation des valeurs et indicateurs posés à la lecture.</summary>
public class RoomReadBuilderTests
{
    private static RoomReadBuilder NewBuilder(long id = 412345) => new(id, "5b1e2f8a-00064b39", VolumeStatus.NotComputed);

    [Fact]
    public void Identity_KeepsStringsLeadingZerosAndEmptyValues()
    {
        var builder = NewBuilder();

        builder.Identity(
            builder.Text(ParameterReading.Of("Bureau"), "ROOM_NAME"),
            builder.Text(ParameterReading.Of("001"), "ROOM_NUMBER"),
            builder.Text(ParameterReading.Of(""), "ROOM_OCCUPANCY"));
        var result = builder.Placement(true).Level(new LevelRef("311", "lvl-uid", "R+0"), 311).Build();

        Assert.Equal("412345", result.Room.ElementId);
        Assert.Equal("5b1e2f8a-00064b39", result.Room.UniqueId);
        Assert.Equal("Bureau", result.Room.Name);
        Assert.Equal("001", result.Room.Number);
        Assert.Equal("", result.Room.Occupancy);
        Assert.Equal("R+0", result.Room.Level?.Name);
        Assert.True(result.Room.IsPlaced);
        Assert.Empty(result.Room.QualityFlags);
        Assert.Empty(result.Anomalies);
    }

    [Fact]
    public void ElementId_PreservesFull64BitValue()
    {
        var result = NewBuilder(9_007_199_254_740_993).Build();

        Assert.Equal("9007199254740993", result.Room.ElementId);
    }

    [Fact]
    public void NoValue_IsNullWithoutAnomaly()
    {
        var builder = NewBuilder();

        Assert.Null(builder.Text(ParameterReading.NoValue, "ROOM_OCCUPANCY"));
        Assert.Empty(builder.Build().Anomalies);
    }

    [Fact]
    public void AbsentParameters_AreNullWithSingleFlagAndOneAnomalyEach()
    {
        var builder = NewBuilder();

        builder.Identity(
            builder.Text(ParameterReading.Absent, "ROOM_NAME"),
            builder.Text(ParameterReading.Of("002"), "ROOM_NUMBER"),
            builder.Text(ParameterReading.Absent, "ROOM_OCCUPANCY"));
        var result = builder.Build();

        Assert.Null(result.Room.Name);
        Assert.Null(result.Room.Occupancy);
        Assert.Equal([QualityFlagCodes.MissingParameter], result.Room.QualityFlags);
        Assert.Equal(["ROOM_NAME", "ROOM_OCCUPANCY"], result.Anomalies.Select(a => a.Details["parameter"]));
        Assert.All(result.Anomalies, a =>
        {
            Assert.Equal(AnomalyScope.Room, a.Scope);
            Assert.Equal(AnomalySeverity.Warning, a.Severity);
            Assert.Equal("412345", a.ElementId);
        });
    }

    [Fact]
    public void MissingLevel_SetsLevelNotFound()
    {
        var result = NewBuilder().Level(null, 999).Build();

        Assert.Null(result.Room.Level);
        Assert.Equal([QualityFlagCodes.LevelNotFound], result.Room.QualityFlags);
        Assert.Equal("999", Assert.Single(result.Anomalies).Details["levelId"]);
    }

    [Fact]
    public void Unplaced_SetsFlagAnomalyAndIsPlacedFalse()
    {
        var result = NewBuilder().Placement(false).Build();

        Assert.False(result.Room.IsPlaced);
        Assert.Equal([QualityFlagCodes.Unplaced], result.Room.QualityFlags);
        var anomaly = Assert.Single(result.Anomalies);
        Assert.Equal(true, anomaly.Details["locationIsNull"]);
    }

    [Fact]
    public void PhaseAndDesignOption_AreReferencesOrNull()
    {
        var result = NewBuilder()
            .Phase(new ElementRef("86961", "Nouvelle construction"))
            .DesignOption(null)
            .Build();

        Assert.Equal(new ElementRef("86961", "Nouvelle construction"), result.Room.Phase);
        Assert.Null(result.Room.DesignOption);
    }

    [Fact]
    public void IsPlaced_IsNotSerialized()
    {
        var json = JsonSerializer.Serialize(NewBuilder().Placement(true).Build().Room);

        Assert.DoesNotContain("IsPlaced", json, StringComparison.OrdinalIgnoreCase);
    }
}
