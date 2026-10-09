using System.Globalization;
using System.Text;
using EgRoomTracker.Aps;
using EgRoomTracker.Aps.DesignAutomation;
using EgRoomTracker.Aps.Oss;
using EgRoomTracker.Aps.Packaging;

namespace EgRoomTracker.WorkItemRunner.Services;

/// <summary>Demande de lancement saisie dans l'interface.</summary>
public sealed record RunRequest(string RevitVersion, string Alias, string Nickname, string ZipPath, string SettingsJson);

/// <summary>Lancement préparé (contrôles locaux et lectures seules faits) : plan à confirmer avant toute écriture.</summary>
public sealed record PreparedRun(
    RunRequest Request,
    string RunId,
    string ActivityId,
    string BucketKey,
    IReadOnlyList<ActivityParameter> Parameters,
    CheckResult ZipCheck,
    CheckResult SettingsCheck,
    OperationPlan Plan);

/// <summary>
/// WorkItem soumis : les clés de finalisation des sorties OSS restent en mémoire (jamais écrites sur disque ni
/// journalisées) jusqu'à la récupération des sorties.
/// </summary>
public sealed class ActiveRun(RunRecord record, IReadOnlyDictionary<string, (string ObjectKey, string UploadKey, string LocalName)> outputs)
{
    public RunRecord Record { get; internal set; } = record;

    internal IReadOnlyDictionary<string, (string ObjectKey, string UploadKey, string LocalName)> Outputs { get; } = outputs;
}

