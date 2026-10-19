using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using EgRoomTracker.Application.Configuration;
using EgRoomTracker.Application.Quality;
using EgRoomTracker.Application.Revit;
using EgRoomTracker.Application.Trc;
using EgRoomTracker.Contracts.Codes;
using EgRoomTracker.Contracts.Configuration;
using EgRoomTracker.Contracts.Results;
using EgRoomTracker.Export;
using EgRoomTracker.Logging;

namespace EgRoomTracker.Application;

/// <summary>
/// Orchestration des étapes et production du bilan (spécifications, section 4).
/// Exécution séquentielle sur le thread appelant : aucun Task.Run ni accès parallèle aux objets Revit.
/// </summary>
public sealed class RoomExtractionService
{
    private readonly IInputConfigurationService _configuration;
    private readonly IRevitDocumentService _document;
    private readonly IVolumeCalculationService _volume;
    private readonly IRoomCollectionService _collection;
    private readonly IRoomParameterReader _reader;
    private readonly IQualityCheckService _quality;
    private readonly IExportService _export;
    private readonly ILogService _log;
    private readonly TimeProvider _time;
    private readonly IReadOnlyList<IRoomElementService> _elementServices;
    private readonly IRoomImageService? _imageService;
    private readonly IRoomHeightService? _heightService;
    private readonly ITrcExportService? _trcExport;

    public RoomExtractionService(
        IInputConfigurationService configuration,
        IRevitDocumentService document,
        IVolumeCalculationService volume,
        IRoomCollectionService collection,
        IRoomParameterReader reader,
        IQualityCheckService quality,
        IExportService export,
        ILogService log,
        TimeProvider? time = null,
        IReadOnlyList<IRoomElementService>? elementServices = null,
        IRoomImageService? imageService = null,
        IRoomHeightService? heightService = null,
        ITrcExportService? trcExport = null)
    {
        _configuration = configuration;
        _document = document;
        _volume = volume;
        _collection = collection;
        _reader = reader;
        _quality = quality;
        _export = export;
        _log = log;
        _time = time ?? TimeProvider.System;
        _elementServices = elementServices ?? [];
        _imageService = imageService;
        _heightService = heightService;
        _trcExport = trcExport;
    }

    /// <summary>Version de l'extracteur, issue de l'assembly (sans métadonnées de build).</summary>
    public static string ExtractorVersion { get; } = ReadExtractorVersion();

