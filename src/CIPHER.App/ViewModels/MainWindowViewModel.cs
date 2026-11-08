using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using CIPHER.Actions;
using CIPHER.Browser;
using CIPHER.Core;
using CIPHER.Delegation;
using CIPHER.Memory;
using CIPHER.Voice;

namespace CIPHER.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly ISettingsStore _settingsStore;
    private readonly IActionExecutor[] _executors;
    private readonly BrowserActionExecutor _browserExecutor;
    private AssistantSettings _settings = new();
    private CancellationTokenSource? _cancellationTokenSource;

    private SqliteMemoryStore? _memoryStore;
    private MemoryCommandHandler? _memoryCommands;

    private readonly DpapiApiKeyStore _apiKeyStore = DpapiApiKeyStore.CreateDefault();
    private readonly AuditLog _auditLog = new(AuditLog.CreateDefaultPath());
    private bool _aiDelegationEnabled;
    private string _aiBaseUrl = "https://api.openai.com/v1";
    private string _aiModel = "gpt-4o-mini";
    private string _apiKeyStatus = "No API key saved yet.";

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
            new WindowsActionExecutor(
                new WindowService(),
                new InputService(),
                appPathResolver: (name, cancellationToken) => _memoryStore is null
                    ? Task.FromResult<string?>(null)
                    : _memoryStore.GetAppPathAsync(name, cancellationToken)),
            _browserExecutor,
            new DelegationActionExecutor(() => new AiDelegationOptions(
                _settings.AiDelegationEnabled && !global::CIPHER.App.App.IsSafeMode,
                _settings.AiBaseUrl,
                _settings.AiModel,
                LoadApiKeyOrNull(),
                _settings.ResultsDirectory))
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
        ClearApiKeyCommand = new RelayCommand(_ =>
        {
            _apiKeyStore.Delete();
            RefreshApiKeyStatus();
            Audit("security.api_key_deleted", "The saved external AI API key was deleted by the user.");
            AddConversation("CIPHER: The saved API key was deleted from this PC.");
        });
    }

    public ObservableCollection<string> Conversation { get; }

    public ICommand ExecuteCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand ToggleExpandedCommand { get; }
    public ICommand ToggleSettingsCommand { get; }
    public ICommand VoiceCommand { get; }
    public ICommand SaveSettingsCommand { get; }
    public ICommand ClearApiKeyCommand { get; }

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

    /// <summary>Master switch for Stage 8 external AI delegation (persisted; off by default).</summary>
    public bool AiDelegationEnabled
    {
        get => _aiDelegationEnabled;
        set => SetProperty(ref _aiDelegationEnabled, value);
    }

    public string AiBaseUrl
    {
        get => _aiBaseUrl;
        set => SetProperty(ref _aiBaseUrl, value ?? string.Empty);
    }

    public string AiModel
    {
        get => _aiModel;
        set => SetProperty(ref _aiModel, value ?? string.Empty);
    }

    /// <summary>Whether a DPAPI-protected API key exists. The key itself is never displayed.</summary>
    public string ApiKeyStatus
    {
        get => _apiKeyStatus;
        private set => SetProperty(ref _apiKeyStatus, value);
    }

    public async Task InitializeAsync()
    {
        _settings = await _settingsStore.LoadAsync().ConfigureAwait(true);
        AlwaysOnTop = _settings.AlwaysOnTop;
        WakePhrase = _settings.WakePhrase;
        SpeechOutputEnabled = _settings.SpeechOutputEnabled;
        AiDelegationEnabled = _settings.AiDelegationEnabled;
        AiBaseUrl = _settings.AiBaseUrl;
        AiModel = _settings.AiModel;
        RefreshApiKeyStatus();
        if (global::CIPHER.App.App.IsSafeMode)
        {
            AddConversation(
                "CIPHER: Safe mode is on (--safe-mode). External AI delegation and continuous wake " +
                "listening are disabled for this session; typed commands, quarantine review, and " +
                "'verify audit log' are available.");
            Audit("app.safe_mode", "CIPHER started with --safe-mode.");
        }

        await InitializeMemoryAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Opens the local SQLite memory, reports any task the previous session left
    /// unfinished (marking it Interrupted — never silently re-running it), and shows the
    /// most recent history. Memory problems degrade to a notice, never a startup failure.
    /// </summary>
    private async Task InitializeMemoryAsync()
    {
        try
        {
            var store = SqliteMemoryStore.CreateDefault();
            await store.InitializeAsync().ConfigureAwait(true);
            _memoryStore = store;
            _memoryCommands = new MemoryCommandHandler(store);

            if (store.RecoveredFromCorruption)
            {
                AddConversation(
                    "CIPHER: The local memory database was damaged, so I quarantined it and started a fresh one. " +
                    "Learned aliases, app locations, and history were reset. (Passwords and credentials are never stored there.)");
                Audit("memory.corruption_recovered", $"Database quarantined and recreated: {store.DatabasePath}");
            }

            var incomplete = await store.GetIncompleteTasksAsync().ConfigureAwait(true);
            foreach (var task in incomplete)
            {
                await store.CompleteTaskAsync(
                    task.Id,
                    TaskRecordStatus.Interrupted,
                    "The previous CIPHER session ended before this task completed.",
                    CancellationToken.None).ConfigureAwait(true);
                AddConversation(
                    $"CIPHER: Recovery note — the previous session ended during '{task.InputText}'. " +
                    "I did not re-run it automatically; ask again if you still want it.");
            }

            if (incomplete.Count > 0)
            {
                Audit("memory.interrupted_recovered", $"{incomplete.Count} interrupted task(s) were marked Interrupted at startup.");
            }

            var history = await store.GetRecentHistoryAsync(3).ConfigureAwait(true);
            foreach (var entry in history)
            {
                AddConversation($"History: '{entry.InputText}' → {entry.Status}");
            }

            var quarantineCount = new QuarantineService().Count();
            if (quarantineCount > 0)
            {
                AddConversation(
                    $"CIPHER: You have {quarantineCount} item(s) in quarantine. Say 'list quarantine' to review " +
                    "them or 'restore <name>' to recover one.");
            }
        }
        catch (Exception ex)
        {
            AddConversation(
                $"CIPHER: Local memory is unavailable ({ex.Message}). Commands still work; " +
                "history, aliases, and crash recovery are off for this session.");
        }
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
            StopListening(processFlushed: false);
            _voiceListener?.Dispose();
            _voiceListener = null;
            _speechSynthesizer?.Dispose();
            _speechSynthesizer = null;
            _memoryStore?.Dispose();
            _memoryStore = null;
            _memoryCommands = null;
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

    private async Task ExecuteFromInputAsync()
    {
        var text = Input.Trim();
        if (text.Length == 0 || IsBusy)
        {
            return;
        }

        Input = string.Empty;
        await ExecuteTextAsync(text).ConfigureAwait(true);
    }

    /// <summary>Runs one command (typed or recognized from speech) through the task engine.</summary>
    private async Task ExecuteTextAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || IsBusy)
        {
            return;
        }

        // Memory commands (teach/forget/list) and whole-phrase aliases are handled before
        // parsing. Alias expansions still run through the full parser/policy/confirmation
        // flow below, so an alias can never bypass a safety check.
        var effectiveText = text;
        if (_memoryCommands is not null)
        {
            try
            {
                var handling = await _memoryCommands.TryHandleAsync(text).ConfigureAwait(true);
                if (handling.Kind == MemoryHandlingKind.Responded)
                {
                    AddConversation($"You: {text}");
                    AddConversation($"CIPHER: {handling.Response}");
                    StatusText = "Completed";
                    ProgressText = handling.Response ?? string.Empty;
                    return;
                }

                if (handling.Kind == MemoryHandlingKind.ExpandAlias && !string.IsNullOrWhiteSpace(handling.Expansion))
                {
                    effectiveText = handling.Expansion;
                }
            }
            catch (Exception ex)
            {
                AddConversation($"CIPHER: Memory lookup failed ({ex.Message}); running your command as typed.");
            }
        }

        AddConversation(effectiveText == text ? $"You: {text}" : $"You: {text}  (alias for: {effectiveText})");
        IsBusy = true;
        ProgressValue = 5;
        ProgressText = "Processing command.";

        _cancellationTokenSource = new CancellationTokenSource();
        var parser = new DeterministicCommandParser(new WakeWordDetector(WakePhrase));
        var engine = new TaskEngine(
            parser,
            new DefaultActionPolicy(),
            _executors,
            new MessageBoxConfirmationService(),
            _memoryStore);
        engine.EventPublished += OnEngineEvent;

        try
        {
            var result = await Task.Run(
                () => engine.ExecuteTextAsync(effectiveText, CommandContext.Current, _cancellationTokenSource.Token));

            var message = result.ActionResult?.Message ?? result.Policy.Reason;
            AddConversation($"CIPHER: {message}");
            SpeakResultMessage(message);
            StatusText = result.Status switch
            {
                CipherTaskStatus.Succeeded => "Completed",
                CipherTaskStatus.SucceededUnverified => "Completed — not independently verified",
                CipherTaskStatus.Cancelled => "Cancelled",
                CipherTaskStatus.Blocked => "Blocked by policy",
                _ => "Failed"
            };
            ProgressValue = result.Status is CipherTaskStatus.Succeeded or CipherTaskStatus.SucceededUnverified ? 100 : 0;
            AuditTaskOutcome(result);

            // An AI-suggested command is placed in the input box for explicit user review;
            // it only runs if the user presses Send (full policy + confirmation applies).
            if (result.ActionResult?.Data is { } resultData &&
                resultData.TryGetValue("suggestedCommand", out var suggestedCommand) &&
                !string.IsNullOrWhiteSpace(suggestedCommand))
            {
                Input = suggestedCommand;
                ProgressText = "Review the AI-suggested command in the input box, then press Send to run it. It has NOT run yet.";
            }
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
            AlwaysOnTop = AlwaysOnTop,
            SpeechOutputEnabled = SpeechOutputEnabled,
            AiDelegationEnabled = AiDelegationEnabled,
            AiBaseUrl = string.IsNullOrWhiteSpace(AiBaseUrl) ? _settings.AiBaseUrl : AiBaseUrl.Trim(),
            AiModel = string.IsNullOrWhiteSpace(AiModel) ? _settings.AiModel : AiModel.Trim()
        };

        await _settingsStore.SaveAsync(_settings).ConfigureAwait(true);
        WakePhrase = _settings.WakePhrase;
        AddConversation("CIPHER: Settings saved locally.");
        ProgressText = "Settings saved to the local CIPHER profile.";
    }

    /// <summary>
    /// Saves an API key from the settings password box into the DPAPI-protected store.
    /// Keys deliberately never travel through the command box (typed commands are journaled).
    /// </summary>
    public async Task SaveApiKeyAsync(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            ApiKeyStatus = "Type or paste a key first. It is stored DPAPI-encrypted; it is never shown or journaled.";
            return;
        }

        await Task.Run(() => _apiKeyStore.Save(apiKey)).ConfigureAwait(true);
        RefreshApiKeyStatus();
        Audit("security.api_key_saved", "An external AI API key was saved by the user (DPAPI-encrypted).");
        AddConversation("CIPHER: API key saved (DPAPI-encrypted on this PC).");
    }

    /// <summary>Best-effort audit append; auditing must never break command execution.</summary>
    private void Audit(string eventType, string details)
    {
        try
        {
            _auditLog.Append(eventType, details);
        }
        catch (Exception)
        {
            // The audit log is evidence, not a runtime dependency.
        }
    }

    private void AuditTaskOutcome(TaskExecutionResult result)
    {
        if (result.Status == CipherTaskStatus.Blocked)
        {
            Audit("task.blocked", $"{result.Command.Intent}: {result.Policy.Reason}");
        }
        else if (result.Status == CipherTaskStatus.Cancelled)
        {
            Audit("task.cancelled", $"{result.Command.Intent}: {result.ActionResult?.Message}");
        }

        if (result.Command.Intent == IntentKind.AskExternalAi)
        {
            var host = Uri.TryCreate(_settings.AiBaseUrl, UriKind.Absolute, out var uri) ? uri.Host : "invalid-endpoint";
            var artifact = result.ActionResult?.Data is { } data && data.TryGetValue("artifactPath", out var path)
                ? path
                : "none";
            Audit(
                result.ActionResult?.Success == true ? "delegation.completed" : "delegation.failed",
                $"endpoint={host} model={_settings.AiModel} artifact={artifact}");
        }
    }

    private void RefreshApiKeyStatus()
    {
        ApiKeyStatus = _apiKeyStore.HasKey
            ? "An API key is saved (DPAPI-encrypted). It is never displayed again."
            : "No API key saved yet.";
    }

    private string? LoadApiKeyOrNull()
    {
        try
        {
            return _apiKeyStore.Load();
        }
        catch (Exception)
        {
            // An unreadable key file behaves like "no key": delegation explains itself.
            return null;
        }
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
            if (global::CIPHER.App.App.IsSafeMode)
            {
                _wakeListeningEnabled = false;
                OnPropertyChanged(nameof(WakeListeningEnabled));
                AddConversation("CIPHER: Wake listening is disabled in safe mode (--safe-mode).");
                return;
            }

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
