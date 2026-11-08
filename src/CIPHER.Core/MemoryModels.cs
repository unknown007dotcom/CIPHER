namespace CIPHER.Core;

/// <summary>Status values stored on <see cref="TaskRecord"/> rows.</summary>
public static class TaskRecordStatus
{
    /// <summary>The task started but has not reached a terminal state yet.</summary>
    public const string Running = "Running";

    /// <summary>The process ended while the task was running; it was not resumed automatically.</summary>
    public const string Interrupted = "Interrupted";
}

/// <summary>
/// One journaled task execution. A record left in <see cref="TaskRecordStatus.Running"/>
/// after a restart means the previous process ended mid-task (crash/kill), which is the
/// recovery signal — such tasks are reported, never silently re-executed.
/// </summary>
public sealed record TaskRecord(
    Guid Id,
    string InputText,
    string Status,
    string Message,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc);

/// <summary>One completed command kept in the local history.</summary>
public sealed record CommandHistoryEntry(
    long Id,
    string InputText,
    string Status,
    string Message,
    DateTimeOffset ExecutedUtc);

/// <summary>A user-taught phrase that expands to a full command.</summary>
public sealed record CommandAlias(
    string Name,
    string Expansion,
    DateTimeOffset UpdatedUtc);

/// <summary>A user-taught application location layered over the built-in catalog.</summary>
public sealed record LearnedAppPath(
    string Name,
    string Path,
    DateTimeOffset UpdatedUtc);

/// <summary>
/// Persistent, fully local memory: task journal (recovery), command history, user aliases,
/// and learned application paths. Implementations must never store passwords, cookies, or
/// credentials — command text that the policy blocks is never executed, and sensitive
/// command classes should not be journaled with secrets by callers.
/// </summary>
public interface IMemoryStore
{
    /// <summary>Creates/migrates the schema. Safe to call repeatedly and after crashes.</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>Journals a task as Running and returns its record.</summary>
    Task<TaskRecord> BeginTaskAsync(string inputText, CancellationToken cancellationToken = default);

    /// <summary>Moves a task to a terminal (or Interrupted) status with a result message.</summary>
    Task CompleteTaskAsync(Guid taskId, string status, string message, CancellationToken cancellationToken = default);

    /// <summary>Tasks still marked Running — the interrupted-work recovery list after a restart.</summary>
    Task<IReadOnlyList<TaskRecord>> GetIncompleteTasksAsync(CancellationToken cancellationToken = default);

    Task AddHistoryAsync(string inputText, string status, string message, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CommandHistoryEntry>> GetRecentHistoryAsync(int limit = 20, CancellationToken cancellationToken = default);

    Task SetAliasAsync(string name, string expansion, CancellationToken cancellationToken = default);

    Task<string?> GetAliasExpansionAsync(string name, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CommandAlias>> ListAliasesAsync(CancellationToken cancellationToken = default);

    /// <returns>True when an alias was actually removed.</returns>
    Task<bool> DeleteAliasAsync(string name, CancellationToken cancellationToken = default);

    Task SetAppPathAsync(string name, string path, CancellationToken cancellationToken = default);

    Task<string?> GetAppPathAsync(string name, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LearnedAppPath>> ListAppPathsAsync(CancellationToken cancellationToken = default);

    /// <returns>True when a learned path was actually removed.</returns>
    Task<bool> DeleteAppPathAsync(string name, CancellationToken cancellationToken = default);
}
