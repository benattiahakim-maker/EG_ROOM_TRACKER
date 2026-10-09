using EgRoomTracker.Aps.DesignAutomation;
using EgRoomTracker.Aps.Packaging;

namespace EgRoomTracker.DaManager.Services;

/// <summary>
/// Construction pure des plans affichés avant chaque écriture. Les étapes reprennent l'algorithme de <c>deploy.py</c>
/// (src/Packaging/README.md, docs/PUBLISHING.md sections 5, 7 et 8) ; aucune URL signée ni secret n'y figure.
/// </summary>
public static class PlanBuilder
{
    private const string Test = DeploymentService.TestAlias;
    private const string Prod = DeploymentService.ProdAlias;

    public static OperationPlan Publish(string nickname, string? commit, IEnumerable<(string RevitVersion, ResourceState Bundle, ResourceState Activity, string ZipPath, long ZipSize)> items)
    {
        var steps = new List<string>
        {
            $"Publication en test (deploy.py test) depuis le commit {commit ?? "inconnu"} : ZIP et définitions doivent venir de ce même commit.",
            "GET /engines : arrêt avant toute écriture si un engine manque.",
        };
        foreach (var (version, bundle, activity, zip, size) in items)
        {
            steps.Add($"Revit {version} :");
            steps.Add($"  contrôle du ZIP {Path.GetFileName(zip)} ({size / 1024.0 / 1024.0:0.0} Mo)");
            steps.Add(bundle.Exists
                ? $"  POST /appbundles/{bundle.Id}/versions (nouvelle version après v{Max(bundle)}) + téléversement du ZIP"
                : $"  POST /appbundles (création de {bundle.Id}, v1) + téléversement du ZIP");
            steps.Add(AliasStep(DaResourceKind.AppBundle, bundle, Test, "nouvelle version"));
            steps.Add(activity.Exists
                ? $"  POST /activities/{activity.Id}/versions (nouvelle version après v{Max(activity)}, AppBundle {nickname}.{bundle.Id}+{Test})"
                : $"  POST /activities (création de {activity.Id}, AppBundle {nickname}.{bundle.Id}+{Test})");
            steps.Add(AliasStep(DaResourceKind.Activity, activity, Test, "nouvelle version"));
            steps.AddRange(QuotaWarnings(bundle, activity));
        }

        return new OperationPlan("Publier une nouvelle version en test", steps);
    }

    public static OperationPlan Promote(string nickname, string? commit, IEnumerable<(string RevitVersion, ResourceState Bundle, ResourceState Activity)> items)
    {
        var steps = new List<string>
        {
            $"Promotion en production (deploy.py promote) depuis le commit {commit ?? "inconnu"} : uniquement après une recette réussie, depuis le même commit que test.",
        };
        foreach (var (version, bundle, activity) in items)
        {
            var testVersion = bundle.AliasVersion(Test) ?? throw new InvalidOperationException($"{bundle.Id} : aucun alias {Test} à promouvoir");
            steps.Add($"Revit {version} :");
            steps.Add($"  {bundle.Id}+{Prod} -> v{testVersion} (actuellement {Show(bundle.AliasVersion(Prod))})");
            steps.Add($"  POST /activities/{activity.Id}/versions (AppBundle {nickname}.{bundle.Id}+{Prod}), puis {activity.Id}+{Prod} sur cette version (actuellement {Show(activity.AliasVersion(Prod))})");
            steps.Add(bundle.AliasVersion(Prod) is { } pb && activity.AliasVersion(Prod) is { } pa
                ? $"  Retour arrière possible : AppBundle v{pb}, Activity v{pa} (à recopier au journal)."
                : "  Première promotion : pas de retour arrière par alias possible.");
            steps.AddRange(QuotaWarnings(activity));
        }

        return new OperationPlan("Promouvoir test en prod", steps);
    }

    /// <summary>Création ou déplacement d'un alias (retour arrière compris). La version cible doit exister.</summary>
    public static OperationPlan SetAlias(ResourceState resource, string alias, int version)
    {
        if (!resource.Versions.Contains(version))
        {
            throw new InvalidOperationException($"{resource.Id} : la version v{version} n'existe pas.");
        }

        var current = resource.AliasVersion(alias);
        var steps = new List<string>
        {
            current is null
                ? $"POST /{resource.Kind.PathSegment()}/{resource.Id}/aliases : création de {alias} -> v{version}"
                : $"PATCH /{resource.Kind.PathSegment()}/{resource.Id}/aliases/{alias} : v{current} -> v{version}",
        };
        if (current is not null)
        {
            steps.Add($"Retour possible : repointer {alias} sur v{current}.");
        }

        if (alias == Prod)
        {
            steps.Add("Production : repointer aussi l'autre ressource (AppBundle et Activity) si nécessaire (docs/PUBLISHING.md section 8).");
        }

        return new OperationPlan($"Alias {resource.Id}+{alias}", steps);
    }

    public static OperationPlan Cleanup(CleanupPlan plan, IReadOnlyList<int>? journalVersions = null)
    {
        var journal = journalVersions is { Count: > 0 }
            ? $"Journal de publication : {string.Join(", ", journalVersions.Select(v => $"v{v}"))} protégée(s) automatiquement."
            : "Journal de publication : aucune version citée pour cette ressource.";
        return new($"Supprimer {plan.ToDelete.Count} version(s) de {plan.Id}", [.. plan.Describe(), journal, "Suppression définitive : une version supprimée ne peut plus servir de retour arrière."], IsDestructive: true);
    }

    private static string AliasStep(DaResourceKind kind, ResourceState state, string alias, string target) =>
        state.AliasVersion(alias) is { } current
            ? $"  PATCH /{kind.PathSegment()}/{state.Id}/aliases/{alias} : v{current} -> {target}"
            : $"  POST /{kind.PathSegment()}/{state.Id}/aliases : création de {alias} -> {target}";

    private static IEnumerable<string> QuotaWarnings(params ResourceState[] states) =>
        states.Where(s => s.Versions.Count >= ResourceState.VersionQuota - 10)
            .Select(s => $"  ATTENTION : {s.Id} a {s.Versions.Count} versions (quota {ResourceState.VersionQuota}) : nettoyer les anciennes versions.");

    private static int Max(ResourceState state) => state.Versions.Count == 0 ? 0 : state.Versions.Max();

    private static string Show(int? version) => version is null ? "aucun" : $"v{version}";
}