    /// <summary>
    /// Exécute l'extraction et écrit <c>result.json</c>. Ne lève pas d'exception : un échec global
    /// est porté par le statut <c>failed</c> du résultat retourné.
    /// </summary>
    public ExtractionResult Run(ExtractionRequest request)
    {
        var startedAt = _time.GetUtcNow();
        var stopwatch = Stopwatch.StartNew();
        var stage = Stages.Startup;
        ExtractionSettings? settings = null;
        DocumentDescriptor? document = null;
        var roomsCollected = 0;
        ImagesSummary? images = null;
        HeightsSummary? heights = null;
        VolumeComputationState? volumeState = null;
        ExtractionResult result;

        try
        {
            try
            {
                _log.Info(LogEventCodes.ExtractionStarted, stage, $"Extraction démarrée (Revit {request.RevitVersion}, extracteur {ExtractorVersion}).");

                stage = Stages.Configuration;
                settings = _configuration.Load(request.InputDirectory, request.SettingsPath);
                _log.Info(LogEventCodes.ConfigurationValidated, stage, "Configuration validée.");

                stage = Stages.Document;
                document = _document.Open(settings, request.InputDirectory);
                _log.Log(new LogEvent(LogLevel.Info, LogEventCodes.DocumentAccessible, stage, "Document accessible.")
                {
                    DocumentPath = document.Source.RelativePath,
                });

                volumeState = _volume.Prepare(settings.VolumeMode);
                LogVolumeComputation(volumeState, stage);

                stage = Stages.Collection;
                var collected = _collection.CollectRooms(settings.PhaseElementIds);
                roomsCollected = collected.Count;
                var rooms = new List<RoomDto>(collected.Count);
                var anomalies = new List<AnomalyDto>(document.Anomalies);
                anomalies.AddRange(_reader.CheckSharedParameters(settings.SharedParameters));
                var roomErrors = 0;
                foreach (var room in collected.OrderBy(r => r.ElementId))
                {
                    var read = ReadRoom(room, settings.SharedParameters, volumeState, document.Source.RelativePath);
                    if (read is null)
                    {
                        roomErrors++;
                        read = ExtractionErrorRoom(room, settings.SharedParameters);
                    }
                    else
                    {
                        if (settings.RoomParameters == RoomParametersMode.All)
                        {
                            read = AttachAllParameters(read, room, document.Source.RelativePath);
                        }

                        if (settings.OutputFormat != OutputFormat.Contract)
                        {
                            read = AttachNamedParameters(read, room);
                        }
                    }

                    rooms.Add(read.Room);
                    anomalies.AddRange(read.Anomalies);
                }

                AttachElements(settings, rooms, anomalies);
                heights = MeasureHeights(settings, rooms, anomalies);
                images = ExportRoomImages(settings, request, document, rooms, anomalies);

                _log.Log(new LogEvent(LogLevel.Info, LogEventCodes.CollectionCompleted, stage, $"{rooms.Count} pièce(s) collectée(s).")
                {
                    Data = new Dictionary<string, object?> { ["roomsCollected"] = roomsCollected },
                });

                stage = Stages.Quality;
                var report = _quality.Evaluate(rooms, anomalies, volumeState);
                var sortedRooms = SortByElementId(report.Rooms);
                if (report.Anomalies.Count > 0)
                {
                    LogAnomalySummary(report.Anomalies);
                }

                LogDuplicatesWithUnknownOptionSet(report.Anomalies, stage);

                // Une pièce en erreur n'est jamais présentée comme un succès sans réserve (section 6).
                var status = roomErrors > 0 && report.Status == ExtractionStatus.Succeeded
                    ? ExtractionStatus.SucceededWithWarnings
                    : report.Status;
                result = BuildResult(request, settings, document, volumeState, startedAt, status, sortedRooms, report.Anomalies, roomsCollected, [], images, heights);
            }
            catch (ExtractionFailedException ex)
            {
                LogFailure(ex.Error, ex);
                result = BuildResult(request, settings, document, volumeState, startedAt, ExtractionStatus.Failed, [], [], roomsCollected, [ex.Error]);
            }
            catch (Exception ex)
            {
                var error = new GlobalError(ErrorCodes.UnexpectedError, stage, "Erreur inattendue hors du périmètre d'une pièce.");
                LogFailure(error, ex);
                result = BuildResult(request, settings, document, volumeState, startedAt, ExtractionStatus.Failed, [], [], roomsCollected, [error]);
            }
            finally
            {
                // Fermeture du document par son propriétaire (D1), en succès comme en échec, avant l'export.
                CloseDocument();
            }

            result = Export(request, result, settings);
            LogSummary(result, stopwatch.Elapsed);
        }
        finally
        {
            _log.Flush();
        }

        return result;
    }

    /// <summary>
    /// Écrit la sortie selon <c>outputFormat</c> : <c>result.json</c> (défaut), <c>TRCValidationDict.json</c> (données seules, sans
    /// statut) ou les deux. Une extraction en échec écrit toujours <c>result.json</c> : sans pièces, il n'y a pas de données TRC, et le
    /// fichier explique l'échec.
    /// </summary>
    private ExtractionResult Export(ExtractionRequest request, ExtractionResult result, ExtractionSettings? settings)
    {
        var format = settings?.OutputFormat ?? OutputFormat.Contract;
        var runFailed = result.Summary.Status == ExtractionStatus.Failed;
        try
        {
            if (format != OutputFormat.Trc || runFailed)
            {
                _export.Write(result, request.OutputPath);
                _log.Info(LogEventCodes.ExportCompleted, Stages.Export, "result.json écrit.");
            }

            if (format != OutputFormat.Contract && !runFailed)
            {
                var trc = _trcExport ?? throw new InvalidOperationException("Service d'export TRC non disponible.");
                var file = trc.Write(result, Path.GetDirectoryName(Path.GetFullPath(request.OutputPath))!);
                _log.Info(LogEventCodes.ExportCompleted, Stages.Export, $"{file} écrit (format TRC).");
            }

            return result;
        }
        catch (Exception ex)
        {
            var error = new GlobalError(ErrorCodes.ExportFailed, Stages.Export, "Écriture ou finalisation de result.json impossible.");
            LogFailure(error, ex);
            var failed = result with
            {
                Rooms = [],
                Anomalies = [],
                Summary = result.Summary with
                {
                    Status = ExtractionStatus.Failed,
                    Errors = [.. result.Summary.Errors, error],
                    Counts = result.Summary.Counts with { RoomsExported = 0, RoomsWithAnomalies = 0 },
                },
            };
            WriteMinimalFailedResult(request, failed);
            return failed;
        }
    }

