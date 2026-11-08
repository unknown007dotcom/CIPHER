using System.Diagnostics;
using System.IO;
using CIPHER.Actions;
using CIPHER.Core;

namespace CIPHER.MilestoneTest;

/// <summary>
/// Interactive Windows acceptance harnesses. Two modes:
///
///   (default)  Stage 3 Notepad milestone — launch Notepad, identify its editor through
///              UI Automation, type a known sentence, switch windows, return, and verify
///              the sentence through UI Automation read-back.
///
///   stage4     Stage 4 named-element targeting — click named Calculator buttons and
///              confirm the display, then type into CIPHER's own named command field
///              and click its named Clear button. Usage:
///              CIPHER.MilestoneTest.exe stage4 --app &lt;path-to-CIPHER.exe&gt;
///
/// Exit codes: 0 = every scored step passed, 1 = a scored step failed,
/// 3 = everything was skipped (targets unavailable in this environment).
/// A harness refuses to type into a Notepad window that already contains user text.
/// </summary>
internal static class Program
{
    private const string Sentence = "CIPHER milestone verification sentence.";
    private static readonly List<(string Name, bool Passed)> Steps = new();
    private static readonly List<string> Skips = new();

    private static async Task<int> Main(string[] args)
    {
        if (args.Any(arg => string.Equals(arg, "stage4", StringComparison.OrdinalIgnoreCase)))
        {
            return await RunStage4Async(args);
        }

        return await RunMilestoneAsync();
    }