/// <summary>
/// Parcours d'un WorkItem EG_ROOM_TRACKER (EGR-20) : préparation (lecture seule), puis après confirmation
/// téléversement OSS par URL signées, soumission, suivi, rapport et récupération des sorties. Les messages de
/// progression ne citent que des clés d'objets, jamais une URL signée.
/// </summary>
public sealed class WorkItemRunnerService(
    DesignAutomationClient designAutomation,
    OssClient oss,
    DefinitionLoader definitions,
    ResultReader results,
    RunHistory history,
    TimeProvider? timeProvider = null)
{
    public const string ResultLocalName = "result.json";
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public RunHistory History => history;

    public ResultReader Results => results;

    /// <summary>Activity qualifiée : <c>nickname.ExtractRooms_R&lt;année&gt;+alias</c>.</summary>
    public string QualifiedActivityId(string nickname, string revitVersion, string alias) =>
        $"{nickname}.{definitions.IdOf(revitVersion, DaResourceKind.Activity)}+{alias}";

    /// <summary>Contrôles locaux, vérification de l'alias d'Activity (lecture seule) et plan. Aucune écriture.</summary>
    public async Task<PreparedRun> PrepareAsync(RunRequest request, string? bucketKey, TimeSpan pollInterval, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(bucketKey))
        {
            throw new InvalidOperationException("Bucket OSS inconnu : définir APS_BUCKET (ou APS_NICKNAME).");
        }

        var zipCheck = InputChecks.CheckZip(request.ZipPath);
        if (!zipCheck.IsValid)
        {
            throw new InvalidOperationException("ZIP refusé : " + string.Join(" ; ", zipCheck.Errors));
        }

        var settingsCheck = InputChecks.CheckSettings(request.SettingsJson);
        if (!settingsCheck.IsValid)
        {
            throw new InvalidOperationException(string.Join(" ; ", settingsCheck.Errors));
        }

        var activity = definitions.Load(request.RevitVersion, DaResourceKind.Activity, request.Nickname, request.Alias);
        var parameters = ActivityParameter.FromDefinition(activity);
        foreach (var parameter in parameters.Where(p => p.IsInput))
        {
            _ = InputFor(parameter, request); // paramètre d'entrée sans source connue : erreur avant tout appel
        }

        var unqualified = activity["id"]!.GetValue<string>();
        var aliases = await designAutomation.GetAliasesAsync(DaResourceKind.Activity, unqualified, cancellationToken).ConfigureAwait(false);
        if (aliases is null || !aliases.TryGetValue(request.Alias, out var activityVersion))
        {
            throw new InvalidOperationException($"Activity {unqualified}+{request.Alias} introuvable : la publier d'abord (DaManager ou deploy.py).");
        }

        var runId = RunHistory.NewRunId(_time.GetUtcNow());
        var activityId = $"{request.Nickname}.{unqualified}+{request.Alias}";
        var steps = new List<string>
        {
            $"WorkItem sur {activityId} (version v{activityVersion}), Revit {request.RevitVersion}.",
            $"OSS : bucket {bucketKey} (transient, objets supprimés après 24 h ; créé s'il n'existe pas).",
        };
        foreach (var parameter in parameters)
        {
            var objectKey = ObjectKey(runId, parameter);
            if (parameter.IsInput)
            {
                var (name, bytes) = DescribeInput(parameter, request);
                steps.Add($"Entrée {parameter.Name} ({parameter.Verb}{(parameter.Zip ? ", zip" : string.Empty)}) : {name} ({Size(bytes)}) -> {objectKey}, URL signée de lecture.");
            }
            else
            {
                steps.Add($"Sortie {parameter.Name} ({parameter.Verb}) : URL signée d'écriture -> {objectKey}.");
            }
        }

        steps.Add("POST /workitems : exécution facturée en crédits APS.");
        steps.Add($"Suivi GET /workitems/{{id}} toutes les {pollInterval.TotalSeconds.ToString(CultureInfo.InvariantCulture)} s, puis rapport (valable 24 h) et sorties dans l'historique local ({runId}).");
        steps.Add("Les URL signées expirent après 60 min : un WorkItem plus long ne pourrait plus écrire result.json.");
        steps.AddRange(zipCheck.Warnings.Concat(settingsCheck.Warnings).Select(w => "ATTENTION : " + w));
        return new PreparedRun(request, runId, activityId, bucketKey, parameters, zipCheck, settingsCheck, new OperationPlan("Lancer un WorkItem", steps));
    }

    /// <summary>Après confirmation : bucket, téléversements, URL signées, soumission. Retourne le lancement actif.</summary>
    public async Task<ActiveRun> SubmitAsync(PreparedRun prepared, Action<string> progress, CancellationToken cancellationToken = default)
    {
        var request = prepared.Request;
        var record = new RunRecord
        {
            RunId = prepared.RunId,
            CreatedAtUtc = _time.GetUtcNow(),
            RevitVersion = request.RevitVersion,
            Alias = request.Alias,
            ActivityId = prepared.ActivityId,
            ZipFileName = Path.GetFileName(request.ZipPath),
            BucketKey = prepared.BucketKey,
        };
        history.Save(record);
        await File.WriteAllTextAsync(Path.Combine(history.DirectoryOf(record.RunId), "settings.json"), request.SettingsJson, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);

        try
        {
            if (await oss.CreateBucketAsync(prepared.BucketKey, cancellationToken: cancellationToken).ConfigureAwait(false))
            {
                progress($"Bucket {prepared.BucketKey} créé.");
            }

            var inputUrls = new Dictionary<string, string>();
            var outputUrls = new Dictionary<string, string>();
            var outputs = new Dictionary<string, (string, string, string)>();
            foreach (var parameter in prepared.Parameters)
            {
                var objectKey = ObjectKey(record.RunId, parameter);
                if (parameter.IsInput)
                {
                    await using var content = InputFor(parameter, request);
                    await oss.UploadObjectAsync(prepared.BucketKey, objectKey, content, content.Length, cancellationToken: cancellationToken).ConfigureAwait(false);
                    progress($"Téléversé : {objectKey}");
                    inputUrls[parameter.Name] = await oss.GetSignedDownloadUrlAsync(prepared.BucketKey, objectKey, cancellationToken: cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    var upload = await oss.GetSignedUploadAsync(prepared.BucketKey, objectKey, cancellationToken: cancellationToken).ConfigureAwait(false);
                    outputUrls[parameter.Name] = upload.Urls[0];
                    outputs[parameter.Name] = (objectKey, upload.UploadKey, parameter.LocalName ?? parameter.Name);
                }
            }

            var workItem = WorkItemArguments.Build(prepared.ActivityId, prepared.Parameters, inputUrls, outputUrls);
            var status = await designAutomation.SubmitWorkItemAsync(workItem, cancellationToken).ConfigureAwait(false);
            record = record with { WorkItemId = status.Id, WorkItemStatus = status.Status };
            history.Save(record);
            progress($"WorkItem {status.Id} soumis ({status.Status}).");
            return new ActiveRun(record, outputs);
        }
        catch (Exception exception) when (exception is ApsException or IOException or HttpRequestException)
        {
            history.Save(record with { Error = exception.Message });
            throw;
        }
    }

    /// <summary>Suivi jusqu'à un statut terminal ; l'annulation arrête seulement le suivi (le WorkItem continue).</summary>
    public async Task<WorkItemStatus> WaitAsync(ActiveRun run, TimeSpan pollInterval, IProgress<WorkItemStatus>? progress, CancellationToken cancellationToken)
    {
        var id = run.Record.WorkItemId ?? throw new InvalidOperationException("WorkItem non soumis.");
        var status = await designAutomation.WaitForWorkItemAsync(id, pollInterval, progress: progress, timeProvider: _time, cancellationToken: cancellationToken).ConfigureAwait(false);
        run.Record = run.Record with { WorkItemStatus = status.Status };
        history.Save(run.Record);
        return status;
    }

    /// <summary><c>DELETE /workitems/{id}</c> (après confirmation).</summary>
    public Task CancelAsync(ActiveRun run, CancellationToken cancellationToken = default) =>
        designAutomation.CancelWorkItemAsync(run.Record.WorkItemId ?? throw new InvalidOperationException("WorkItem non soumis."), cancellationToken);

    /// <summary>
    /// Rapport et sorties : chaque sortie est finalisée puis téléchargée ; une sortie absente est notée (D6) sans erreur.
    /// <c>result.json</c> est lu et validé contre le schéma.
    /// </summary>
    public async Task<(RunRecord Record, ResultView? Result, string? Report)> CollectAsync(ActiveRun run, WorkItemStatus status, Action<string> progress, CancellationToken cancellationToken = default)
    {
        var directory = history.DirectoryOf(run.Record.RunId);
        string? report = null;
        if (status.ReportUrl is not null)
        {
            try
            {
                report = await designAutomation.DownloadReportAsync(status, cancellationToken).ConfigureAwait(false);
                await File.WriteAllTextAsync(Path.Combine(directory, RunHistory.ReportFileName), report, cancellationToken).ConfigureAwait(false);
                progress("Rapport APS téléchargé.");
            }
            catch (ApsException exception)
            {
                progress($"Rapport indisponible : {exception.Message}");
            }
        }

        var present = new Dictionary<string, bool>();
        ResultView? result = null;
        foreach (var (name, (objectKey, uploadKey, localName)) in run.Outputs)
        {
            try
            {
                await oss.CompleteUploadAsync(run.Record.BucketKey, objectKey, uploadKey, cancellationToken).ConfigureAwait(false);
                var bytes = await oss.DownloadObjectAsync(run.Record.BucketKey, objectKey, cancellationToken).ConfigureAwait(false);
                await File.WriteAllBytesAsync(Path.Combine(directory, localName), bytes, cancellationToken).ConfigureAwait(false);
                present[localName] = true;
                progress($"Sortie {name} récupérée : {localName} ({Size(bytes.Length)}).");
                if (localName == ResultLocalName)
                {
                    result = results.Read(Encoding.UTF8.GetString(bytes));
                }
            }
            catch (ApsException exception)
            {
                present[localName] = false;
                progress($"Sortie {name} absente ({localName}) : {exception.Message}");
            }
        }

        run.Record = run.Record with
        {
            WorkItemStatus = status.Status,
            CompletedAtUtc = _time.GetUtcNow(),
            Stats = status.Stats?.ToJsonString(),
            Outputs = present,
            ResultStatus = result?.Status,
            SchemaErrorCount = result?.SchemaErrors.Count,
            ReportDownloaded = report is not null,
        };
        history.Save(run.Record);

        // Relevé D6 (src/Packaging/README.md) : statut du WorkItem et présence de result.json.
        var resultPresence = present.GetValueOrDefault(ResultLocalName) ? $"présent (status {result?.Status ?? "?"})" : "absent";
        progress($"Relevé : WorkItem {status.Status}, result.json {resultPresence}.");
        return (run.Record, result, report);
    }

    /// <summary>Relit un lancement de l'historique (result.json et rapport locaux).</summary>
    public (ResultView? Result, string? Report) LoadRun(RunRecord record)
    {
        var directory = history.DirectoryOf(record.RunId);
        var resultPath = Path.Combine(directory, ResultLocalName);
        var reportPath = Path.Combine(directory, RunHistory.ReportFileName);
        return (
            File.Exists(resultPath) ? results.Read(File.ReadAllText(resultPath)) : null,
            File.Exists(reportPath) ? File.ReadAllText(reportPath) : null);
    }

    private static string ObjectKey(string runId, ActivityParameter parameter) =>
        $"{runId}/{(parameter.Zip ? parameter.Name + ".zip" : parameter.LocalName ?? parameter.Name)}";

    /// <summary>Source d'une entrée : le ZIP pour un paramètre <c>zip: true</c>, l'éditeur pour <c>settings.json</c>.</summary>
    private static Stream InputFor(ActivityParameter parameter, RunRequest request) =>
        parameter.Zip ? File.OpenRead(request.ZipPath)
        : parameter.LocalName == "settings.json" ? new MemoryStream(new UTF8Encoding(false).GetBytes(request.SettingsJson))
        : throw new InvalidOperationException($"Paramètre d'entrée non pris en charge : {parameter.Name} ({parameter.LocalName}).");

    private static (string Name, long Bytes) DescribeInput(ActivityParameter parameter, RunRequest request) =>
        parameter.Zip ? (Path.GetFileName(request.ZipPath), new FileInfo(request.ZipPath).Length) : ("settings.json (éditeur)", Encoding.UTF8.GetByteCount(request.SettingsJson));

    private static string Size(long bytes) =>
        bytes >= 1024 * 1024 ? $"{(bytes / 1024.0 / 1024.0).ToString("0.0", CultureInfo.InvariantCulture)} Mo" : $"{(bytes / 1024.0).ToString("0.0", CultureInfo.InvariantCulture)} Ko";
}
