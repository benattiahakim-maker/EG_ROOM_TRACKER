using System.Windows;
using EgRoomTracker.DaManager.Services;
using Microsoft.Win32;

namespace EgRoomTracker.DaManager.App;

/// <summary>Affiche le plan dans une fenêtre modale ; seule une confirmation explicite autorise l'écriture.</summary>
public sealed class WpfConfirmationService(Window owner) : IConfirmationService
{
    public Task<bool> ConfirmAsync(OperationPlan plan)
    {
        var dialog = new ConfirmationWindow(plan) { Owner = owner };
        return Task.FromResult(dialog.ShowDialog() == true);
    }
}

public sealed class WpfFileDialogService : IFileDialogService
{
    public string? PickFolder(string title, string? initialDirectory)
    {
        var dialog = new OpenFolderDialog { Title = title };
        if (!string.IsNullOrEmpty(initialDirectory) && System.IO.Directory.Exists(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
}