    private static async Task<int> RunMilestoneAsync()
    {
        Console.WriteLine("CIPHER Stage 3 Notepad milestone harness");
        Console.WriteLine($"Sentence under test: {Sentence}");
        Console.WriteLine();

        var windowService = new WindowService();
        var textService = new TextControlService();
        var engine = CreateEngine();

        try
        {
            // Precondition: never modify a Notepad document the user already has open.
            var existingWindow = windowService.FindWindow("Notepad");
            if (existingWindow is not null)
            {
                var existingText = textService.ReadWindowText(existingWindow);
                if (!string.IsNullOrWhiteSpace(existingText))
                {
                    Report("Precondition", false,
                        "Notepad already contains text. Refusing to modify an existing document.");
                    return FinishMilestone();
                }
            }

            // 1. Launch Notepad.
            var openResult = await engine.ExecuteTextAsync("open Notepad");
            Report("Launch Notepad",
                openResult.Status == CipherTaskStatus.Succeeded,
                openResult.ActionResult?.Message ?? openResult.Policy.Reason);
            await Task.Delay(800);

            // 2. Identify the editor through UI Automation.
            var notepadWindow = windowService.FindWindow("Notepad");
            var editor = notepadWindow is null ? null : textService.FindTextControl(notepadWindow);
            Report("Identify Notepad editor",
                editor is not null,
                editor is null
                    ? "No readable Edit/Document control was found."
                    : $"ControlType={editor.Current.ControlType.ProgrammaticName}, ClassName={editor.Current.ClassName}");
            if (notepadWindow is null || editor is null)
            {
                return FinishMilestone();
            }

            // 3. Focus Notepad.
            var switchResult = await engine.ExecuteTextAsync("switch to Notepad");
            Report("Focus Notepad",
                switchResult.Status == CipherTaskStatus.Succeeded,
                switchResult.ActionResult?.Message ?? switchResult.Policy.Reason);

            // 4. Type the sentence into the focused editor.
            var typeResult = await engine.ExecuteTextAsync($"type {Sentence}");
            Report("Type sentence",
                typeResult.ActionResult?.Success == true,
                $"{typeResult.ActionResult?.Message} (verified={typeResult.ActionResult?.Verified})");
            await Task.Delay(300);

            // 5. Read the editor back and confirm the sentence is present.
            notepadWindow = windowService.FindWindow("Notepad");
            var readBack = notepadWindow is null ? null : textService.ReadWindowText(notepadWindow);
            Report("Read back editor text",
                TextMatching.ContainsText(readBack, Sentence),
                readBack is null
                    ? "Editor text was not readable."
                    : $"Editor contains {TextMatching.NormalizeForComparison(readBack).Length} normalized character(s).");

            // 6. Switch away to File Explorer.
            var awayResult = await engine.ExecuteTextAsync("open File Explorer");
            Report("Switch away to File Explorer",
                awayResult.ActionResult?.Success == true,
                awayResult.ActionResult?.Message ?? awayResult.Policy.Reason);
            await Task.Delay(800);

            // 7. Switch back to Notepad.
            var backResult = await engine.ExecuteTextAsync("switch to Notepad");
            var foreground = windowService.GetForegroundWindowInfo();
            var foregroundIsNotepad =
                (foreground?.ProcessName.Contains("notepad", StringComparison.OrdinalIgnoreCase) ?? false) ||
                (foreground?.Title.Contains("Notepad", StringComparison.OrdinalIgnoreCase) ?? false);
            Report("Return to Notepad",
                backResult.Status == CipherTaskStatus.Succeeded && foregroundIsNotepad,
                $"Foreground: title='{foreground?.Title}', process='{foreground?.ProcessName}'.");

            // 8. Verify through the normal CIPHER command path.
            var verifyResult = await engine.ExecuteTextAsync($"verify Notepad contains {Sentence}");
            Report("Verify sentence via CIPHER command",
                verifyResult.Status == CipherTaskStatus.Succeeded,
                verifyResult.ActionResult?.Message ?? verifyResult.Policy.Reason);

            await CleanupMilestoneAsync(engine);
            return FinishMilestone();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"::error title=CIPHER Notepad milestone::Harness exception: {ex.Message}");
            Console.WriteLine($"MILESTONE RESULT: FAIL (exception: {ex.GetType().Name}: {ex.Message})");
            return 1;
        }
    }

    private static async Task<int> RunStage4Async(string[] args)
    {
        Console.WriteLine("CIPHER Stage 4 named-element targeting harness");
        Console.WriteLine();

        var windowService = new WindowService();
        var uiService = new UiElementService();
        var engine = CreateEngine();

        try
        {
            // Part A: click named Calculator buttons (Clear, 5, +, 3, =) and read the display.
            var openCalc = await engine.ExecuteTextAsync("open Calculator");
            await Task.Delay(1200);
            var calcWindow = windowService.FindWindow("Calculator");
            if (openCalc.ActionResult?.Success != true || calcWindow is null)
            {
                ReportSkip("Calculator button targeting",
                    $"Calculator could not be opened here: {openCalc.ActionResult?.Message}");
            }
            else
            {
                Report("Open Calculator", true, openCalc.ActionResult?.Message ?? string.Empty);

                // Calculator builds differ: newer ones name buttons "5"/"Add"/"3",
                // older ones "Five"/"Plus"/"Three". Try each spoken form in order.
                var buttons = new (string Label, string[] Candidates)[]
                {
                    ("Clear", new[] { "Clear" }),
                    ("Five", new[] { "Five", "5" }),
                    ("Plus", new[] { "Plus", "Add" }),
                    ("Three", new[] { "Three", "3" }),
                    ("Equals", new[] { "Equals" })
                };
                foreach (var (label, candidates) in buttons)
                {
                    var clicked = false;
                    var detail = "No candidate was tried.";
                    foreach (var candidate in candidates)
                    {
                        var click = await engine.ExecuteTextAsync($"click {candidate} button in Calculator");
                        detail = click.ActionResult?.Message ?? click.Policy.Reason;
                        if (click.ActionResult?.Success == true)
                        {
                            clicked = true;
                            detail = $"Clicked '{candidate}': {detail}";
                            break;
                        }
                    }

                    Report($"Click Calculator '{label}'", clicked, detail);
                }

                calcWindow = windowService.FindWindow("Calculator");
                var display = calcWindow is null
                    ? null
                    : uiService.FindElement(calcWindow, "CalculatorResults") ??
                      uiService.FindElement(calcWindow, "Display is");
                string? displayName = null;
                try
                {
                    displayName = display?.Element.Current.Name;
                }
                catch (Exception)
                {
                    displayName = null;
                }
                var displayOk = displayName is not null && TextMatching.ContainsText(displayName, "8");
                Report("Calculator display shows 8",
                    displayOk,
                    $"Display element name: '{displayName}'.");
                if (!displayOk && calcWindow is not null)
                {
                    var buttonNames = new List<string>();
                    try
                    {
                        var buttonElements = calcWindow.FindAll(
                            System.Windows.Automation.TreeScope.Descendants,
                            new System.Windows.Automation.PropertyCondition(
                                System.Windows.Automation.AutomationElement.ControlTypeProperty,
                                System.Windows.Automation.ControlType.Button));
                        foreach (System.Windows.Automation.AutomationElement button in buttonElements)
                        {
                            var buttonName = button.Current.Name;
                            if (!string.IsNullOrWhiteSpace(buttonName))
                            {
                                buttonNames.Add(buttonName);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        buttonNames.Add($"(enumeration failed: {ex.Message})");
                    }

                    Console.WriteLine(
                        $"::warning title=Calculator button names::{string.Join(" | ", buttonNames.Take(60))}");
                }

                var closeCalc = await engine.ExecuteTextAsync("close Calculator");
                Console.WriteLine($"CLEANUP: Calculator close={(closeCalc.ActionResult?.Success == true ? "OK" : "not completed")}.");
            }

            // Part B: type into CIPHER's own named command field and click its Clear button.
            var appPathArgument = GetArgumentValue(args, "--app");
            var appPath = string.IsNullOrWhiteSpace(appPathArgument)
                ? null
                : Path.GetFullPath(appPathArgument);
            if (appPath is null || !File.Exists(appPath))
            {
                ReportSkip("CIPHER field targeting",
                    $"CIPHER.exe was not found at '{appPath ?? "(no --app path supplied)"}'.");
            }
            else
            {
                Process? process;
                try
                {
                    process = Process.Start(new ProcessStartInfo
                    {
                        FileName = appPath,
                        WorkingDirectory = Path.GetDirectoryName(appPath),
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    Report("Launch CIPHER app", false, $"Could not start '{appPath}': {ex.Message}");
                    return FinishStage4();
                }

                using var startedProcess = process;

                var appWindow = await WaitForWindowAsync(windowService, "CIPHER", TimeSpan.FromSeconds(10));
                var appProcessCount = Process.GetProcessesByName("CIPHER").Length;
                var exited = startedProcess?.HasExited ?? true;
                var exitDetail = "n/a";
                if (exited && startedProcess is not null)
                {
                    try
                    {
                        exitDetail = startedProcess.ExitCode.ToString();
                    }
                    catch (Exception)
                    {
                        exitDetail = "unavailable";
                    }
                }

                Report("Launch CIPHER app", appWindow is not null,
                    $"Path: {appPath}; CIPHER processes running: {appProcessCount}; started process exited: {exited}; exit code: {exitDetail}.");

                if (appWindow is null)
                {
                    var errorLog = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "CIPHER", "logs", "app-errors.log");
                    if (File.Exists(errorLog))
                    {
                        var tail = File.ReadAllText(errorLog);
                        tail = tail.Length <= 1200 ? tail : tail[^1200..];
                        var escaped = tail.Replace("%", "%25").Replace("\r", "%0D").Replace("\n", "%0A");
                        Console.WriteLine($"::warning title=CIPHER app error log::{escaped}");
                    }
                }
                if (appWindow is not null)
                {
                    var typeInto = await engine.ExecuteTextAsync(
                        "type STAGE4-FIELD-OK into CIPHER command input in CIPHER");
                    Report("Type into 'CIPHER command input'",
                        typeInto.Status == CipherTaskStatus.Succeeded,
                        $"{typeInto.ActionResult?.Message} (verified={typeInto.ActionResult?.Verified})");

                    var clickClear = await engine.ExecuteTextAsync("click Clear button in CIPHER");
                    Report("Click CIPHER 'Clear' button",
                        clickClear.ActionResult?.Success == true,
                        clickClear.ActionResult?.Message ?? clickClear.Policy.Reason);

                    var closeApp = await engine.ExecuteTextAsync("close CIPHER");
                    Console.WriteLine($"CLEANUP: CIPHER close={(closeApp.ActionResult?.Success == true ? "OK" : "not completed")}.");
                }
            }

            return FinishStage4();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"::error title=CIPHER Stage 4 element targeting::Harness exception: {ex.Message}");
            Console.WriteLine($"STAGE4 RESULT: FAIL (exception: {ex.GetType().Name}: {ex.Message})");
            return 1;
        }
    }

    private static TaskEngine CreateEngine()
    {
        return new TaskEngine(
            new DeterministicCommandParser(),
            new DefaultActionPolicy(),
            new IActionExecutor[] { new BuiltInActionExecutor(), new WindowsActionExecutor() });
    }

    private static async Task<System.Windows.Automation.AutomationElement?> WaitForWindowAsync(
        WindowService windowService,
        string title,
        TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var window = windowService.FindWindow(title);
            if (window is not null)
            {
                return window;
            }

            await Task.Delay(250);
        }

        return null;
    }

    private static string? GetArgumentValue(string[] args, string name)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }

    private static async Task CleanupMilestoneAsync(TaskEngine engine)
    {
        try
        {
            await engine.ExecuteTextAsync("switch to Notepad");
            await engine.ExecuteTextAsync("press Ctrl+A");
            await engine.ExecuteTextAsync("press Delete");
            await Task.Delay(200);
            var closeNotepad = await engine.ExecuteTextAsync("close Notepad");
            var closeExplorer = await engine.ExecuteTextAsync("close File Explorer");
            Console.WriteLine(
                $"CLEANUP: Notepad close={(closeNotepad.ActionResult?.Success == true ? "OK" : "not completed")}; " +
                $"File Explorer close={(closeExplorer.ActionResult?.Success == true ? "OK" : "not completed")}.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"CLEANUP: not completed ({ex.GetType().Name}: {ex.Message}).");
        }
    }

    private static void Report(string name, bool passed, string details)
    {
        Steps.Add((name, passed));
        Console.WriteLine($"STEP: {name} => {(passed ? "PASS" : "FAIL")} - {details}");
        if (!passed)
        {
            // Surface the failure detail as a workflow annotation so it is visible
            // even when full CI logs cannot be downloaded.
            var escaped = details.Replace("%", "%25").Replace("\r", "%0D").Replace("\n", "%0A");
            Console.WriteLine($"::error title=CIPHER harness step '{name}'::{escaped}");
        }
    }

    private static void ReportSkip(string name, string reason)
    {
        Skips.Add($"{name}: {reason}");
        Console.WriteLine($"STEP: {name} => SKIP - {reason}");
    }

    private static int FinishMilestone()
    {
        var passedCount = Steps.Count(step => step.Passed);
        var allPassed = Steps.Count > 0 && passedCount == Steps.Count;
        Console.WriteLine();
        Console.WriteLine($"MILESTONE RESULT: {(allPassed ? "PASS" : "FAIL")} ({passedCount}/{Steps.Count} steps)");

        var failedNames = string.Join(", ", Steps.Where(step => !step.Passed).Select(step => step.Name));
        Console.WriteLine(allPassed
            ? "::notice title=CIPHER Notepad milestone::PASS - launch, editor identification, typing, window switching, and UI Automation read-back verification all succeeded."
            : $"::error title=CIPHER Notepad milestone::FAIL - failed steps: {failedNames}");

        return allPassed ? 0 : 1;
    }

    private static int FinishStage4()
    {
        var passedCount = Steps.Count(step => step.Passed);
        var anyScored = Steps.Count > 0;
        var allPassed = anyScored && passedCount == Steps.Count;
        var resultWord = !anyScored ? "SKIP" : allPassed ? "PASS" : "FAIL";

        Console.WriteLine();
        Console.WriteLine($"STAGE4 RESULT: {resultWord} ({passedCount}/{Steps.Count} scored steps, {Skips.Count} skipped)");

        if (!anyScored)
        {
            Console.WriteLine("::warning title=CIPHER Stage 4 element targeting::SKIP - no interactive targets were available in this environment.");
            return 3;
        }

        var failedNames = string.Join(", ", Steps.Where(step => !step.Passed).Select(step => step.Name));
        var skipNote = Skips.Count == 0 ? string.Empty : $" Skipped: {string.Join("; ", Skips)}";
        Console.WriteLine(allPassed
            ? $"::notice title=CIPHER Stage 4 element targeting::PASS - named button clicking and named field entry verified.{skipNote}"
            : $"::error title=CIPHER Stage 4 element targeting::FAIL - failed steps: {failedNames}.{skipNote}");

        return allPassed ? 0 : 1;
    }
}
