using System.Net.Http;
using System.Windows;
using EgRoomTracker.WorkItemRunner.Services;
using EgRoomTracker.WorkItemRunner.ViewModels;

namespace EgRoomTracker.WorkItemRunner.App;

/// <summary>Composition : options lues dans l'environnement, services WPF, ViewModel.</summary>
public partial class App : System.Windows.Application
{
    private HttpClient? _httpClient;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        var window = new MainWindow();
        var viewModel = new MainViewModel(RunnerOptions.FromEnvironment(), _httpClient, new WpfConfirmationService(window), new WpfFileDialogService());
        window.DataContext = viewModel;
        window.Show();
        viewModel.Initialize();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _httpClient?.Dispose();
        base.OnExit(e);
    }
}
