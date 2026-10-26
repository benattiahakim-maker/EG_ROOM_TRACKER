using System.Text.Json.Nodes;
using EgRoomTracker.Application.Generic;
using EgRoomTracker.Application.Revit;
using EgRoomTracker.Contracts.Codes;
using EgRoomTracker.Contracts.Generic;
using EgRoomTracker.Contracts.Results;
using EgRoomTracker.Tests.Support;

namespace EgRoomTracker.Tests.Application;

/// <summary>Sortie generic : VOLUME_NOT_COMPUTED n'est plus un objet, seulement une information unique dans warnings[].</summary>
public sealed class GenericVolumeInfoTests
{
    [Fact]
    public void VolumeNotComputed_IsOneInfoWithTheRoomCount_AndNoAnomalyObject()
    {
        var envelope = GenericMapper.Map(GenericFixtures.Result(anomalies: [Rooms.VolumeNotComputed(3)]));
        var json = JsonNode.Parse(GenericJson.Serialize(envelope))!;

        var info = Assert.Single(envelope.Warnings, w => w.Code == "VOLUME_NOT_COMPUTED");
        Assert.Equal("info", info.Severity);
        Assert.StartsWith("3 pièce(s) sans volume calculé", info.Message, StringComparison.Ordinal);
        Assert.True(info.Message.Length <= GenericWarning.MaxMessageLength);
        Assert.DoesNotContain(GenericEnvelopeChecks.AllObjects(json), o => o["name"]!.GetValue<string>() == "VOLUME_NOT_COMPUTED");
        GenericEnvelopeChecks.AssertValid(json);
    }

    [Fact]
    public void VolumeNotComputed_NeverMakesTheEnvelopePartial()
    {
        var result = GenericFixtures.Result();
        var complete = result with { Rooms = [result.Rooms[0], result.Rooms[1], result.Rooms[2] with { BoundingBox = GenericFixtures.Box(9000) }], Anomalies = [Rooms.VolumeNotComputed(3)] };

        Assert.Equal("done", GenericMapper.Map(complete).Status);
    }

    [Fact]
    public void WithoutTheAnomaly_ThereIsNoInfo()
    {
        var envelope = GenericMapper.Map(GenericFixtures.Result());

        Assert.DoesNotContain(envelope.Warnings, w => w.Code == "VOLUME_NOT_COMPUTED");
    }

    [Fact]
    public void TwoAnomalies_GiveASingleInfo()
    {
        var envelope = GenericMapper.Map(GenericFixtures.Result(anomalies: [Rooms.VolumeNotComputed(3), Rooms.VolumeNotComputed(3)]));

        Assert.Single(envelope.Warnings, w => w.Code == "VOLUME_NOT_COMPUTED");
    }

    [Fact]
    public void OtherDocumentAnomalies_StayRootObjects_AfterTheLevelObjects()
    {
        var envelope = JsonNode.Parse(GenericJson.Serialize(GenericMapper.Map(GenericFixtures.Result(anomalies: [Rooms.VolumeNotComputed(3), DocumentAnomalies.LinkNotLoaded("Lien.rvt", null, "NotLoaded")]))))!;

        var ids = envelope["objects"]!.AsArray().Select(o => o!["id"]!.GetValue<string>()).ToList();

        Assert.StartsWith("level-", ids[0], StringComparison.Ordinal);
        Assert.Equal("anomaly-1", ids[^1]);
        Assert.Equal(DocumentAnomalyCodes.LinkNotLoaded, envelope["objects"]!.AsArray().Last()!["name"]!.GetValue<string>());
    }

    [Fact]
    public void Warnings_NeverExceedTwentyItems_AndInfosAreKept()
    {
        var anomalies = Enumerable.Range(1, 30)
            .Select(i => Rooms.RoomAnomaly($"CODE_{i:00}", "101", AnomalySeverity.Warning))
            .Append(Rooms.VolumeNotComputed(3))
            .ToList();

        var envelope = GenericMapper.Map(GenericFixtures.Result(anomalies: anomalies));

        Assert.Equal(GenericWarning.MaxItems, envelope.Warnings.Count);
        Assert.Contains(envelope.Warnings, w => w.Code == "VOLUME_NOT_COMPUTED");
        Assert.Contains(envelope.Warnings, w => w.Message.Contains("autre(s) avertissement(s) non détaillé(s)", StringComparison.Ordinal));
        Assert.Equal("partial", envelope.Status);
    }
}
