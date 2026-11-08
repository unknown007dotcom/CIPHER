using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Automation;
using CIPHER.Core;

namespace CIPHER.Actions;

/// <summary>
/// Windows desktop and file-system executor. It uses process APIs, UI Automation, and SendInput.
/// It never force-kills applications and moves deletes into a reversible local quarantine.
/// </summary>
public sealed class WindowsActionExecutor : IActionExecutor
{
    private readonly WindowService _windowService;
    private readonly InputService _inputService;
    private readonly TextControlService _textControlService;
    private readonly UiElementService _uiElementService;

    public WindowsActionExecutor()
        : this(new WindowService(), new InputService(), new TextControlService())
    {
    }

    public WindowsActionExecutor(
        WindowService windowService,
        InputService inputService,
        TextControlService? textControlService = null,
        UiElementService? uiElementService = null)
    {
        _windowService = windowService ?? throw new ArgumentNullException(nameof(windowService));
        _inputService = inputService ?? throw new ArgumentNullException(nameof(inputService));
        _textControlService = textControlService ?? new TextControlService();
        _uiElementService = uiElementService ?? new UiElementService(inputService);
    }

    public bool CanExecute(ParsedCommand command)
    {
        return command.Intent is
            IntentKind.OpenApplication or
            IntentKind.CloseApplication or
            IntentKind.SwitchWindow or
            IntentKind.TypeText or
            IntentKind.PressKeys or
            IntentKind.ReadWindowText or
            IntentKind.VerifyWindowText or
            IntentKind.ClickUiElement or
            IntentKind.TypeIntoUiElement or
            IntentKind.OpenPath or
            IntentKind.CreateFolder or
            IntentKind.CreateFile or
            IntentKind.ListDirectory or
            IntentKind.DeletePath or
            IntentKind.MovePath or
            IntentKind.CopyPath or
            IntentKind.RenamePath;
    }

    public async Task<ActionResult> ExecuteAsync(
        ParsedCommand command,
        CommandContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            return await Task.Run(() => Execute(command, context, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            return ActionResult.Fail($"Windows could not complete the action: {ex.Message}", "WINDOWS_ACTION_FAILED");
        }
    }

    private ActionResult Execute(ParsedCommand command, CommandContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return command.Intent switch
        {
            IntentKind.OpenApplication => OpenApplication(command, context, cancellationToken),
            IntentKind.CloseApplication => CloseApplication(command, cancellationToken),
            IntentKind.SwitchWindow => SwitchWindow(command),
            IntentKind.TypeText => TypeText(command),
            IntentKind.PressKeys => PressKeys(command),
            IntentKind.ReadWindowText => ReadWindowText(command),
            IntentKind.VerifyWindowText => VerifyWindowText(command),
            IntentKind.ClickUiElement => ClickUiElement(command),
            IntentKind.TypeIntoUiElement => TypeIntoUiElement(command),
            IntentKind.OpenPath => OpenPath(command, context),
            IntentKind.CreateFolder => CreateFolder(command, context),
            IntentKind.CreateFile => CreateFile(command, context),
            IntentKind.ListDirectory => ListDirectory(command, context),
            IntentKind.DeletePath => DeletePath(command, context),
            IntentKind.MovePath => MovePath(command, context, isRename: false),
            IntentKind.RenamePath => MovePath(command, context, isRename: true),
            IntentKind.CopyPath => CopyPath(command, context, cancellationToken),
            _ => ActionResult.Fail($"The Windows executor does not handle '{command.Intent}'.", "UNSUPPORTED_INTENT")
        };
    }

    private ActionResult OpenApplication(ParsedCommand command, CommandContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Target))
        {
            return ActionResult.Fail("No application was specified.", "APPLICATION_NOT_SPECIFIED");
        }

        var known = ApplicationCatalog.Resolve(command.Target);
        var launchTarget = known?.LaunchTarget ?? PathSafety.Unquote(command.Target);

        using var startedProcess = Process.Start(new ProcessStartInfo
        {
            FileName = launchTarget,
            UseShellExecute = true,
            WorkingDirectory = context.WorkingDirectory
        });

