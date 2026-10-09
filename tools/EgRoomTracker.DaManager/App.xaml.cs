using System.Net.Http;
using System.Windows;
using EgRoomTracker.DaManager.Services;
using EgRoomTracker.DaManager.ViewModels;

namespace EgRoomTracker.DaManager.App;

/// <summary>Composition : options lues dans l'environnement, services WPF, ViewModel.</summary>
public partial class App : Application
{
    private HttpClient? _httpClient;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        var options = DaManagerOptions.FromEnvironment();
        var window = new MainWindow();
        window.DataContext = new MainViewModel(options, _httpClient, new WpfConfirmationService(window), new WpfFileDialogService());
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _httpClient?.Dispose();
        base.OnExit(e);
    }
}
