using System.Text;
using EgRoomTracker.Aps;
using EgRoomTracker.Aps.Packaging;

namespace EgRoomTracker.WorkItemRunner.Services;

/// <summary>
/// Configuration de WorkItemRunner, lue dans l'environnement : identifiants APS (<see cref="ApsSettings"/>, comme
/// <c>deploy.py</c> et DaManager), <c>EGR_REPO_ROOT</c> (définitions et schéma), <c>APS_BUCKET</c> (bucket OSS des
/// entrées et sorties) et <c>EGR_RUNNER_DATA</c> (historique local des lancements).
/// </summary>
public sealed class RunnerOptions
{
    public const string RepositoryRootVariable = "EGR_REPO_ROOT";
    public const string BucketVariable = "APS_BUCKET";
    public const string DataDirectoryVariable = "EGR_RUNNER_DATA";

    public required ApsSettings Aps { get; init; }
    public string? RepositoryRoot { get; init; }
    public string? BucketKey { get; init; }
    public required string DataDirectory { get; init; }

    public static RunnerOptions FromEnvironment(IReadOnlyDictionary<string, string?>? environment = null, string? startDirectory = null)
    {
        string? Get(string name)
        {
            var value = environment is null ? Environment.GetEnvironmentVariable(name) : environment.GetValueOrDefault(name);
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        var aps = ApsSettings.FromEnvironment(environment);
        return new RunnerOptions
        {
            Aps = aps,
            RepositoryRoot = Get(RepositoryRootVariable) ?? FindRepositoryRoot(startDirectory ?? AppContext.BaseDirectory),
            BucketKey = Get(BucketVariable) ?? DefaultBucketKey(aps.Nickname),
            DataDirectory = Get(DataDirectoryVariable) ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EgRoomTracker", "WorkItemRunner"),
        };
    }

    /// <summary>Premier dossier parent contenant <c>EgRoomTracker.sln</c> et <c>src/Packaging/definitions</c>.</summary>
    public static string? FindRepositoryRoot(string startDirectory)
    {
        for (var directory = new DirectoryInfo(startDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "EgRoomTracker.sln")) &&
                Directory.Exists(Path.Combine(directory.FullName, "src", "Packaging", "definitions")))
            {
                return directory.FullName;
            }
        }

        return null;
    }

    /// <summary>
    /// Bucket par défaut <c>egr-workitems-&lt;nickname&gt;</c> (caractères OSS autorisés : <c>[-_.a-z0-9]</c>, 3 à 128).
    /// Les clés de bucket sont globales chez APS : <c>APS_BUCKET</c> est recommandé.
    /// </summary>
    public static string? DefaultBucketKey(string? nickname)
    {
        if (string.IsNullOrWhiteSpace(nickname))
        {
            return null;
        }

        var builder = new StringBuilder("egr-workitems-");
        foreach (var c in nickname.ToLowerInvariant())
        {
            builder.Append(c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_' or '.' ? c : '-');
        }

        var key = builder.ToString();
        return key.Length > 128 ? key[..128] : key;
    }

    /// <summary>Source des identifiants, sans jamais leur valeur.</summary>
    public string CredentialSource =>
        Aps.HasClientCredentials ? $"{ApsSettings.ClientIdVariable} / {ApsSettings.ClientSecretVariable} (OAuth 2-legged)"
        : Aps.HasToken ? $"{ApsSettings.TokenVariable} (jeton fourni)"
        : "aucun identifiant : définir APS_CLIENT_ID et APS_CLIENT_SECRET (ou APS_TOKEN)";

    public static IReadOnlyList<string> RevitVersions => DeploymentService.SupportedVersions;
}
