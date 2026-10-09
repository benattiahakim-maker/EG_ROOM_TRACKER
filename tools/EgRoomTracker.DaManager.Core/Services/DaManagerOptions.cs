using EgRoomTracker.Aps;
using EgRoomTracker.Aps.Packaging;

namespace EgRoomTracker.DaManager.Services;

/// <summary>
/// Configuration de DaManager, lue dans les mêmes variables que <c>deploy.py</c> (docs/PUBLISHING.md section 1) :
/// identifiants APS (<see cref="ApsSettings"/>), <c>REVIT_VERSIONS</c>, <c>APPBUNDLE_DIR</c>, plus
/// <c>EGR_REPO_ROOT</c> (racine du dépôt, sinon recherchée à partir du dossier de l'application).
/// </summary>
public sealed class DaManagerOptions
{
    public const string RevitVersionsVariable = "REVIT_VERSIONS";
    public const string AppBundleDirVariable = "APPBUNDLE_DIR";
    public const string RepositoryRootVariable = "EGR_REPO_ROOT";

    public required ApsSettings Aps { get; init; }
    public required IReadOnlyList<string> RevitVersions { get; init; }
    public string? RepositoryRoot { get; init; }
    public string? BundleDirectory { get; init; }

    public static DaManagerOptions FromEnvironment(IReadOnlyDictionary<string, string?>? environment = null, string? startDirectory = null)
    {
        string? Get(string name)
        {
            var value = environment is null ? Environment.GetEnvironmentVariable(name) : environment.GetValueOrDefault(name);
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        var versions = (Get(RevitVersionsVariable) ?? string.Join(' ', DeploymentService.SupportedVersions))
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(v => DeploymentService.SupportedVersions.Contains(v))
            .ToList();
        var root = Get(RepositoryRootVariable) ?? FindRepositoryRoot(startDirectory ?? AppContext.BaseDirectory);
        var bundles = Get(AppBundleDirVariable) ?? (root is null ? null : Path.Combine(root, "artifacts", "appbundles"));
        return new DaManagerOptions { Aps = ApsSettings.FromEnvironment(environment), RevitVersions = versions, RepositoryRoot = root, BundleDirectory = bundles };
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

    /// <summary>Source des identifiants, sans jamais leur valeur.</summary>
    public string CredentialSource =>
        Aps.HasClientCredentials ? $"{ApsSettings.ClientIdVariable} / {ApsSettings.ClientSecretVariable} (OAuth 2-legged)"
        : Aps.HasToken ? $"{ApsSettings.TokenVariable} (jeton fourni)"
        : "aucun identifiant : définir APS_CLIENT_ID et APS_CLIENT_SECRET (ou APS_TOKEN)";
}
