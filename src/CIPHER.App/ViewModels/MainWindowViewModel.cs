using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using CIPHER.Actions;
using CIPHER.Core;

namespace CIPHER.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly ISettingsStore _settingsStore;
    private readonly IActionExecutor[] _executors;
    private AssistantSettings _settings = new();
    private CancellationTokenSource? _cancellationTokenSource;

    private string _input = string.Empty;
    private string _statusText = "Ready";
    private string _progressText = "Type a command or say the configured wake phrase after voice is enabled.";
    private System.Windows.Media.Brush _statusBrush = System.Windows.Media.System.Windows.Media.Brushes.LimeGreen;
    private bool _isExpanded = true;
    private bool _isSettingsOpen;
    private bool _isBusy;
    private double _progressValue;
    private bool _alwaysOnTop = true;
    private string _wakePhrase = "Hey CIPHER";

    public MainWindowViewModel()
        : this(JsonSettingsStore.CreateDefault())
    {
    }

    public MainWindowViewModel(ISettingsStore settingsStore)
    {
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _executors = new IActionExecutor[]
        {
            new BuiltInActionExecutor(),
            new WindowsActionExecutor()
        };

        Conversation = new ObservableCollection<string>
        {
            "CIPHER is ready. Try: open Notepad"
        };

        ExecuteCommand = new AsyncRelayCommand(
            _ => ExecuteAsync(),
            _ => !IsBusy && !string.IsNullOrWhiteSpace(Input),
            ex => AddConversation($"CIPHER: An unexpected error occurred: {ex.Message}"));

        CancelCommand = new RelayCommand(_ => _cancellationTokenSource?.Cancel(), _ => IsBusy);
        ClearCommand = new RelayCommand(_ => Conversation.Clear());
        ToggleExpandedCommand = new RelayCommand(_ => IsExpanded = !IsExpanded);
        ToggleSettingsCommand = new RelayCommand(_ =>
        {
            IsSettingsOpen = !IsSettingsOpen;
            if (IsSettingsOpen)
            {
                IsExpanded = true;
            }
        });
        VoiceCommand = new RelayCommand(_ =>
        {
            StatusText = "Voice not configured";
            ProgressText = "Offline voice recognition is scheduled for Stage 6.";
            AddConversation(
                "CIPHER: Voice input is not enabled in this build yet. Offline Vosk recognition is planned for Stage 6; typed commands are available now.");
        });
        SaveSettingsCommand = new AsyncRelayCommand(
            _ => SaveSettingsAsync(),
            _ => !IsBusy,
            ex => AddConversation($"CIPHER: Settings could not be saved: {ex.Message}"));
    }

    public ObservableCollection<string> Conversation { get; }

    public ICommand ExecuteCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand ToggleExpandedCommand { get; }
    public ICommand ToggleSettingsCommand { get; }
    public ICommand VoiceCommand { get; }
    public ICommand SaveSettingsCommand { get; }

    public string Input
    {
        get => _input;
        set
        {
            if (SetProperty(ref _input, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string ProgressText
    {
        get => _progressText;
        private set => SetProperty(ref _progressText, value);
    }

    public System.Windows.Media.Brush StatusBrush
    {
        get => _statusBrush;
        private set => SetProperty(ref _statusBrush, value);
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public bool IsSettingsOpen
    {
        get => _isSettingsOpen;
        set => SetProperty(ref _isSettingsOpen, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public double ProgressValue
    {
        get => _progressValue;
        private set => SetProperty(ref _progressValue, value);
    }

    public bool AlwaysOnTop
    {
        get => _alwaysOnTop;
        set => SetProperty(ref _alwaysOnTop, value);
    }

    public string WakePhrase
    {
        get => _wakePhrase;
        set => SetProperty(ref _wakePhrase, value ?? string.Empty);
    }

    public async Task InitializeAsync()
    {
        _settings = await _settingsStore.LoadAsync().ConfigureAwait(true);
        AlwaysOnTop = _settings.AlwaysOnTop;
        WakePhrase = _settings.WakePhrase;
    }

    public void RequestCancellation()
    {
        _cancellationTokenSource?.Cancel();
    }

    /// <summary>Adds a shell-level status note (tray, hotkey) to the conversation.</summary>
    public void AddStatusMessage(string message)
    {
        AddConversation($"CIPHER: {message}");
        ProgressText = message;
    }

    private async Task ExecuteAsync()
    {
        var text = Input.Trim();
        if (text.Length == 0 || IsBusy)
        {
            return;
        }

        Input = string.Empty;
        AddConversation($"You: {text}");
        IsBusy = true;
        ProgressValue = 5;
        ProgressText = "Processing command.";

        _cancellationTokenSource = new CancellationTokenSource();
        var parser = new DeterministicCommandParser(new WakeWordDetector(WakePhrase));
        var engine = new TaskEngine(
            parser,
            new DefaultActionPolicy(),
            _executors,
            new MessageBoxConfirmationService());
        engine.EventPublished += OnEngineEvent;

        try
        {
            var result = await Task.Run(
                () => engine.ExecuteTextAsync(text, CommandContext.Current, _cancellationTokenSource.Token));

            var message = result.ActionResult?.Message ?? result.Policy.Reason;
            AddConversation($"CIPHER: {message}");
            StatusText = result.Status switch
            {
                CipherTaskStatus.Succeeded => "Completed",
                CipherTaskStatus.SucceededUnverified => "Completed — not independently verified",
                CipherTaskStatus.Cancelled => "Cancelled",
                CipherTaskStatus.Blocked => "Blocked by policy",
                _ => "Failed"
            };
            ProgressValue = result.Status is CipherTaskStatus.Succeeded or CipherTaskStatus.SucceededUnverified ? 100 : 0;
        }
        catch (OperationCanceledException)
        {
            StatusText = "Cancelled";
            ProgressText = "The task was cancelled.";
            AddConversation("CIPHER: The task was cancelled.");
        }
        finally
        {
            engine.EventPublished -= OnEngineEvent;
            _cancellationTokenSource.Dispose();
            _cancellationTokenSource = null;
            IsBusy = false;
        }
    }

    private async Task SaveSettingsAsync()
    {
        _settings = _settings with
        {
            WakePhrase = string.IsNullOrWhiteSpace(WakePhrase) ? "Hey CIPHER" : WakePhrase.Trim(),
            AlwaysOnTop = AlwaysOnTop
        };

        await _settingsStore.SaveAsync(_settings).ConfigureAwait(true);
        WakePhrase = _settings.WakePhrase;
        AddConversation("CIPHER: Settings saved locally.");
        ProgressText = "Settings saved to the local CIPHER profile.";
    }

    private void OnEngineEvent(object? sender, AssistantEvent assistantEvent)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            ApplyEngineEvent(assistantEvent);
            return;
        }

        dispatcher.Invoke(() => ApplyEngineEvent(assistantEvent));
    }

    private void ApplyEngineEvent(AssistantEvent assistantEvent)
    {
        StatusText = assistantEvent.State switch
        {
            AssistantState.Listening => "Listening",
            AssistantState.Processing => "Processing",
            AssistantState.Validating => "Validating",
            AssistantState.Executing => "Executing",
            AssistantState.Verifying => "Verifying",
            AssistantState.WaitingForConfirmation => "Waiting for confirmation",
            AssistantState.Completed => "Completed",
            AssistantState.Failed => "Failed",
            AssistantState.Cancelled => "Cancelled",
            _ => "Ready"
        };

        ProgressText = assistantEvent.Message;
        ProgressValue = assistantEvent.State switch
        {
            AssistantState.Processing => 20,
            AssistantState.Validating => 35,
            AssistantState.WaitingForConfirmation => 50,
            AssistantState.Executing => 65,
            AssistantState.Verifying => 85,
            AssistantState.Completed => 100,
            AssistantState.Failed => 100,
            AssistantState.Cancelled => 0,
            _ => 0
        };

        StatusBrush = assistantEvent.State switch
        {
            AssistantState.Failed => System.Windows.Media.Brushes.IndianRed,
            AssistantState.Cancelled => System.Windows.Media.Brushes.Goldenrod,
            AssistantState.WaitingForConfirmation => System.Windows.Media.Brushes.Goldenrod,
            AssistantState.Completed => System.Windows.Media.Brushes.LimeGreen,
            AssistantState.Idle => System.Windows.Media.Brushes.LimeGreen,
            _ => System.Windows.Media.Brushes.DeepSkyBlue
        };
    }

    private void AddConversation(string message)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(() => Conversation.Add(message));
            return;
        }

        Conversation.Add(message);
    }

    private sealed class MessageBoxConfirmationService : IConfirmationService
    {
        public Task<bool> ConfirmAsync(
            ParsedCommand command,
            PolicyDecision decision,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null)
            {
                return Task.FromResult(false);
            }

            if (dispatcher.CheckAccess())
            {
                return Task.FromResult(ShowConfirmation(command, decision));
            }

            return dispatcher.InvokeAsync(() => ShowConfirmation(command, decision)).Task;
        }

        private static bool ShowConfirmation(ParsedCommand command, PolicyDecision decision)
        {
            var target = command.Target ?? "(not specified)";
            var destination = command.SecondaryTarget is null ? string.Empty : $"\nDestination: {command.SecondaryTarget}";
            var argument = command.Argument is null ? string.Empty : $"\nArgument: {command.Argument}";
            var message =
                $"CIPHER is requesting approval for a {decision.Risk} risk action.\n\n" +
                $"Action: {command.Summary}\nTarget: {target}{destination}{argument}\n\n" +
                $"Policy: {decision.Reason}\n\nDo you want to continue?";

            var result = MessageBox.Show(
                Application.Current?.MainWindow,
                message,
                "CIPHER confirmation required",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

            return result == MessageBoxResult.Yes;
        }
    }
}
