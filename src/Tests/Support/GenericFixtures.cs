using EgRoomTracker.Contracts.Codes;
using EgRoomTracker.Contracts.Configuration;
using EgRoomTracker.Contracts.Results;

namespace EgRoomTracker.Tests.Support;

/// <summary>Résultat d'extraction de référence pour la sortie generic : trois pièces, une porte entre deux pièces, un équipement de lien.</summary>
internal static class GenericFixtures
{
    public const string SourceFile = "EG_Batiment_A.rvt";

    public static string CodeFile => EgRoomTracker.Application.TrcNaming.CodeFile(SourceFile);

    public static RoomElementDto Door(string id, string relation, string? level = "R+0") => new()
    {
        ElementId = id,
        UniqueId = $"door-uid-{id}",
        Category = "Portes",
        Family = "Porte simple",
        Type = "0915 x 2134",
        Mark = "P01",
        Number = "1.02",
        Level = level,
        Relation = relation,
        WidthM = 0.9,
        HeightM = 2.04,
        DimensionSource = "geometry",
        Parameters = new Dictionary<string, object?> { ["Commentaires"] = "Coupe-feu", ["Marque"] = "P01", ["Sans valeur"] = null },
        TypeParameters = new Dictionary<string, object?> { ["Largeur"] = 0.9, ["Epaisseur"] = 40L },
    };

    public static RoomElementDto Equipment(string id, string? link = null) => new()
    {
        ElementId = id,
        UniqueId = $"equip-uid-{id}",
        Category = "Equipement de génie climatique",
        Family = "CTA",
        Type = "CTA 5000",
        Level = "R+0",
        Relation = ElementRoomRelation.In,
        Discipline = "cvc",
        LinkName = link,
        Parameters = new Dictionary<string, object?> { ["Puissance"] = 12.5, ["Actif"] = true, ["Identifiant long"] = 9_000_000_000_000_000_000L },
    };

    public static RoomDto Room(long id, string number, BoundingBoxMm? box, IReadOnlyList<RoomElementDto>? doors = null, IReadOnlyList<RoomElementDto>? equipment = null) => new()
    {
        ElementId = id.ToString(System.Globalization.CultureInfo.InvariantCulture),
        UniqueId = $"room-uid-{id}",
        Name = "Bureau",
        Number = number,
        AreaM2 = 18.425,
        Level = new LevelRef("311", "level-uid", "R+0"),
        VolumeStatus = VolumeStatus.NotComputed,
        BoundingBox = box,
        Doors = doors,
        Equipment = equipment,
        Parameters = new Dictionary<string, object?> { ["Hauteur limite"] = 2.7, ["Commentaires"] = "RAS" },
    };

    public static BoundingBoxMm Box(double offset = 0) => new(offset, 0, 0, offset + 4300, 4285, 2700);

    /// <summary>R100 (porte de R100 vers R101, CTA), R101 (même porte), R102 (non placée, sans boîte).</summary>
    public static ExtractionResult Result(ExtractionStatus status = ExtractionStatus.Succeeded, IReadOnlyList<AnomalyDto>? anomalies = null, bool boxes = true) => new()
    {
        Summary = new ExtractionSummary
        {
            CorrelationId = "wi",
            ExtractorVersion = "0.1.0",
            Revit = new RevitInfo("2026", null),
            Source = new SourceInfo(SourceFile, $"Projet/{SourceFile}", false),
            StartedAtUtc = DateTimeOffset.UnixEpoch,
            CompletedAtUtc = DateTimeOffset.UnixEpoch,
            Options = new AppliedOptions(null, VolumeMode.Preserve, []),
            Counts = new RoomCounts(3, 3, 0),
            Status = status,
        },
        Rooms =
        [
            Room(100, "001", boxes ? Box() : null, doors: [Door("5001", ElementRoomRelation.From)], equipment: [Equipment("6001", link: "CVC lien")]),
            Room(101, "002", boxes ? Box(5000) : null, doors: [Door("5001", ElementRoomRelation.To)]),
            Room(102, "003", null),
        ],
        Anomalies = anomalies ?? [],
    };
}
