namespace EgRoomTracker.DaManager.Services;

/// <summary>
/// Plan d'une écriture APS, affiché avant confirmation (#35 : « Chaque écriture affiche le plan et demande confirmation »).
/// <see cref="IsDestructive"/> signale une suppression (confirmation renforcée dans l'interface).
/// </summary>
public sealed record OperationPlan(string Title, IReadOnlyList<string> Steps, bool IsDestructive = false)
{
    public string Text => string.Join(Environment.NewLine, Steps);
}

/// <summary>Demande à l'utilisateur de confirmer un plan. Aucune écriture n'a lieu sans réponse positive.</summary>
public interface IConfirmationService
{
    Task<bool> ConfirmAsync(OperationPlan plan);
}

/// <summary>Sélection de fichiers et dossiers (implémentée par la vue).</summary>
public interface IFileDialogService
{
    string? PickFolder(string title, string? initialDirectory);
}