    /// <summary>
    /// Repli EGR-13 (décision du chef de projet) : après <c>EXPORT_FAILED</c>, une seule nouvelle tentative d'écrire
    /// un <c>result.json</c> minimal (statut <c>failed</c>, erreurs, pièces et anomalies vides). En cas de nouvel
    /// échec, le diagnostic repose sur le seul log console.
    /// </summary>
    private void WriteMinimalFailedResult(ExtractionRequest request, ExtractionResult failed)
    {
        try
        {
            _export.Write(failed, request.OutputPath);
            _log.Info(LogEventCodes.ExportCompleted, Stages.Export, "result.json minimal écrit après EXPORT_FAILED (statut failed).");
        }
        catch (Exception ex)
        {
            _log.Log(new LogEvent(LogLevel.Error, LogEventCodes.ExtractionFailed, Stages.Export, $"{ErrorCodes.ExportFailed} : result.json minimal impossible à écrire ; diagnostic par le log console uniquement.")
            {
                Exception = ex,
                Data = new Dictionary<string, object?> { ["errorCode"] = ErrorCodes.ExportFailed, ["fallback"] = true },
            });
        }
    }

    /// <summary>Tri par elementId numérique croissant (section 2), quel que soit l'ordre rendu par le contrôle qualité.</summary>
    internal static IReadOnlyList<RoomDto> SortByElementId(IReadOnlyList<RoomDto> rooms) =>
        rooms
            .OrderBy(r => long.TryParse(r.ElementId, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var id) ? id : long.MaxValue)
            .ThenBy(r => r.ElementId, StringComparer.Ordinal)
            .ToList();

    private void CloseDocument()
    {
        try
        {
            _document.Close();
        }
        catch (Exception ex)
        {
            // Sans effet sur le résultat : le RVT source n'est jamais sauvegardé.
            _log.Log(new LogEvent(LogLevel.Warning, LogEventCodes.DocumentCloseFailed, Stages.Document, "Fermeture du document impossible.")
            {
                Exception = ex,
            });
        }
    }

    /// <summary>Lit une pièce ; retourne null si une exception limitée à la pièce survient (filet de sécurité).</summary>
    private RoomReadResult? ReadRoom(CollectedRoom room, IReadOnlyList<SharedParameterMapping> sharedParameters, VolumeComputationState volumeState, string documentPath)
    {
        try
        {
            return _reader.Read(room, sharedParameters, volumeState);
        }
        catch (Exception ex)
        {
            _log.Log(new LogEvent(LogLevel.Warning, LogEventCodes.RoomExtractionFailed, Stages.Collection, "Lecture de la pièce impossible ; identité exportée, indicateur EXTRACTION_ERROR.")
            {
                DocumentPath = documentPath,
                RoomElementId = room.ElementId.ToString(CultureInfo.InvariantCulture),
                Exception = ex,
            });
            return null;
        }
    }

    /// <summary>Joint les paramètres d'identification TRC lus par leur nom ; un échec n'altère pas les champs du contrat.</summary>
    private RoomReadResult AttachNamedParameters(RoomReadResult read, CollectedRoom room)
    {
        try
        {
            return _reader.ReadNamedParameters(room.ElementId, TrcIdentification.ParameterNames) is { } named
                ? read with { Room = read.Room with { NamedParameters = named } }
                : read;
        }
        catch (Exception)
        {
            return read;
        }
    }

