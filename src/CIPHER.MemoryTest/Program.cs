using CIPHER.Core;
using CIPHER.Memory;

namespace CIPHER.MemoryTest;

/// <summary>
/// Stage 7 kill/restart harness, run as two separate processes against one database:
///
///   --write  --db &lt;path&gt;  journals a task as Running, saves an alias, an app path, and
///                            a history entry, then exits WITHOUT completing the task
///                            (the simulated kill).
///   --verify --db &lt;path&gt;  a fresh process must find the interrupted task, the alias,
///                            the app path, and the history; it then marks the task
///                            Interrupted (never re-executed) and confirms recovery.
///
/// Exit codes: 0 = ok, 1 = assertion failure, 2 = bad arguments.
/// </summary>
internal static class Program
{
    private const string ProbeTaskText = "stage7 recovery probe: open notepad";
    private const string ProbeAliasName = "probe alias";
    private const string ProbeAliasExpansion = "help";
    private const string ProbeAppName = "probe app";
    private const string ProbeAppPath = @"C:\Tools\probe.exe";
    private const string ProbeHistoryText = "probe history entry";

    private static async Task<int> Main(string[] args)
    {
        var mode = args.Length > 0 ? args[0] : string.Empty;
        var databasePath = GetOption(args, "--db");
        if (string.IsNullOrWhiteSpace(databasePath) || (mode is not ("--write" or "--verify")))
        {
            Console.WriteLine("::error title=Stage 7 memory harness::Usage: CIPHER.MemoryTest --write|--verify --db <path>");
            return 2;
        }

        try
        {
            return mode == "--write"
                ? await WritePhaseAsync(databasePath).ConfigureAwait(false)
                : await VerifyPhaseAsync(databasePath).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"::error title=Stage 7 memory harness::STAGE7-FAIL {mode} phase threw: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> WritePhaseAsync(string databasePath)
    {
        if (File.Exists(databasePath))
        {
            File.Delete(databasePath);
        }

        using var store = new SqliteMemoryStore(databasePath);
        await store.InitializeAsync().ConfigureAwait(false);
        var record = await store.BeginTaskAsync(ProbeTaskText).ConfigureAwait(false);
        await store.SetAliasAsync(ProbeAliasName, ProbeAliasExpansion).ConfigureAwait(false);
        await store.SetAppPathAsync(ProbeAppName, ProbeAppPath).ConfigureAwait(false);
        await store.AddHistoryAsync(ProbeHistoryText, "Succeeded", "probe").ConfigureAwait(false);

        Console.WriteLine($"STAGE7-WRITE-OK task {record.Id} journaled as Running in {databasePath}; exiting without completing it (simulated kill).");
        return 0;
    }

    private static async Task<int> VerifyPhaseAsync(string databasePath)
    {
        using var store = new SqliteMemoryStore(databasePath);
        await store.InitializeAsync().ConfigureAwait(false);

        Require(!store.RecoveredFromCorruption, "The probe database was unexpectedly quarantined as corrupt.");

        var incomplete = await store.GetIncompleteTasksAsync().ConfigureAwait(false);
        var recovered = incomplete.FirstOrDefault(task => task.InputText == ProbeTaskText);
        Require(recovered is not null, "The interrupted probe task was not recovered after restart.");

        var expansion = await store.GetAliasExpansionAsync("PROBE ALIAS").ConfigureAwait(false);
        Require(expansion == ProbeAliasExpansion, $"Alias did not survive restart (got '{expansion}').");

        var appPath = await store.GetAppPathAsync(ProbeAppName).ConfigureAwait(false);
        Require(appPath == ProbeAppPath, $"Learned app path did not survive restart (got '{appPath}').");

        var history = await store.GetRecentHistoryAsync(10).ConfigureAwait(false);
        Require(history.Any(entry => entry.InputText == ProbeHistoryText), "History entry did not survive restart.");

        await store.CompleteTaskAsync(recovered!.Id, TaskRecordStatus.Interrupted, "Recovered by Stage 7 harness; not re-executed.").ConfigureAwait(false);
        var remaining = await store.GetIncompleteTasksAsync().ConfigureAwait(false);
        Require(remaining.All(task => task.InputText != ProbeTaskText), "Probe task is still marked incomplete after recovery.");

        Console.WriteLine("::notice title=Stage 7 memory harness::STAGE7-PASS interrupted task recovered and marked Interrupted (not re-executed); alias, app path, and history persisted across processes.");
        return 0;
    }

    private static string? GetOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
