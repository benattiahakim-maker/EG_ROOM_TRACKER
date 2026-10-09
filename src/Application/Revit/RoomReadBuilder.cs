using System.Globalization;
using EgRoomTracker.Contracts.Codes;
using EgRoomTracker.Contracts.Results;

namespace EgRoomTracker.Application.Revit;

/// <summary>
/// Construction d'un <see cref="RoomDto"/> et des anomalies posées à la lecture (section 3 et 9.1), sans appel Revit.
/// Indicateurs posés ici : UNPLACED, MISSING_PARAMETER, LEVEL_NOT_FOUND (EXTRACTION_ERROR : orchestration).
/// DUPLICATE_NUMBER, ZERO_AREA et VOLUME_NOT_COMPUTED relèvent du contrôle qualité (EGR-11).
/// </summary>
public sealed class RoomReadBuilder
{
    private readonly string _elementId;
    private readonly List<string> _flags = [];
    private readonly List<AnomalyDto> _anomalies = [];
    private RoomDto _room;

    public RoomReadBuilder(long elementId, string uniqueId, VolumeStatus volumeStatus)
    {
        _elementId = elementId.ToString(CultureInfo.InvariantCulture);
        _room = new RoomDto { ElementId = _elementId, UniqueId = uniqueId, VolumeStatus = volumeStatus };
    }

    /// <summary>
    /// Valeur d'un paramètre texte : valeur telle quelle (chaîne vide et zéros initiaux conservés), null sans valeur,
    /// null et MISSING_PARAMETER (détail : paramètre) si absent.
    /// </summary>
    public string? Text(ParameterReading reading, string parameterName)
    {
        switch (reading.State)
        {
            case ParameterState.HasValue:
                return reading.Value;
            case ParameterState.Absent:
                AddRoomAnomaly(QualityFlagCodes.MissingParameter, AnomalySeverity.Warning,
                    $"Paramètre {parameterName} absent de la pièce.",
                    new Dictionary<string, object?> { ["parameter"] = parameterName });
                return null;
            default:
                return null;
        }
    }

    public RoomReadBuilder Identity(string? name, string? number, string? occupancy)
    {
        _room = _room with { Name = name, Number = number, Occupancy = occupancy };
        return this;
    }

    /// <summary>Niveau de la pièce ; null → LEVEL_NOT_FOUND (détail : LevelId lu, null si invalide).</summary>
    public RoomReadBuilder Level(LevelRef? level, long? levelId)
    {
        _room = _room with { Level = level };
        if (level is null)
        {
            AddRoomAnomaly(QualityFlagCodes.LevelNotFound, AnomalySeverity.Warning,
                "Niveau de la pièce introuvable (LevelId invalide ou niveau absent).",
                new Dictionary<string, object?> { ["levelId"] = levelId?.ToString(CultureInfo.InvariantCulture) });
        }

        return this;
    }

    /// <summary>Placement vérifié (<c>Location</c>) ; non placée → UNPLACED.</summary>
    public RoomReadBuilder Placement(bool isPlaced)
    {
        _room = _room with { IsPlaced = isPlaced };
        if (!isPlaced)
        {
            AddRoomAnomaly(QualityFlagCodes.Unplaced, AnomalySeverity.Warning,
                "Pièce non placée (Location absente).",
                new Dictionary<string, object?> { ["locationIsNull"] = true });
        }

        return this;
    }

    public RoomReadBuilder Phase(ElementRef? phase)
    {
        _room = _room with { Phase = phase };
        return this;
    }

    public RoomReadBuilder DesignOption(ElementRef? designOption)
    {
        _room = _room with { DesignOption = designOption };
        return this;
    }

    /// <summary>Complète les champs relevant d'autres cartes (quantités EGR-09, finitions et paramètres partagés EGR-10).</summary>
    public RoomReadBuilder With(Func<RoomDto, RoomDto> update)
    {
        _room = update(_room);
        return this;
    }

    public RoomReadResult Build() =>
        new(_room with { QualityFlags = _flags.ToList() }, _anomalies.ToList());

    private void AddRoomAnomaly(string code, AnomalySeverity severity, string message, IReadOnlyDictionary<string, object?> details)
    {
        if (!_flags.Contains(code))
        {
            _flags.Add(code);
        }

        _anomalies.Add(new AnomalyDto
        {
            Code = code,
            Severity = severity,
            Scope = AnomalyScope.Room,
            ElementId = _elementId,
            Message = message,
            Details = details,
        });
    }
}