        if (!string.IsNullOrWhiteSpace(known?.ProcessName))
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
            while (DateTimeOffset.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var processes = Process.GetProcessesByName(known.ProcessName);
                try
                {
                    if (processes.Length > 0)
                    {
                        return ActionResult.Ok(
                            $"Opened {known.DisplayName}.",
                            verified: true,
                            data: new Dictionary<string, string>
                            {
                                ["processName"] = known.ProcessName,
                                ["processCount"] = processes.Length.ToString()
                            });
                    }
                }
                finally
                {
                    foreach (var process in processes)
                    {
                        process.Dispose();
                    }
                }

                Thread.Sleep(100);
            }
        }

        if (startedProcess is not null)
        {
            return ActionResult.Ok(
                $"Launch requested for {known?.DisplayName ?? command.Target}.",
                verified: false,
                data: new Dictionary<string, string> { ["launchTarget"] = launchTarget });
        }

        return ActionResult.Fail($"Windows did not start '{command.Target}'.", "APPLICATION_START_FAILED");
    }

    private ActionResult CloseApplication(ParsedCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Target))
        {
            return ActionResult.Fail("No application was specified.", "APPLICATION_NOT_SPECIFIED");
        }

        var processes = FindProcesses(command.Target);
        if (processes.Count == 0)
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }

            return ActionResult.Fail($"'{command.Target}' is not currently running.", "APPLICATION_NOT_RUNNING");
        }

        var closeRequested = 0;
        foreach (var process in processes)
        {
            try
            {
                if (process.MainWindowHandle != IntPtr.Zero && process.CloseMainWindow())
                {
                    closeRequested++;
                }
            }
            catch (InvalidOperationException)
            {
                // The process exited while it was being inspected.
            }
        }

        if (closeRequested == 0)
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }

            return ActionResult.Fail(
                $"'{command.Target}' has no closable main window. CIPHER did not force-kill it.",
                "CLOSE_NOT_COMPLETED");
        }

        var deadline = DateTimeOffset.UtcNow.AddSeconds(4);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (processes.All(process =>
            {
                try
                {
                    return process.HasExited;
                }
                catch (InvalidOperationException)
                {
                    return true;
                }
            }))
            {
                foreach (var process in processes)
                {
                    process.Dispose();
                }

                return ActionResult.Ok($"Closed {command.Target}.", verified: true);
            }

            Thread.Sleep(100);
        }

        foreach (var process in processes)
        {
            process.Dispose();
        }

        return ActionResult.Fail(
            $"A graceful close was requested for '{command.Target}', but it is still running. CIPHER did not force-kill it.",
            "CLOSE_NOT_COMPLETED");
    }

    private List<Process> FindProcesses(string target)
    {
        var known = ApplicationCatalog.Resolve(target);
        var processName = known?.ProcessName;
        if (string.IsNullOrWhiteSpace(processName))
        {
            processName = Path.GetFileNameWithoutExtension(PathSafety.Unquote(target)).Replace(" ", string.Empty);
        }

        var processes = string.IsNullOrWhiteSpace(processName)
            ? new List<Process>()
            : Process.GetProcessesByName(processName).ToList();

        if (processes.Count > 0)
        {
            return processes;
        }

        var window = _windowService.FindWindow(target);
        if (window is null)
        {
            return processes;
        }

        try
        {
            var process = Process.GetProcessById(window.Current.ProcessId);
            processes.Add(process);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // Fall through with no matching process.
        }

        return processes;
    }

    private ActionResult SwitchWindow(ParsedCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Target))
        {
            return ActionResult.Fail("No window was specified.", "WINDOW_NOT_SPECIFIED");
        }

        var window = _windowService.FindWindow(command.Target);
        if (window is null)
        {
            return ActionResult.Fail($"No visible window matching '{command.Target}' was found.", "WINDOW_NOT_FOUND");
        }

        var title = window.Current.Name;
        var focused = _windowService.Focus(window);
        return ActionResult.Ok(
            focused ? $"Switched to '{title}'." : $"Focus was requested for '{title}', but Windows did not confirm foreground activation.",
            verified: focused,
            data: new Dictionary<string, string> { ["windowTitle"] = title });
    }

    private ActionResult TypeText(ParsedCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Argument))
        {
            return ActionResult.Fail("No text was specified.", "TEXT_NOT_SPECIFIED");
        }

        var foregroundBefore = _windowService.GetForegroundElement();
        var textBefore = foregroundBefore is null ? null : _textControlService.ReadWindowText(foregroundBefore);

        _inputService.TypeText(command.Argument);
        Thread.Sleep(150);

        // Verify by reading the focused window's editor back through UI Automation.
        // Typing is only reported as verified when the typed text is actually observed.
        var foregroundAfter = _windowService.GetForegroundElement();
        var textAfter = foregroundAfter is null ? null : _textControlService.ReadWindowText(foregroundAfter);

        var occurrencesAfter = TextMatching.CountOccurrences(textAfter, command.Argument);
        var verified = textAfter is not null &&
                       TextMatching.ContainsText(textAfter, command.Argument) &&
                       (textBefore is null || occurrencesAfter > TextMatching.CountOccurrences(textBefore, command.Argument));

        return ActionResult.Ok(
            verified
                ? "Text was typed and verified in the focused editor."
                : "Text was sent to the currently focused control.",
            verified: verified,
            data: new Dictionary<string, string> { ["characterCount"] = command.Argument.Length.ToString() });
    }

    private ActionResult ReadWindowText(ParsedCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Target))
        {
            return ActionResult.Fail("No window was specified.", "WINDOW_NOT_SPECIFIED");
        }

        var window = _windowService.FindWindow(command.Target);
        if (window is null)
        {
            return ActionResult.Fail($"No visible window matching '{command.Target}' was found.", "WINDOW_NOT_FOUND");
        }

        var title = window.Current.Name;
        var text = _textControlService.ReadWindowText(window);
        if (text is null)
        {
            return ActionResult.Fail(
                $"CIPHER found '{title}' but its text content is not readable through UI Automation.",
                "TEXT_NOT_READABLE");
        }

        var displayText = text.Length <= 500 ? text : text[..500] + "…";
        var message = text.Length == 0
            ? $"The editor in '{title}' is empty."
            : $"Text in '{title}': {displayText}";

        return ActionResult.Ok(
            message,
            verified: true,
            data: new Dictionary<string, string>
            {
                ["windowTitle"] = title,
                ["characterCount"] = text.Length.ToString(),
                ["text"] = text.Length <= 4_000 ? text : text[..4_000]
            });
    }

    private ActionResult VerifyWindowText(ParsedCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Target))
        {
            return ActionResult.Fail("No window was specified.", "WINDOW_NOT_SPECIFIED");
        }

        if (string.IsNullOrWhiteSpace(command.Argument))
        {
            return ActionResult.Fail("No expected text was specified.", "TEXT_NOT_SPECIFIED");
        }

        var window = _windowService.FindWindow(command.Target);
        if (window is null)
        {
            return ActionResult.Fail($"No visible window matching '{command.Target}' was found.", "WINDOW_NOT_FOUND");
        }

        var title = window.Current.Name;
        var text = _textControlService.ReadWindowText(window);
        if (text is null)
        {
            return ActionResult.Fail(
                $"CIPHER found '{title}' but its text content is not readable through UI Automation, so the text could not be verified.",
                "TEXT_NOT_READABLE");
        }

        if (TextMatching.ContainsText(text, command.Argument))
        {
            return ActionResult.Ok(
                $"Verified: '{title}' contains the expected text.",
                verified: true,
                data: new Dictionary<string, string> { ["windowTitle"] = title });
        }

        var preview = text.Length <= 200 ? text : text[..200] + "…";
        return ActionResult.Fail(
            $"Verification failed: '{title}' does not contain the expected text. Current content begins: {preview}",
            "TEXT_NOT_FOUND");
    }

    private ActionResult PressKeys(ParsedCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Argument))
        {
            return ActionResult.Fail("No keyboard shortcut was specified.", "KEYS_NOT_SPECIFIED");
        }

        _inputService.PressKeys(command.Argument);
        return ActionResult.Ok($"Pressed {command.Argument}.", verified: false);
    }

    private ActionResult ClickUiElement(ParsedCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Target))
        {
            return ActionResult.Fail("No control name was specified.", "ELEMENT_NOT_SPECIFIED");
        }

        var scope = ResolveScopeWindow(command, out var scopeError);
        if (scope is null)
        {
            return scopeError!;
        }

        var match = _uiElementService.FindElement(scope, command.Target);
        if (match is null)
        {
            return ActionResult.Fail(
                $"No control named '{command.Target}' was found in '{scope.Current.Name}'.",
                "ELEMENT_NOT_FOUND");
        }

        var element = match.Element;
        if (!element.Current.IsEnabled)
        {
            return ActionResult.Fail(
                $"The control '{command.Target}' in '{scope.Current.Name}' is disabled.",
                "ELEMENT_DISABLED");
        }

        var stateBefore = UiElementService.ReadState(element);
        var activation = _uiElementService.Activate(element);
        if (!activation.Activated)
        {
            return ActionResult.Fail(
                $"The control '{command.Target}' does not expose an activation pattern or clickable point.",
                "ELEMENT_NOT_ACTIONABLE");
        }

        // A click is only "verified" when an observable state (toggle/selection/expansion)
        // actually changed; otherwise the click was dispatched but its effect is unknown.
        var verified = stateBefore is not null &&
                       activation.StateAfter is not null &&
                       !string.Equals(stateBefore, activation.StateAfter, StringComparison.Ordinal);

        return ActionResult.Ok(
            verified
                ? $"Clicked '{command.Target}' in '{scope.Current.Name}' and verified its state changed ({stateBefore} → {activation.StateAfter})."
                : $"Clicked '{command.Target}' in '{scope.Current.Name}' via {activation.Method}.",
            verified: verified,
            data: new Dictionary<string, string>
            {
                ["elementName"] = command.Target,
                ["matchedBy"] = match.MatchedBy,
                ["activationMethod"] = activation.Method,
                ["windowTitle"] = scope.Current.Name
            });
    }

    private ActionResult TypeIntoUiElement(ParsedCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Target))
        {
            return ActionResult.Fail("No field name was specified.", "ELEMENT_NOT_SPECIFIED");
        }

        if (command.Argument is null)
        {
            return ActionResult.Fail("No text was specified.", "TEXT_NOT_SPECIFIED");
        }

        var scope = ResolveScopeWindow(command, out var scopeError);
        if (scope is null)
        {
            return scopeError!;
        }

        var match = _uiElementService.FindElement(scope, command.Target, preferTextEntry: true);
        if (match is null)
        {
            return ActionResult.Fail(
                $"No field named '{command.Target}' was found in '{scope.Current.Name}'.",
                "ELEMENT_NOT_FOUND");
        }

        var element = match.Element;
        if (element.Current.IsPassword)
        {
            return ActionResult.Fail(
                "CIPHER does not type into password fields. Enter the value yourself.",
                "CREDENTIAL_FIELD");
        }

        if (!element.Current.IsEnabled)
        {
            return ActionResult.Fail(
                $"The field '{command.Target}' in '{scope.Current.Name}' is disabled.",
                "ELEMENT_DISABLED");
        }

        // Preferred path: set the value semantically, then read it back.
        if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var valueObject) &&
            valueObject is ValuePattern valuePattern &&
            !valuePattern.Current.IsReadOnly)
        {
            valuePattern.SetValue(command.Argument);
            Thread.Sleep(100);
            var readBack = valuePattern.Current.Value;
            var verified = string.Equals(
                TextMatching.NormalizeForComparison(readBack),
                TextMatching.NormalizeForComparison(command.Argument),
                StringComparison.Ordinal);

            return ActionResult.Ok(
                verified
                    ? $"Text was entered into '{command.Target}' in '{scope.Current.Name}' and verified."
                    : $"Text was entered into '{command.Target}' in '{scope.Current.Name}', but the read-back differs.",
                verified: verified,
                data: new Dictionary<string, string>
                {
                    ["elementName"] = command.Target,
                    ["matchedBy"] = match.MatchedBy,
                    ["windowTitle"] = scope.Current.Name
                });
        }

        // Fallback path: focus the field, select existing content, type, and read back.
        try
        {
            element.SetFocus();
        }
        catch (InvalidOperationException)
        {
            return ActionResult.Fail(
                $"The field '{command.Target}' cannot receive focus.",
                "ELEMENT_NOT_FOCUSABLE");
        }

        Thread.Sleep(100);
        _inputService.PressKeys("Ctrl+A");
        _inputService.PressKeys("Delete");
        _inputService.TypeText(command.Argument);
        Thread.Sleep(150);

        var textAfter = _textControlService.ReadText(element);
        var keyboardVerified = TextMatching.ContainsText(textAfter, command.Argument);
        return ActionResult.Ok(
            keyboardVerified
                ? $"Text was typed into '{command.Target}' in '{scope.Current.Name}' and verified."
                : $"Text was typed into '{command.Target}' in '{scope.Current.Name}'.",
            verified: keyboardVerified,
            data: new Dictionary<string, string>
            {
                ["elementName"] = command.Target,
                ["matchedBy"] = match.MatchedBy,
                ["windowTitle"] = scope.Current.Name
            });
    }

    /// <summary>
    /// Resolves the window an element command applies to: the explicitly named window
    /// (which is focused first), or the current foreground window.
    /// </summary>
    private AutomationElement? ResolveScopeWindow(
        ParsedCommand command,
        out ActionResult? error)
    {
        error = null;

        if (!string.IsNullOrWhiteSpace(command.SecondaryTarget))
        {
            var window = _windowService.FindWindow(command.SecondaryTarget);
            if (window is null)
            {
                error = ActionResult.Fail(
                    $"No visible window matching '{command.SecondaryTarget}' was found.",
                    "WINDOW_NOT_FOUND");
                return null;
            }

            _windowService.Focus(window);
            return window;
        }

        var foreground = _windowService.GetForegroundElement();
        if (foreground is null)
        {
            error = ActionResult.Fail(
                "No window was specified and no foreground window could be identified.",
                "WINDOW_NOT_FOUND");
        }

        return foreground;
    }

    private ActionResult OpenPath(ParsedCommand command, CommandContext context)
    {
        if (string.IsNullOrWhiteSpace(command.Target))
        {
            return ActionResult.Fail("No path was specified.", "PATH_NOT_SPECIFIED");
        }

        var target = PathSafety.Unquote(command.Target);
        var isUri = Uri.TryCreate(target, UriKind.Absolute, out var uri) && !uri.IsFile;
        var fullPath = isUri ? target : NormalizePath(target, context);

        if (!isUri && !File.Exists(fullPath) && !Directory.Exists(fullPath))
        {
            return ActionResult.Fail($"The path '{fullPath}' does not exist.", "PATH_NOT_FOUND");
        }

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = fullPath,
            UseShellExecute = true,
            WorkingDirectory = context.WorkingDirectory
        });

        return ActionResult.Ok($"Open requested for {fullPath}.", verified: false);
    }

    private ActionResult CreateFolder(ParsedCommand command, CommandContext context)
    {
        if (string.IsNullOrWhiteSpace(command.Target))
        {
            return ActionResult.Fail("No folder path was specified.", "PATH_NOT_SPECIFIED");
        }

        var path = NormalizePath(command.Target, context);
        if (!string.IsNullOrWhiteSpace(command.Argument))
        {
            path = Path.Combine(path, PathSafety.Unquote(command.Argument));
        }

        Directory.CreateDirectory(path);
        return ActionResult.Ok(
            $"Folder is ready at {path}.",
            verified: Directory.Exists(path),
            data: new Dictionary<string, string> { ["path"] = path });
    }

    private ActionResult CreateFile(ParsedCommand command, CommandContext context)
    {
        if (string.IsNullOrWhiteSpace(command.Target))
        {
            return ActionResult.Fail("No file path was specified.", "PATH_NOT_SPECIFIED");
        }

        var path = NormalizePath(command.Target, context);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(path))
        {
            return ActionResult.Fail($"The file '{path}' already exists. CIPHER did not overwrite it.", "FILE_ALREADY_EXISTS");
        }

        File.WriteAllBytes(path, Array.Empty<byte>());
        return ActionResult.Ok(
            $"Created empty file at {path}.",
            verified: File.Exists(path),
            data: new Dictionary<string, string> { ["path"] = path });
    }

    private ActionResult ListDirectory(ParsedCommand command, CommandContext context)
    {
        var path = string.IsNullOrWhiteSpace(command.Target)
            ? context.WorkingDirectory
            : NormalizePath(command.Target, context);

        if (!Directory.Exists(path))
        {
            return ActionResult.Fail($"The directory '{path}' does not exist.", "DIRECTORY_NOT_FOUND");
        }

        var entries = Directory
            .EnumerateFileSystemEntries(path)
            .Take(201)
            .Select(entry =>
            {
                var attributes = File.GetAttributes(entry);
                var kind = attributes.HasFlag(FileAttributes.Directory) ? "Folder" : "File";
                return $"{kind}: {Path.GetFileName(entry)}";
            })
            .ToList();

        var truncated = entries.Count > 200;
        if (truncated)
        {
            entries = entries.Take(200).ToList();
        }

        var message = entries.Count == 0
            ? $"The folder {path} is empty."
            : $"Found {entries.Count}{(truncated ? "+ (showing first 200)" : string.Empty)} item(s) in {path}: {string.Join(", ", entries)}";

        return ActionResult.Ok(
            message,
            verified: true,
            data: new Dictionary<string, string>
            {
                ["path"] = path,
                ["entries"] = string.Join(Environment.NewLine, entries),
                ["displayedCount"] = entries.Count.ToString(),
                ["truncated"] = truncated.ToString()
            });
    }

    private ActionResult DeletePath(ParsedCommand command, CommandContext context)
    {
        if (string.IsNullOrWhiteSpace(command.Target))
        {
            return ActionResult.Fail("No path was specified.", "PATH_NOT_SPECIFIED");
        }

        var source = NormalizePath(command.Target, context);
        if (!File.Exists(source) && !Directory.Exists(source))
        {
            return ActionResult.Fail($"The path '{source}' does not exist.", "PATH_NOT_FOUND");
        }

        var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var quarantineRoot = Path.Combine(localApplicationData, "CIPHER", "Quarantine");
        if (PathSafety.IsSameOrDescendant(source, quarantineRoot))
        {
            return ActionResult.Fail("Items already inside CIPHER quarantine cannot be deleted through this command.", "PATH_PROTECTED");
        }

        Directory.CreateDirectory(quarantineRoot);
        var originalName = Path.GetFileName(source.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(originalName))
        {
            return ActionResult.Fail("The path does not have a valid file or folder name.", "PATH_INVALID");
        }

        var destination = Path.Combine(
            quarantineRoot,
            $"{DateTime.Now:yyyyMMdd-HHmmssfff}-{SanitizeFileName(originalName)}");

        if (File.Exists(source))
        {
            File.Move(source, destination);
        }
        else
        {
            Directory.Move(source, destination);
        }

        var verified = !File.Exists(source) && !Directory.Exists(source) &&
                       (File.Exists(destination) || Directory.Exists(destination));

        return ActionResult.Ok(
            $"Moved '{source}' to CIPHER quarantine at '{destination}'. This is reversible and is not permanent deletion.",
            verified: verified,
            data: new Dictionary<string, string>
            {
                ["source"] = source,
                ["quarantinePath"] = destination
            });
    }

    private ActionResult MovePath(ParsedCommand command, CommandContext context, bool isRename)
    {
        if (string.IsNullOrWhiteSpace(command.Target) || string.IsNullOrWhiteSpace(command.SecondaryTarget))
        {
            return ActionResult.Fail("Both a source and destination are required.", "PATH_NOT_SPECIFIED");
        }

        var source = NormalizePath(command.Target, context);
        if (!File.Exists(source) && !Directory.Exists(source))
        {
            return ActionResult.Fail($"The source path '{source}' does not exist.", "PATH_NOT_FOUND");
        }

        var destination = ResolveDestination(source, command.SecondaryTarget, context);
        if (File.Exists(destination) || Directory.Exists(destination))
        {
            return ActionResult.Fail($"The destination '{destination}' already exists. CIPHER did not overwrite it.", "DESTINATION_EXISTS");
        }

        var parent = Path.GetDirectoryName(destination);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        if (File.Exists(source))
        {
            File.Move(source, destination);
        }
        else
        {
            Directory.Move(source, destination);
        }

        var verified = !File.Exists(source) && !Directory.Exists(source) &&
                       (File.Exists(destination) || Directory.Exists(destination));
        var verb = isRename ? "Renamed" : "Moved";

        return ActionResult.Ok(
            $"{verb} '{source}' to '{destination}'.",
            verified: verified,
            data: new Dictionary<string, string>
            {
                ["source"] = source,
                ["destination"] = destination
            });
    }

    private ActionResult CopyPath(ParsedCommand command, CommandContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Target) || string.IsNullOrWhiteSpace(command.SecondaryTarget))
        {
            return ActionResult.Fail("Both a source and destination are required.", "PATH_NOT_SPECIFIED");
        }

        var source = NormalizePath(command.Target, context);
        if (!File.Exists(source) && !Directory.Exists(source))
        {
            return ActionResult.Fail($"The source path '{source}' does not exist.", "PATH_NOT_FOUND");
        }

        var destination = ResolveDestination(source, command.SecondaryTarget, context);
        if (File.Exists(destination) || Directory.Exists(destination))
        {
            return ActionResult.Fail($"The destination '{destination}' already exists. CIPHER did not overwrite it.", "DESTINATION_EXISTS");
        }

        var parent = Path.GetDirectoryName(destination);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        if (File.Exists(source))
        {
            File.Copy(source, destination, overwrite: false);
        }
        else
        {
            CopyDirectory(source, destination, cancellationToken);
        }

        return ActionResult.Ok(
            $"Copied '{source}' to '{destination}'.",
            verified: File.Exists(destination) || Directory.Exists(destination),
            data: new Dictionary<string, string>
            {
                ["source"] = source,
                ["destination"] = destination
            });
    }

    private static string ResolveDestination(string source, string destinationText, CommandContext context)
    {
        var unquoted = PathSafety.Unquote(destinationText);
        string destination;

        if (!Path.IsPathFullyQualified(unquoted) &&
            string.IsNullOrEmpty(Path.GetDirectoryName(unquoted)))
        {
            var sourceDirectory = Path.GetDirectoryName(source) ?? context.WorkingDirectory;
            destination = Path.Combine(sourceDirectory, unquoted);
        }
        else
        {
            destination = NormalizePath(unquoted, context);
        }

        if (Directory.Exists(destination))
        {
            var sourceName = Path.GetFileName(source.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            destination = Path.Combine(destination, sourceName);
        }

        return destination;
    }

    private static void CopyDirectory(string source, string destination, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destination);

        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(source, directory);
            Directory.CreateDirectory(Path.Combine(destination, relative));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            var parent = Path.GetDirectoryName(target);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                Directory.CreateDirectory(parent);
            }

            File.Copy(file, target, overwrite: false);
        }
    }

    private static string NormalizePath(string path, CommandContext context)
    {
        return PathSafety.NormalizePath(path, context.WorkingDirectory);
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var characters = value.Select(character => invalid.Contains(character) ? '_' : character).ToArray();
        return new string(characters);
    }
}
