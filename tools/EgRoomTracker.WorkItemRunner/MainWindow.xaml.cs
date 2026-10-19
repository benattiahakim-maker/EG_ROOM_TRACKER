using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using EgRoomTracker.WorkItemRunner.Services;
using EgRoomTracker.WorkItemRunner.ViewModels;

namespace EgRoomTracker.WorkItemRunner.App;

/// <summary>
/// Vue principale : tout est lié au MainViewModel, sauf les colonnes du tableau des pièces, générées d'après les clés
/// de <c>rooms[]</c> du résultat affiché (<see cref="Services.ResultView.RoomColumns"/>).
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is INotifyPropertyChanged previous)
        {
            previous.PropertyChanged -= OnViewModelChanged;
        }

        if (e.NewValue is INotifyPropertyChanged current)
        {
            current.PropertyChanged += OnViewModelChanged;
        }

        RebuildRoomColumns();
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(MainViewModel.Result))
        {
            RebuildRoomColumns();
        }
    }

    private void RebuildRoomColumns()
    {
        RoomsGrid.ItemsSource = null;
        RoomsGrid.Columns.Clear();
        if (DataContext is not MainViewModel { Result: { } result })
        {
            return;
        }

        // Colonne de l'arbre : retrait et flèche de dépliage (pièce → portes, équipements, terminaux).
        RoomsGrid.Columns.Add(new DataGridTemplateColumn
        {
            CellTemplate = (DataTemplate)FindResource("TreeToggleCell"),
            Width = DataGridLength.Auto,
        });
        for (var index = 0; index < result.RoomColumns.Count; index++)
        {
            // L'en-tête est le chemin exact de la clé dans le JSON (TextBlock : le « _ » n'est pas une touche d'accès).
            RoomsGrid.Columns.Add(new DataGridTextColumn
            {
                Header = new TextBlock { Text = result.RoomColumns[index] },
                Binding = new Binding($"[{index}]"),
                Width = DataGridLength.Auto,
            });
        }

        RoomsGrid.ItemsSource = TableRow.Visible(result.RoomTree);
    }

    /// <summary>Déplie ou replie une pièce (flèche), puis réaffiche les lignes visibles.</summary>
    private void OnToggleRow(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TableRow row })
        {
            Toggle(row);
        }
    }

    /// <summary>Double-clic sur une pièce : même effet que la flèche.</summary>
    private void OnRoomsGridDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (RoomsGrid.SelectedItem is TableRow { HasChildren: true } row)
        {
            Toggle(row);
        }
    }

    private void Toggle(TableRow row)
    {
        if (DataContext is MainViewModel { Result: { } result })
        {
            row.IsExpanded = !row.IsExpanded;
            RoomsGrid.ItemsSource = TableRow.Visible(result.RoomTree);
            RoomsGrid.SelectedItem = row;
        }
    }
}
