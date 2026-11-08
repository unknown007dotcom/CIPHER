using CIPHER.Actions;
using CIPHER.Core;

namespace CIPHER.MilestoneTest;

/// <summary>
/// Interactive Windows milestone harness for Stage 3. It performs the exact acceptance
/// scenario from the CIPHER plan against the real desktop:
///
///   1. Launch Notepad.
///   2. Identify its editor through UI Automation.
///   3. Type a known sentence.
///   4. Switch to another window.
///   5. Return to Notepad.
///   6. Verify through UI Automation read-back that the sentence is still present.
///
/// Exit code 0 means every scored step passed. The harness refuses to type into a
/// Notepad window that already contains text, so it cannot destroy user content.
/// </summary>
internal static class Program
{
    private const string Sentence = "CIPHER milestone verification sentence.";
    private static readonly List<(string Name, bool Passed)> Steps = new();

    private static async Task<int> Main()
    {
        Console.WriteLine("CIPHER Stage 3 Notepad milestone harness");
        Console.WriteLine($"Sentence under test: {Sentence}");
        Console.WriteLine();

        var windowService = new WindowService();
        var textService = new TextControlService();
        var engine = new TaskEngine(
            new DeterministicCommandParser(),
            new DefaultActionPolicy(),
            new IActionExecutor[] { new BuiltInActionExecutor(), new WindowsActionExecutor() });

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
                    return Finish();
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
                return Finish();
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

            await CleanupAsync(engine);
            return Finish();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"::error title=CIPHER Notepad milestone::Harness exception: {ex.Message}");
            Console.WriteLine($"MILESTONE RESULT: FAIL (exception: {ex.GetType().Name}: {ex.Message})");
            return 1;
        }
    }

    private static async Task CleanupAsync(TaskEngine engine)
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
    }

    private static int Finish()
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
}