    /// <summary>Joint tous les paramètres de la pièce (<c>roomParameters: all</c>) ; un échec n'altère pas les champs du contrat.</summary>
    private RoomReadResult AttachAllParameters(RoomReadResult read, CollectedRoom room, string documentPath)
    {
        try
        {
            return _reader.ReadAllParameters(room.ElementId) is { } parameters
                ? read with { Room = read.Room with { Parameters = parameters } }
                : read;
        }
        catch (Exception ex)
        {
            _log.Log(new LogEvent(LogLevel.Warning, LogEventCodes.RoomParametersFailed, Stages.Collection, "Lecture de tous les paramètres de la pièce impossible ; champs du contrat conservés.")
            {
                DocumentPath = documentPath,
                RoomElementId = room.ElementId.ToString(CultureInfo.InvariantCulture),
                Exception = ex,
            });
            return read;
        }
    }

    /// <summary>
    /// Rattache aux pièces les portes, équipements et terminaux demandés par <c>settings.json</c> (schéma 1.4). Chaque
    /// famille demandée donne un tableau (éventuellement vide) sur toutes les pièces exportées. Une collecte en échec
    /// produit ELEMENTS_COLLECTION_FAILED (warning) sans faire échouer l'extraction ; les éléments sans pièce exportée
    /// ou illisibles sont comptés dans ELEMENT_WITHOUT_ROOM (info).
    /// </summary>
    private void AttachElements(ExtractionSettings settings, List<RoomDto> rooms, List<AnomalyDto> anomalies)
    {
        if (settings.Elements.Count == 0)
        {
            return;
        }

        var indexById = new Dictionary<long, int>();
        for (var i = 0; i < rooms.Count; i++)
        {
            if (long.TryParse(rooms[i].ElementId, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var id))
            {
                indexById[id] = i;
            }
        }

        var includeAll = settings.RoomParameters == RoomParametersMode.All;
        foreach (var kind in settings.Elements)
        {
            var name = ElementKinds.ToName(kind);
            var service = _elementServices.FirstOrDefault(s => s.Kind == kind);
            if (service is null)
            {
                anomalies.Add(ElementsFailure(name, "service non disponible"));
                continue;
            }

            ElementCollection collection;
            try
            {
                collection = service.Collect(includeAll, settings.ElementCategories.GetValueOrDefault(kind), settings.LinkedElements);
            }
            catch (Exception ex)
            {
                _log.Log(new LogEvent(LogLevel.Warning, LogEventCodes.ElementsCollectionFailed, Stages.Collection, $"Collecte « {name} » impossible ; l'extraction des pièces continue.")
                {
                    Exception = ex,
                });
                anomalies.Add(ElementsFailure(name, ex.GetType().Name));
                continue;
            }

            var perRoom = new Dictionary<int, List<RoomElementDto>>();
            foreach (var index in Enumerable.Range(0, rooms.Count))
            {
                perRoom[index] = [];
            }

            var withoutRoom = new List<string>();
            foreach (var placement in collection.Placements)
            {
                var attached = false;
                foreach (var link in placement.Links)
                {
                    if (indexById.TryGetValue(link.RoomId, out var index))
                    {
                        perRoom[index].Add(placement.Element with { Relation = link.Relation });
                        attached = true;
                    }
                }

                if (!attached)
                {
                    withoutRoom.Add(placement.Element.ElementId);
                }
            }

            foreach (var (index, elements) in perRoom)
            {
                rooms[index] = WithElements(rooms[index], kind, elements);
            }

            _log.Log(new LogEvent(LogLevel.Info, LogEventCodes.ElementsCollected, Stages.Collection, $"{collection.Placements.Count} élément(s) « {name} » collecté(s), {withoutRoom.Count} sans pièce.")
            {
                Data = new Dictionary<string, object?>
                {
                    ["kind"] = name,
                    ["collected"] = collection.Placements.Count,
                    ["withoutRoom"] = withoutRoom.Count,
                    ["unreadable"] = collection.Unreadable,
                    ["categories"] = collection.Categories?.ToArray(),
                    ["sources"] = collection.Sources?.ToArray(),
                },
            });

            if (collection.UnknownCategories is { Count: > 0 } unknown)
            {
                anomalies.Add(new AnomalyDto
                {
                    Code = DocumentAnomalyCodes.ElementsCollectionFailed,
                    Severity = AnomalySeverity.Warning,
                    Scope = AnomalyScope.Document,
                    ElementId = null,
                    Message = $"Catégories Revit inconnues ignorées pour « {name} » : {string.Join(", ", unknown)}.",
                    Details = new Dictionary<string, object?> { ["kind"] = name, ["reason"] = "unknownCategories", ["unknownCategories"] = unknown.ToArray() },
                });
            }

            if (withoutRoom.Count > 0 || collection.Unreadable > 0)
            {
                anomalies.Add(new AnomalyDto
                {
                    Code = DocumentAnomalyCodes.ElementWithoutRoom,
                    Severity = AnomalySeverity.Info,
                    Scope = AnomalyScope.Document,
                    ElementId = null,
                    Message = $"{withoutRoom.Count} élément(s) « {name} » sans pièce exportée, {collection.Unreadable} illisible(s).",
                    Details = new Dictionary<string, object?>
                    {
                        ["kind"] = name,
                        ["count"] = withoutRoom.Count,
                        ["elementIds"] = withoutRoom.Take(MaxListedElements).ToArray(),
                        ["unreadable"] = collection.Unreadable,
                    },
                });
            }
        }
    }

