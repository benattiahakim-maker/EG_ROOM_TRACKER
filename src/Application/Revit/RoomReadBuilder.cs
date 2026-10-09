using System.Globalization;
using EgRoomTracker.Contracts.Codes;
using EgRoomTracker.Contracts.Configuration;
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
        if (!isPlaced)
        {
            AddRoomAnomaly(QualityFlagCodes.Unplaced, AnomalySeverity.Warning,
                "Pièce non placée (Location absente).",
                new Dictionary<string, object?> { ["locationIsNull"] = true });
        }

        return this;
    }

    /// <summary>Quantités converties et état du volume (EGR-09).</summary>
    public RoomReadBuilder Quantities(RoomQuantityValues quantities)
    {
        _room = _room with
        {
            AreaM2 = quantities.AreaM2,
            UnboundedHeightM = quantities.UnboundedHeightM,
            VolumeM3 = quantities.VolumeM3,
            VolumeStatus = quantities.VolumeStatus,
        };
        return this;
    }

    public RoomReadBuilder Phase(ElementRef? phase)
    {
        _room = _room with { Phase = phase };
        return this;
    }

    /// <param name="designOptionSetId">Jeu de variantes de la variante (donnée interne non sérialisée) ; null si inconnu.</param>
    public RoomReadBuilder DesignOption(ElementRef? designOption, string? designOptionSetId = null)
    {
        _room = _room with { DesignOption = designOption, DesignOptionSetId = designOption is null ? null : designOptionSetId };
        return this;
    }

    /// <summary>Finitions textuelles (EGR-10) : valeurs issues de <see cref="Text"/> (absent → MISSING_PARAMETER).</summary>
    public RoomReadBuilder Finishes(string? floor, string? wall, string? ceiling, string? @base)
    {
        _room = _room with { Finishes = new RoomFinishes(floor, wall, ceiling, @base) };
        return this;
    }

    /// <summary>
    /// Paramètres partagés (EGR-10) : une entrée par clé de mapping, dans l'ordre de la configuration, avec le GUID
    /// en provenance. Absent de la pièce → null et MISSING_PARAMETER (détails : clé et GUID) ; GUID inconnu du
    /// document → null sans indicateur (anomalie document SHARED_PARAMETER_NOT_FOUND posée une seule fois).
    /// </summary>
    public RoomReadBuilder SharedParameters(IEnumerable<(SharedParameterMapping Mapping, SharedParameterRead Read)> values)
    {
        var result = new Dictionary<string, SharedParameterValue>();
        foreach (var (mapping, read) in values)
        {
            if (read.State == SharedParameterState.Absent)
            {
                AddRoomAnomaly(QualityFlagCodes.MissingParameter, AnomalySeverity.Warning,
                    $"Paramètre partagé {mapping.Key} absent de la pièce.",
                    new Dictionary<string, object?>
                    {
                        ["parameter"] = mapping.Key,
                        ["guid"] = mapping.Guid.ToString("D"),
                    });
            }

            result[mapping.Key] = new SharedParameterValue(mapping.Guid, read.State == SharedParameterState.HasValue ? read.Value : null);
        }

        _room = _room with { SharedParameters = result };
        return this;
    }

    /// <summary>Complète des champs du DTO non couverts par les méthodes dédiées.</summary>
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
