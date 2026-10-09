using EgRoomTracker.Contracts.Codes;
using EgRoomTracker.Contracts.Results;

namespace EgRoomTracker.Tests.Support;

/// <summary>Fabriques de DTO de pièces et d'anomalies, inspirées de docs/examples/result.example.json.</summary>
internal static class Rooms
{
    public static readonly Guid CodeLocalGuid = Guid.Parse("3f2b8c1e-6d4a-4b7e-9a51-2c0d8e7f4a10");

    public static readonly LevelRef R0 = new("311", "8d2c4f6a-0b1e-4c3d-9e8f-7a6b5c4d3e2f-00000137", "R+0");

    public static readonly ElementRef NewConstruction = new("86961", "Nouvelle construction");

    public static readonly ElementRef Existing = new("86960", "Existant");

    public static RoomDto Complete(long id, string? number = null, ElementRef? phase = null) => new()
    {
        ElementId = id.ToString(System.Globalization.CultureInfo.InvariantCulture),
        UniqueId = $"5b1e2f8a-1c3d-4e5f-8a9b-0c1d2e3f4a5b-{id:x8}",
        Name = "Bureau",
        Number = number ?? $"{id % 1000:000}",
        AreaM2 = 18.425,
        UnboundedHeightM = 2.7,
        Level = R0,
        Occupancy = "Bureau individuel",
        VolumeM3 = null,
        VolumeStatus = VolumeStatus.NotComputed,
        Finishes = new RoomFinishes("Parquet chêne", "Peinture blanche", "Faux plafond 60x60", "Plinthe bois"),
        Phase = phase ?? NewConstruction,
        DesignOption = null,
        SharedParameters = new Dictionary<string, SharedParameterValue> { ["codeLocal"] = new(CodeLocalGuid, "A-001") },
    };

    public static AnomalyDto RoomAnomaly(string code, string elementId, AnomalySeverity severity = AnomalySeverity.Warning) => new()
    {
        Code = code,
        Severity = severity,
        Scope = AnomalyScope.Room,
        ElementId = elementId,
        Message = $"{code} sur la pièce {elementId}.",
    };

    public static AnomalyDto VolumeNotComputed(int roomsAffected) => new()
    {
        Code = DocumentAnomalyCodes.VolumeNotComputed,
        Severity = AnomalySeverity.Info,
        Scope = AnomalyScope.Document,
        ElementId = null,
        Message = "Calcul des volumes désactivé dans le document ; volumeM3 exporté à null (mode preserve).",
        Details = new Dictionary<string, object?> { ["volumeMode"] = "preserve", ["roomsAffected"] = roomsAffected },
    };
}