    /// <summary>
    /// Hauteur de chaque pièce placée par lancer de rayons (<c>roomHeights</c>), document ouvert : hauteur renseignée et
    /// mesurée séparées, hauteur effective = mesurée si un obstacle existe, sinon renseignée (règle de TRC). Une pièce en
    /// échec garde tous les champs du contrat ; un échec produit HEIGHT_MEASUREMENT_FAILED (warning).
    /// </summary>
    private HeightsSummary? MeasureHeights(ExtractionSettings settings, List<RoomDto> rooms, List<AnomalyDto> anomalies)
    {
        if (!settings.RoomHeights)
        {
            return null;
        }

        var requested = 0;
        var measured = 0;
        var withoutObstacle = 0;
        var failed = 0;
        string? firstError = null;
        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; i < rooms.Count; i++)
        {
            var room = rooms[i];
            if (room.QualityFlags.Contains(QualityFlagCodes.Unplaced)
                || !long.TryParse(room.ElementId, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var id))
            {
                continue;
            }

            requested++;
            if (_heightService is null)
            {
                failed++;
                firstError ??= "service non disponible";
                continue;
            }

            try
            {
                var m = _heightService.Measure(id, settings.HeightStepM);

                // Une hauteur absente est null, jamais 0 : une valeur nulle ou négative n'est pas une hauteur.
                var declared = Positive(m.DeclaredM);
                var measuredMin = Positive(m.MeasuredMinM);
                rooms[i] = room with
                {
                    Heights = new RoomHeightsDto(
                        declared,
                        measuredMin,
                        measuredMin ?? declared,
                        measuredMin is not null ? HeightSources.Measured : declared is not null ? HeightSources.Declared : null,
                        m.Samples)
                    {
                        LimitingCategory = measuredMin is null ? null : m.LimitingCategory,
                        LimitingDocument = measuredMin is null ? null : m.LimitingDocument,
                        ByCategoryM = measuredMin is null ? null : m.ByCategoryM,
                    },
                };
                if (measuredMin is null)
                {
                    withoutObstacle++;
                }
                else
                {
                    measured++;
                }
            }
            catch (Exception ex)
            {
                failed++;
                firstError ??= ex.GetType().Name;
            }
        }

        _log.Log(new LogEvent(failed > 0 ? LogLevel.Warning : LogLevel.Info, failed > 0 ? LogEventCodes.RoomHeightsFailed : LogEventCodes.RoomHeightsMeasured, Stages.Collection,
            $"{measured} hauteur(s) mesurée(s), {withoutObstacle} sans obstacle, {failed} en échec sur {requested} pièce(s).")
        {
            Duration = stopwatch.Elapsed,
            Data = new Dictionary<string, object?>
            {
                ["requested"] = requested,
                ["measured"] = measured,
                ["withoutObstacle"] = withoutObstacle,
                ["failed"] = failed,
                ["stepM"] = settings.HeightStepM,
            },
        });
        if (failed > 0)
        {
            anomalies.Add(new AnomalyDto
            {
                Code = DocumentAnomalyCodes.HeightMeasurementFailed,
                Severity = AnomalySeverity.Warning,
                Scope = AnomalyScope.Document,
                ElementId = null,
                Message = $"Hauteur non mesurée pour {failed} pièce(s) sur {requested}.",
                Details = new Dictionary<string, object?> { ["requested"] = requested, ["failed"] = failed, ["reason"] = firstError },
            });
        }

