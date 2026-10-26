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

    [Fact]
    public void RoomCount_IsComputedOnce_ForTheLogAndTheEnvelope()
    {
        // L'anomalie dit 7 pièces alors que la fixture en contient 3 : les deux sorties lisent le même nombre (celui de l'anomalie).
        var anomalies = new[] { Rooms.VolumeNotComputed(7) };

        var count = DocumentAnomalies.VolumeNotComputedRooms(anomalies);
        var info = Assert.Single(GenericMapper.Map(GenericFixtures.Result(anomalies: anomalies)).Warnings, w => w.Code == "VOLUME_NOT_COMPUTED");

        Assert.Equal(7, count);
        Assert.Equal(DocumentAnomalies.VolumeNotComputedMessage(7), info.Message);
        Assert.Null(DocumentAnomalies.VolumeNotComputedRooms([]));
    }

    [Fact]
    public void Warnings_WithBothInfosOfTheGuards_StayAtTwenty_AndKeepBothInfos()
    {
        var anomalies = Enumerable.Range(1, 30)
            .Select(i => Rooms.RoomAnomaly($"CODE_{i:00}", "101", AnomalySeverity.Warning))
            .Append(Rooms.VolumeNotComputed(3))
            .ToList();
        var rooms = Enumerable.Range(1, 21)
            .Select(i => GenericFixtures.Room(i, i.ToString(System.Globalization.CultureInfo.InvariantCulture), GenericFixtures.Box(i * 5000.0), null, [GenericFixtures.Equipment("1")]))
            .ToList();
        var result = GenericFixtures.Result(anomalies: anomalies);

        var envelope = GenericMapper.Map(result with { Rooms = rooms, Summary = result.Summary with { Counts = new RoomCounts(21, 21, 0) } });

        Assert.Equal(GenericWarning.MaxItems, envelope.Warnings.Count);
        Assert.Contains(envelope.Warnings, w => w.Code == "VOLUME_NOT_COMPUTED");
        Assert.Contains(envelope.Warnings, w => w.Code == "SHARED_ELEMENT_MANY_ROOMS");
        Assert.Equal(["info", "info"], envelope.Warnings.TakeLast(2).Select(w => w.Severity));
    }

    // La fixture porte déjà 1 avertissement (pièce sans boîte) ; on y ajoute N avertissements d'anomalie et 1 information (VOLUME_NOT_COMPUTED).
    // Les anomalies de gravité info ne sont jamais écrites comme avertissements.
    [Theory]
    [InlineData(18, false)] // 1 + 18 avertissements + 1 information = 20 pile : rien n'est coupé (un « < 20 » casserait ce cas)
    [InlineData(19, true)] // 1 + 19 + 1 = 21 : un avertissement de moins, remplacé par un résumé « 2 autre(s) » ; le total reste 20
    public void Cap_IsExactlyTwenty_WithoutLosingInfos(int anomalyWarnings, bool summarized)
    {
        var anomalies = Enumerable.Range(1, anomalyWarnings)
            .Select(i => Rooms.RoomAnomaly($"CODE_{i:00}", "101", AnomalySeverity.Warning))
            .Append(Rooms.VolumeNotComputed(3))
            .ToList();

        var envelope = GenericMapper.Map(GenericFixtures.Result(anomalies: anomalies));

        Assert.Equal(GenericWarning.MaxItems, envelope.Warnings.Count);
        Assert.Equal(summarized, envelope.Warnings.Any(w => w.Message.Contains("autre(s) avertissement(s) non détaillé(s)", StringComparison.Ordinal)));
        Assert.Equal("VOLUME_NOT_COMPUTED", envelope.Warnings[^1].Code);
        if (summarized)
        {
            Assert.Equal("2 autre(s) avertissement(s) non détaillé(s) ; voir result.json.", envelope.Warnings[^2].Message);
        }
    }

    [Fact]
    public void OmittedWarnings_AreCountedInTheSummary_AndTheCountAddsUp()
    {
        const int anomalyWarnings = 30;
        var anomalies = Enumerable.Range(1, anomalyWarnings)
            .Select(i => Rooms.RoomAnomaly($"CODE_{i:00}", "101", AnomalySeverity.Warning))
            .Append(Rooms.VolumeNotComputed(3))
            .ToList();

        var envelope = GenericMapper.Map(GenericFixtures.Result(anomalies: anomalies));

        // 31 avertissements (30 + pièce sans boîte) ; 19 places (20 moins l'information) : 18 gardés + 1 résumé qui compte les 13 autres.
        var detailed = envelope.Warnings.Count(w => w.Severity == "warning" && !w.Message.Contains("autre(s)", StringComparison.Ordinal));
        var summary = Assert.Single(envelope.Warnings, w => w.Message.Contains("autre(s) avertissement(s) non détaillé(s)", StringComparison.Ordinal));
        Assert.Equal(18, detailed);
        Assert.Equal("13 autre(s) avertissement(s) non détaillé(s) ; voir result.json.", summary.Message);
        Assert.Equal(GenericWarning.MaxItems, envelope.Warnings.Count);
    }

    [Fact]
    public void RoomsAffectedAbsent_CountsZero_AndGivesNoInfoInTheEnvelope()
    {
        var anomaly = Rooms.VolumeNotComputed(3) with { Details = new Dictionary<string, object?> { ["volumeMode"] = "preserve" } };

        Assert.Equal(0, DocumentAnomalies.VolumeNotComputedRooms([anomaly]));
        Assert.DoesNotContain(GenericMapper.Map(GenericFixtures.Result(anomalies: [anomaly])).Warnings, w => w.Code == "VOLUME_NOT_COMPUTED");
    }
}
