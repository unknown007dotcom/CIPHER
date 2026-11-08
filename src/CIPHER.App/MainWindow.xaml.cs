using System.ComponentModel;
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using CIPHER.App.ViewModels;
using Button = System.Windows.Controls.Button;

namespace CIPHER.App;

public partial class MainWindow : Window
{
    private const int GlobalHotkeyId = 0xC1FE;

    private readonly MainWindowViewModel _viewModel;
    private NotifyIcon? _trayIcon;
    private HwndSource? _hwndSource;
    private bool _hotkeyRegistered;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainWindowViewModel();
        DataContext = _viewModel;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        Closing += MainWindow_Closing;
        CreateTrayIcon();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var handle = new WindowInteropHelper(this).Handle;
        _hwndSource = HwndSource.FromHwnd(handle);
        _hwndSource?.AddHook(WndProc);

        _hotkeyRegistered = NativeMethods.RegisterHotKey(
            handle,
            GlobalHotkeyId,
            NativeMethods.ModAlt | NativeMethods.ModControl,
            NativeMethods.VkC);

        if (!_hotkeyRegistered)
        {
            _viewModel.AddStatusMessage(
                "The Ctrl+Alt+C global hotkey could not be registered because another application is using it. CIPHER remains available from the taskbar and system tray.");
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WmHotkey && wParam.ToInt32() == GlobalHotkeyId)
        {
            ToggleVisibility();
            handled = true;
        }

        return IntPtr.Zero;
    }

    private void CreateTrayIcon()
    {
        var showItem = new ToolStripMenuItem("Show CIPHER", null, (_, _) => ShowCipher());
        var hideItem = new ToolStripMenuItem("Hide CIPHER", null, (_, _) => Hide());
        var exitItem = new ToolStripMenuItem("Exit CIPHER", null, (_, _) => Close());

        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "CIPHER — offline desktop assistant",
            Visible = true,
            ContextMenuStrip = new ContextMenuStrip()
        };
        _trayIcon.ContextMenuStrip.Items.Add(showItem);
        _trayIcon.ContextMenuStrip.Items.Add(hideItem);
        _trayIcon.ContextMenuStrip.Items.Add(new ToolStripSeparator());
        _trayIcon.ContextMenuStrip.Items.Add(exitItem);
        _trayIcon.DoubleClick += (_, _) => ShowCipher();
    }

    private void ShowCipher()
    {
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        CommandInput.Focus();
    }

    private void ToggleVisibility()
    {
        if (IsVisible && IsActive)
        {
            Hide();
        }
        else
        {
            ShowCipher();
        }
    }

    private async void SaveApiKey_Click(object sender, RoutedEventArgs e)
    {
        // The password box content goes straight to the DPAPI store and is cleared
        // immediately; it is never bound, journaled, or logged.
        await _viewModel.SaveApiKeyAsync(AiApiKeyBox.Password);
        AiApiKeyBox.Clear();
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

    private void CommandInput_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
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
        _ = _viewModel.ShutdownAsync();
        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;

        if (_hotkeyRegistered)
        {
            NativeMethods.UnregisterHotKey(new WindowInteropHelper(this).Handle, GlobalHotkeyId);
            _hotkeyRegistered = false;
        }

        _hwndSource?.RemoveHook(WndProc);
        _hwndSource = null;

        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }
    }
}