        return new HeightsSummary(requested, measured, withoutObstacle, failed);
    }

    /// <summary>Valeur strictement positive et finie ; sinon null (une hauteur nulle n'existe pas).</summary>
    private static double? Positive(double? value) => value is > 0 && double.IsFinite(value.Value) ? value : null;

    /// <summary>
    /// Vues 3D des pièces placées (<c>roomImages</c>) dans <c>Images/</c>, document ouvert. Un échec, total ou partiel,
    /// produit IMAGE_EXPORT_FAILED (warning) sans faire échouer l'extraction des pièces.
    /// </summary>
    private ImagesSummary? ExportRoomImages(ExtractionSettings settings, ExtractionRequest request, DocumentDescriptor document, IReadOnlyList<RoomDto> rooms, List<AnomalyDto> anomalies)
    {
        if (!settings.RoomImages)
        {
            return null;
        }

        var targets = rooms
            .Where(r => !r.QualityFlags.Contains(QualityFlagCodes.Unplaced))
            .Select(r => long.TryParse(r.ElementId, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var id) ? new RoomImageRequest(id, r.UniqueId) : null)
            .OfType<RoomImageRequest>()
            .ToList();
        if (_imageService is null || request.ImagesDirectory is null)
        {
            anomalies.Add(ImageFailure(targets.Count, 0, "service ou dossier d'images non disponible"));
            return new ImagesSummary(targets.Count, 0, targets.Count);
        }

        try
        {
            var result = _imageService.Export(targets, request.ImagesDirectory, TrcNaming.CodeFile(document.Source.FileName), settings.ImageWidthPx);
            var failed = targets.Count - result.Exported;
            _log.Log(new LogEvent(failed > 0 ? LogLevel.Warning : LogLevel.Info, failed > 0 ? LogEventCodes.RoomImagesFailed : LogEventCodes.RoomImagesExported, Stages.Collection,
                $"{result.Exported} image(s) de pièce exportée(s) sur {targets.Count}.")
            {
                Data = new Dictionary<string, object?> { ["requested"] = targets.Count, ["exported"] = result.Exported, ["failed"] = failed, ["widthPx"] = settings.ImageWidthPx },
            });
            if (failed > 0)
            {
                anomalies.Add(ImageFailure(targets.Count, result.Exported, result.Error ?? "export impossible pour certaines pièces"));
            }

            return new ImagesSummary(targets.Count, result.Exported, failed);
        }
        catch (Exception ex)
        {
            _log.Log(new LogEvent(LogLevel.Warning, LogEventCodes.RoomImagesFailed, Stages.Collection, "Export des vues 3D impossible ; l'extraction des pièces continue.")
            {
                Exception = ex,
            });
            anomalies.Add(ImageFailure(targets.Count, 0, ex.GetType().Name));
            return new ImagesSummary(targets.Count, 0, targets.Count);
        }
    }

    private static AnomalyDto ImageFailure(int requested, int exported, string reason) => new()
    {
        Code = DocumentAnomalyCodes.ImageExportFailed,
        Severity = AnomalySeverity.Warning,
        Scope = AnomalyScope.Document,
        ElementId = null,
        Message = $"{requested - exported} image(s) de pièce non exportée(s) sur {requested}.",
        Details = new Dictionary<string, object?> { ["requested"] = requested, ["exported"] = exported, ["failed"] = requested - exported, ["reason"] = reason },
    };

    /// <summary>Nombre maximal d'identifiants cités dans le détail de ELEMENT_WITHOUT_ROOM.</summary>
    private const int MaxListedElements = 50;

    private static AnomalyDto ElementsFailure(string kind, string reason) => new()
    {
        Code = DocumentAnomalyCodes.ElementsCollectionFailed,
        Severity = AnomalySeverity.Warning,
        Scope = AnomalyScope.Document,
        ElementId = null,
        Message = $"Collecte « {kind} » impossible ; tableau absent des pièces.",
        Details = new Dictionary<string, object?> { ["kind"] = kind, ["reason"] = reason },
    };

    private static RoomDto WithElements(RoomDto room, ElementKind kind, IReadOnlyList<RoomElementDto> elements) => kind switch
    {
        ElementKind.Doors => room with { Doors = elements },
        ElementKind.Equipment => room with { Equipment = elements },
        _ => room with { Terminals = elements },
    };

    /// <summary>Pièce réduite à son identité, champs illisibles à null (section 6).</summary>
    /// <summary>Pièce en erreur : identité seule ; une entrée null par clé de mapping partagé (forme stable, EGR-10).</summary>
    private static RoomReadResult ExtractionErrorRoom(CollectedRoom room, IReadOnlyList<SharedParameterMapping> sharedParameters)
    {
        var elementId = room.ElementId.ToString(CultureInfo.InvariantCulture);
        var dto = new RoomDto
        {
            ElementId = elementId,
            UniqueId = room.UniqueId,
            VolumeStatus = VolumeStatus.NotAvailable,
            SharedParameters = sharedParameters.ToDictionary(m => m.Key, m => new SharedParameterValue(m.Guid, null)),
            QualityFlags = [QualityFlagCodes.ExtractionError],
        };
        var anomaly = new AnomalyDto
        {
            Code = QualityFlagCodes.ExtractionError,
            Severity = AnomalySeverity.Error,
            Scope = AnomalyScope.Room,
            ElementId = elementId,
            Message = "Erreur de lecture limitée à la pièce ; identité exportée, champs illisibles à null.",
        };
        return new RoomReadResult(dto, [anomaly]);
    }

    private void LogFailure(GlobalError error, Exception exception) =>
        _log.Log(new LogEvent(LogLevel.Error, LogEventCodes.ExtractionFailed, error.Stage, $"{error.Code} : {error.Message}")
        {
            Exception = exception,
            Data = new Dictionary<string, object?> { ["errorCode"] = error.Code },
        });

    private ExtractionResult BuildResult(
        ExtractionRequest request,
        ExtractionSettings? settings,
        DocumentDescriptor? document,
        VolumeComputationState? volumeState,
        DateTimeOffset startedAt,
        ExtractionStatus status,
        IReadOnlyList<RoomDto> rooms,
        IReadOnlyList<AnomalyDto> anomalies,
        int roomsCollected,
        IReadOnlyList<GlobalError> errors,
        ImagesSummary? images = null,
        HeightsSummary? heights = null)
    {
        var options = new AppliedOptions(
            settings?.PhaseIds,
            settings?.VolumeMode ?? VolumeMode.Preserve,
            settings?.SharedParameters.Select(p => p.Key).ToList() ?? [],
            settings?.RoomParameters == RoomParametersMode.All ? "all" : null,
            settings is { Elements.Count: > 0 } ? settings.Elements.Select(ElementKinds.ToName).ToList() : null,
            settings is { RoomImages: true } ? true : null,
            settings is { RoomImages: true } ? settings.ImageWidthPx : null,
            settings is { RoomHeights: true } ? true : null,
            settings is { RoomHeights: true } ? settings.HeightStepM : null,
            settings is { LinkedElements: false } ? false : null,
            settings is { OutputFormat: not OutputFormat.Contract } ? (settings.OutputFormat == OutputFormat.Trc ? "trc" : "both") : null);

        return new ExtractionResult
        {
            Summary = new ExtractionSummary
            {
                CorrelationId = request.CorrelationId,
                ExtractorVersion = ExtractorVersion,
                Revit = new RevitInfo(request.RevitVersion, request.RevitBuild),
                ApsEngine = request.ApsEngine,
                Source = document?.Source,
                StartedAtUtc = startedAt,
                CompletedAtUtc = _time.GetUtcNow(),
                Options = options,
                VolumeComputation = volumeState is null
                    ? null
                    : new VolumeComputationSummary(volumeState.DocumentComputeVolumes, volumeState.ComputeApplied, volumeState.TransactionRolledBack),
                Counts = new RoomCounts(roomsCollected, rooms.Count, rooms.Count(r => r.QualityFlags.Count > 0)),
                Images = images,
                Heights = heights,
                Status = status,
                Errors = errors,
            },
            Rooms = rooms,
            Anomalies = anomalies,
        };
    }

    /// <summary>
    /// Traçabilité du calcul des volumes (EGR-16) : VOLUME_COMPUTATION (info ; warning si l'annulation de la transaction
    /// temporaire n'est pas confirmée), et VOLUME_COMPUTE_FAILED (warning) si l'activation temporaire a échoué.
    /// </summary>
    private void LogVolumeComputation(VolumeComputationState state, string stage)
    {
        var mode = state.Mode.ToString().ToLowerInvariant();
        var level = state.TransactionRolledBack == false ? LogLevel.Warning : LogLevel.Info;
        var message = state.ComputeApplied
            ? "Calcul des volumes activé temporairement (transaction annulée après lecture, RVT non sauvegardé)."
            : $"Calcul des volumes : mode {mode}, réglage du document conservé.";
        _log.Log(new LogEvent(level, LogEventCodes.VolumeComputation, stage, message)
        {
            Data = new Dictionary<string, object?>
            {
                ["volumeMode"] = mode,
                ["documentComputeVolumes"] = state.DocumentComputeVolumes,
                ["computeApplied"] = state.ComputeApplied,
                ["transactionRolledBack"] = state.TransactionRolledBack,
                ["volumesComputed"] = state.VolumesComputed,
                ["roomsRead"] = state.ComputedVolumes?.Count,
            },
        });

        if (state.ComputeFailure is not null)
        {
            _log.Warning(LogEventCodes.VolumeComputeFailed, stage, state.ComputeFailure);
        }
    }

    /// <summary>Compteur agrégé par code : pas un message par pièce (section 5).</summary>
    private void LogAnomalySummary(IReadOnlyList<AnomalyDto> anomalies)
    {
        var byCode = anomalies
            .GroupBy(a => a.Code, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (object?)g.Count(), StringComparer.Ordinal);
        var level = anomalies.Any(a => a.Severity != AnomalySeverity.Info) ? LogLevel.Warning : LogLevel.Info;
        _log.Log(new LogEvent(level, LogEventCodes.AnomaliesDetected, Stages.Quality, $"{anomalies.Count} anomalie(s) ; détail dans result.json.")
        {
            Data = byCode,
        });
    }

    private void LogSummary(ExtractionResult result, TimeSpan duration)
    {
        var summary = result.Summary;
        var level = summary.Status switch
        {
            ExtractionStatus.Failed => LogLevel.Error,
            ExtractionStatus.SucceededWithWarnings => LogLevel.Warning,
            _ => LogLevel.Info,
        };
        _log.Log(new LogEvent(level, LogEventCodes.ExtractionSummary, Stages.Completion, $"Extraction terminée : {summary.Status}.")
        {
            DocumentPath = summary.Source?.RelativePath,
            Duration = duration,
            Data = new Dictionary<string, object?>
            {
                ["status"] = ToCamelCase(summary.Status.ToString()),
                ["roomsCollected"] = summary.Counts.RoomsCollected,
                ["roomsExported"] = summary.Counts.RoomsExported,
                ["roomsWithAnomalies"] = summary.Counts.RoomsWithAnomalies,
                ["errors"] = summary.Errors.Select(e => e.Code).ToArray(),
            },
        });
    }

    /// <summary>Statut au format de <c>result.json</c> (<c>succeededWithWarnings</c>).</summary>
    internal static string ToCamelCase(string value) =>
        string.IsNullOrEmpty(value) ? value : char.ToLowerInvariant(value[0]) + value[1..];

    /// <summary>Une ligne Warning si des doublons reposent sur un jeu d'options illisible (détail <c>optionSetUnknown</c>).</summary>
    private void LogDuplicatesWithUnknownOptionSet(IReadOnlyList<AnomalyDto> anomalies, string stage)
    {
        var rooms = anomalies
            .Where(a => a.Code == QualityFlagCodes.DuplicateNumber
                && a.Details.TryGetValue(QualityCheckService.OptionSetUnknownDetail, out var unknown) && unknown is true)
            .Select(a => a.ElementId)
            .ToList();
        if (rooms.Count == 0)
        {
            return;
        }

        _log.Log(new LogEvent(LogLevel.Warning, LogEventCodes.DuplicateOptionSetUnknown, stage,
            $"{rooms.Count} pièce(s) signalée(s) DUPLICATE_NUMBER faute de jeu d'options lisible (variantes différentes comparées par prudence).")
        {
            Data = new Dictionary<string, object?> { ["roomsAffected"] = rooms.Count, ["elementIds"] = rooms },
        });
    }

    private static string ReadExtractorVersion()
    {
        var assembly = typeof(RoomExtractionService).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "0.0.0";
        var plus = version.IndexOf('+');
        return plus >= 0 ? version[..plus] : version;
    }
}
