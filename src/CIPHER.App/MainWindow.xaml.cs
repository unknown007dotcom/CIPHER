using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CIPHER.App.ViewModels;

namespace CIPHER.App;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainWindowViewModel();
        DataContext = _viewModel;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        Closing += MainWindow_Closing;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        PositionAtTopCenter();
        UpdateWindowLayout();
        await _viewModel.InitializeAsync();
        Topmost = _viewModel.AlwaysOnTop;
        CommandInput.Focus();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.IsExpanded) or nameof(MainWindowViewModel.IsSettingsOpen))
        {
            UpdateWindowLayout();
        }
        else if (e.PropertyName == nameof(MainWindowViewModel.AlwaysOnTop))
        {
            Topmost = _viewModel.AlwaysOnTop;
        }
    }

    private void UpdateWindowLayout()
    {
        Height = !_viewModel.IsExpanded
            ? 86
            : _viewModel.IsSettingsOpen
                ? 650
                : 560;
        PositionAtTopCenter();
    }

    private void PositionAtTopCenter()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Left + ((workArea.Width - Width) / 2);
        Top = workArea.Top + 12;
    }

    private void CommandInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        if (_viewModel.ExecuteCommand.CanExecute(null))
        {
            _viewModel.ExecuteCommand.Execute(null);
        }

        e.Handled = true;
    }

    private void HeaderBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.OriginalSource is Button)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // DragMove can race with window state changes.
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        _viewModel.RequestCancellation();
        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
    }
}
