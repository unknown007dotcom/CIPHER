using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using CIPHER.Actions;
using CIPHER.Browser;
using CIPHER.Core;
using CIPHER.Voice;

namespace CIPHER.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly ISettingsStore _settingsStore;
    private readonly IActionExecutor[] _executors;
    private readonly BrowserActionExecutor _browserExecutor;
    private AssistantSettings _settings = new();
    private CancellationTokenSource? _cancellationTokenSource;

    private VoskMicrophoneListener? _voiceListener;
    private VoiceSession? _voiceSession;
    private EspeakSpeechSynthesizer? _speechSynthesizer;
    private bool _isListening;
    private bool _continuousListening;
    private bool _speechOutputEnabled;
    private bool _wakeListeningEnabled;
    private bool _speechUnavailableReported;

    private string _input = string.Empty;
    private string _statusText = "Ready";
    private string _progressText = "Type a command or say the configured wake phrase after voice is enabled.";
    private global::System.Windows.Media.Brush _statusBrush = global::System.Windows.Media.Brushes.LimeGreen;
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
        // The browser session starts lazily on the first browser command and runs
        // headed in the app so the user can see and take over the page at any time.
        _browserExecutor = new BrowserActionExecutor(new BrowserSessionOptions { Headless = false });
        _executors = new IActionExecutor[]
        {
            new BuiltInActionExecutor(),
            new WindowsActionExecutor(),
            _browserExecutor
        };

        Conversation = new ObservableCollection<string>
        {
            "CIPHER is ready. Try: open Notepad"
        };

        ExecuteCommand = new AsyncRelayCommand(
            _ => ExecuteFromInputAsync(),
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
        VoiceCommand = new AsyncRelayCommand(
            _ => TogglePushToTalkAsync(),
            _ => true,
            ex => AddConversation($"CIPHER: Voice input failed: {ex.Message}"));
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

    public global::System.Windows.Media.Brush StatusBrush
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

    /// <summary>True while the microphone is open (push-to-talk or wake listening).</summary>
    public bool IsListening
    {
        get => _isListening;
        private set => SetProperty(ref _isListening, value);
    }

    /// <summary>Speak command results aloud through eSpeak NG (persisted setting).</summary>
    public bool SpeechOutputEnabled
    {
        get => _speechOutputEnabled;
        set => SetProperty(ref _speechOutputEnabled, value);
    }

    /// <summary>
    /// Continuous wake-phrase listening. Deliberately a runtime-only toggle: it is never
    /// auto-started at launch, even though the checkbox state is simple to re-enable.
    /// </summary>
    public bool WakeListeningEnabled
    {
        get => _wakeListeningEnabled;
        set
        {
            if (SetProperty(ref _wakeListeningEnabled, value))
            {
                _ = OnWakeListeningToggledAsync(value);
            }
        }
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
        SpeechOutputEnabled = _settings.SpeechOutputEnabled;
    }

    public void RequestCancellation()
    {
        _cancellationTokenSource?.Cancel();
    }

    /// <summary>Stops voice capture/speech and closes the lazily started browser session.</summary>
    public async Task ShutdownAsync()
    {
        try
        {
            StopListening();
            _voiceListener?.Dispose();
            _voiceListener = null;
            _speechSynthesizer?.Dispose();
            _speechSynthesizer = null;
        }
        catch (Exception)
        {
            // Voice shutdown must never block application exit.
        }

        try
        {
            await _browserExecutor.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Browser shutdown must never block application exit.
        }
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

    // ---- Offline voice (Stage 6) ----

    private async Task TogglePushToTalkAsync()
    {
        if (IsListening)
        {
            // Pressing the microphone button while listening stops capture; any final
            // transcript flushed from the recognizer is still processed first.
            StopListening(processFlushed: true);
            return;
        }

        _continuousListening = false;
        var started = await StartListeningAsync().ConfigureAwait(true);
        if (started)
        {
            // Push-to-talk: the next utterance is the command, no wake phrase required.
            _voiceSession?.Prime(DateTimeOffset.Now);
            StatusText = "Listening";
            ProgressText = "Listening… speak a command, then press the microphone button again to stop.";
        }
    }

    private async Task OnWakeListeningToggledAsync(bool enabled)
    {
        if (enabled)
        {
            _continuousListening = true;
            var started = await StartListeningAsync().ConfigureAwait(true);
            if (started)
            {
                StatusText = "Listening for wake phrase";
                ProgressText = $"Say '{WakePhrase}' followed by a command. The microphone stays open until you turn this off.";
                AddConversation($"CIPHER: Wake-phrase listening is on. Say '{WakePhrase}' and then a command.");
            }
            else
            {
                _continuousListening = false;
                _wakeListeningEnabled = false;
                OnPropertyChanged(nameof(WakeListeningEnabled));
            }
        }
        else
        {
            _continuousListening = false;
            StopListening(processFlushed: false);
            if (!IsBusy)
            {
                StatusText = "Ready";
            }

            ProgressText = "Wake-phrase listening is off. The microphone is closed.";
        }
    }

    private async Task<bool> StartListeningAsync()
    {
        if (IsListening)
        {
            return true;
        }

        var listener = await EnsureVoiceListenerAsync().ConfigureAwait(true);
        if (listener is null)
        {
            return false;
        }

        try
        {
            _speechSynthesizer?.StopCurrent();
            _voiceSession = new VoiceSession(new WakeWordDetector(
                string.IsNullOrWhiteSpace(WakePhrase) ? "Hey CIPHER" : WakePhrase.Trim()));
            listener.Start();
            IsListening = true;
            return true;
        }
        catch (Exception ex)
        {
            StatusText = "Microphone unavailable";
            ProgressText = "The microphone could not be opened.";
            AddConversation($"CIPHER: The microphone could not be started: {ex.Message}");
            return false;
        }
    }

    private void StopListening(bool processFlushed)
    {
        if (_voiceListener is null || !IsListening)
        {
            return;
        }

        var flushed = _voiceListener.Stop();
        IsListening = false;
        if (!IsBusy)
        {
            StatusText = "Ready";
        }

        if (processFlushed && !string.IsNullOrWhiteSpace(flushed))
        {
            HandleVoiceTranscript(flushed);
        }
    }

    private async Task<VoskMicrophoneListener?> EnsureVoiceListenerAsync()
    {
        if (_voiceListener is not null)
        {
            return _voiceListener;
        }

        var modelPath = VoskModelLocator.Resolve(_settings.VoiceModelPath);
        if (modelPath is null)
        {
            StatusText = "Voice model not found";
            ProgressText = "The offline speech model is missing, so the microphone stays closed.";
            AddConversation(
                $"CIPHER: Voice input needs the offline Vosk model '{VoskModelLocator.SmallEnglishModelName}'. " +
                $"Unzip it to {_settings.VoiceModelPath} (or into a models folder next to CIPHER), then try again. " +
                "Recognition runs fully on this PC; no audio is uploaded anywhere.");
            return null;
        }

        try
        {
            StatusText = "Loading voice model";
            ProgressText = "Loading the offline speech model…";
            var listener = await Task.Run(() =>
            {
                var recognizer = new VoskSpeechRecognizer(modelPath);
                return new VoskMicrophoneListener(recognizer);
            }).ConfigureAwait(true);

            listener.PartialTextAvailable += (_, text) => PostToUi(() =>
            {
                if (IsListening)
                {
                    ProgressText = $"Listening… {text}";
                }
            });
            listener.FinalTextAvailable += (_, text) => PostToUi(() => HandleVoiceTranscript(text));
            listener.ListeningError += (_, ex) => PostToUi(() =>
            {
                AddConversation($"CIPHER: Voice listening stopped: {ex.Message}");
                StopListening(processFlushed: false);
            });

            _voiceListener = listener;
            return listener;
        }
        catch (Exception ex)
        {
            StatusText = "Voice unavailable";
            ProgressText = "The offline voice engine could not start.";
            AddConversation($"CIPHER: The offline voice engine could not start: {ex.Message}");
            return null;
        }
    }

    private void HandleVoiceTranscript(string text)
    {
        if (_voiceSession is null || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var result = _voiceSession.HandleFinalTranscript(text, DateTimeOffset.Now);
        switch (result.Outcome)
        {
            case VoiceTranscriptOutcome.CommandReady:
                AddConversation($"Heard: {text}");
                if (!_continuousListening)
                {
                    StopListening(processFlushed: false);
                }

                if (IsBusy)
                {
                    ProgressText = "Still working on the previous command; the spoken command was not started.";
                    AddConversation("CIPHER: I am still busy with the previous command, so I did not start the spoken one.");
                    return;
                }

                _ = ExecuteTextAsync(result.CommandText);
                break;

            case VoiceTranscriptOutcome.WakePhraseHeard:
                StatusText = "Wake phrase heard";
                ProgressText = $"Heard '{WakePhrase}'. Say your command now.";
                break;

            default:
                if (_continuousListening)
                {
                    ProgressText = $"Listening for '{WakePhrase}'…";
                }

                break;
        }
    }

    private void SpeakResultMessage(string message)
    {
        if (!SpeechOutputEnabled || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var synthesizer = EnsureSpeechSynthesizer();
        if (synthesizer is null)
        {
            if (!_speechUnavailableReported)
            {
                _speechUnavailableReported = true;
                AddConversation(
                    "CIPHER: Speech output is enabled but eSpeak NG (espeak-ng.exe) was not found. " +
                    "Install eSpeak NG or place espeak-ng.exe in a tools\\espeak-ng folder next to CIPHER; " +
                    "results stay text-only until then.");
            }

            return;
        }

        var spoken = message.Trim();
        if (spoken.Length > 180)
        {
            spoken = spoken[..180].TrimEnd();
        }

        _ = SpeakSafelyAsync(synthesizer, spoken);
    }

    private EspeakSpeechSynthesizer? EnsureSpeechSynthesizer()
    {
        if (_speechSynthesizer is not null)
        {
            return _speechSynthesizer;
        }

        var voice = string.IsNullOrWhiteSpace(_settings.SpeechVoice) ||
                    string.Equals(_settings.SpeechVoice, "Default", StringComparison.OrdinalIgnoreCase)
            ? _settings.VoiceLanguage.ToLowerInvariant()
            : _settings.SpeechVoice;
        var synthesizer = new EspeakSpeechSynthesizer(
            string.IsNullOrWhiteSpace(_settings.EspeakExecutablePath) ? null : _settings.EspeakExecutablePath,
            voice);
        if (!synthesizer.IsAvailable)
        {
            synthesizer.Dispose();
            return null;
        }

        _speechSynthesizer = synthesizer;
        return synthesizer;
    }

    private async Task SpeakSafelyAsync(EspeakSpeechSynthesizer synthesizer, string text)
    {
        try
        {
            await synthesizer.SpeakAsync(text).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Speech output must never break the command flow.
        }
    }

    private void PostToUi(Action action)
    {
        var dispatcher = global::System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.BeginInvoke(action);
    }

    private void OnEngineEvent(object? sender, AssistantEvent assistantEvent)
    {
        var dispatcher = global::System.Windows.Application.Current?.Dispatcher;
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
            AssistantState.Failed => global::System.Windows.Media.Brushes.IndianRed,
            AssistantState.Cancelled => global::System.Windows.Media.Brushes.Goldenrod,
            AssistantState.WaitingForConfirmation => global::System.Windows.Media.Brushes.Goldenrod,
            AssistantState.Completed => global::System.Windows.Media.Brushes.LimeGreen,
            AssistantState.Idle => global::System.Windows.Media.Brushes.LimeGreen,
            _ => global::System.Windows.Media.Brushes.DeepSkyBlue
        };
    }

    private void AddConversation(string message)
    {
        var dispatcher = global::System.Windows.Application.Current?.Dispatcher;
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

            var dispatcher = global::System.Windows.Application.Current?.Dispatcher;
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

            var result = global::System.Windows.MessageBox.Show(
                global::System.Windows.Application.Current?.MainWindow,
                message,
                "CIPHER confirmation required",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

            return result == MessageBoxResult.Yes;
        }
    }
}
