using System.Globalization;
using CIPHER.Core;
using Microsoft.Data.Sqlite;

namespace CIPHER.Memory;

/// <summary>
/// SQLite-backed <see cref="IMemoryStore"/>. Fully local (WAL journaling so a process kill
/// cannot tear committed writes), schema-migrated, and self-healing: a database file that
/// turns out not to be a database (or is corrupt) is quarantined next to the original and
/// replaced with a fresh one instead of crashing the assistant.
/// </summary>
public sealed class SqliteMemoryStore : IMemoryStore, IDisposable
{
    private const int CurrentSchemaVersion = 1;

    private readonly string _databasePath;
    private readonly string _connectionString;
    private readonly SemaphoreSlim _initializeLock = new(1, 1);
    private volatile bool _initialized;
    private bool _disposed;

    public SqliteMemoryStore(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException("A database path is required.", nameof(databasePath));
        }

        _databasePath = databasePath;
        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
    }

    public string DatabasePath => _databasePath;

    /// <summary>True when initialization found a corrupt database and quarantined it.</summary>
    public bool RecoveredFromCorruption { get; private set; }

    public static SqliteMemoryStore CreateDefault()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var root = string.IsNullOrWhiteSpace(localAppData)
            ? Path.GetTempPath()
            : Path.Combine(localAppData, "CIPHER");

        return new SqliteMemoryStore(Path.Combine(root, "cipher-memory.db"));
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        await _initializeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized)
            {
                return;
            }

            var directory = Path.GetDirectoryName(_databasePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            try
            {
                await OpenAndMigrateAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (SqliteException ex) when (IsCorruption(ex))
            {
                // Quarantine the damaged file (kept for inspection, never deleted) and
                // start fresh: losing learned aliases beats failing to start at all.
                QuarantineDatabaseFiles();
                RecoveredFromCorruption = true;
                await OpenAndMigrateAsync(cancellationToken).ConfigureAwait(false);
            }

            _initialized = true;
        }
        finally
        {
            _initializeLock.Release();
        }
    }

    public async Task<TaskRecord> BeginTaskAsync(string inputText, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var record = new TaskRecord(Guid.NewGuid(), inputText ?? string.Empty, TaskRecordStatus.Running, string.Empty, now, now);

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO tasks (id, input_text, status, message, created_utc, updated_utc) " +
            "VALUES ($id, $input, $status, $message, $created, $updated);";
        command.Parameters.AddWithValue("$id", record.Id.ToString("D"));
        command.Parameters.AddWithValue("$input", record.InputText);
        command.Parameters.AddWithValue("$status", record.Status);
        command.Parameters.AddWithValue("$message", record.Message);
        command.Parameters.AddWithValue("$created", FormatTimestamp(record.CreatedUtc));
        command.Parameters.AddWithValue("$updated", FormatTimestamp(record.UpdatedUtc));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return record;
    }

    public async Task CompleteTaskAsync(Guid taskId, string status, string message, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "UPDATE tasks SET status = $status, message = $message, updated_utc = $updated WHERE id = $id;";
        command.Parameters.AddWithValue("$id", taskId.ToString("D"));
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$message", message ?? string.Empty);
        command.Parameters.AddWithValue("$updated", FormatTimestamp(DateTimeOffset.UtcNow));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TaskRecord>> GetIncompleteTasksAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT id, input_text, status, message, created_utc, updated_utc FROM tasks " +
            "WHERE status = $running ORDER BY created_utc;";
        command.Parameters.AddWithValue("$running", TaskRecordStatus.Running);

        var results = new List<TaskRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(new TaskRecord(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                ParseTimestamp(reader.GetString(4)),
                ParseTimestamp(reader.GetString(5))));
        }

        return results;
    }

    public async Task AddHistoryAsync(string inputText, string status, string message, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO command_history (input_text, status, message, executed_utc) VALUES ($input, $status, $message, $executed);";
        command.Parameters.AddWithValue("$input", inputText ?? string.Empty);
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$message", message ?? string.Empty);
        command.Parameters.AddWithValue("$executed", FormatTimestamp(DateTimeOffset.UtcNow));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CommandHistoryEntry>> GetRecentHistoryAsync(int limit = 20, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT id, input_text, status, message, executed_utc FROM command_history ORDER BY id DESC LIMIT $limit;";
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 500));

        var results = new List<CommandHistoryEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(new CommandHistoryEntry(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                ParseTimestamp(reader.GetString(4))));
        }

        return results;
    }

    public async Task SetAliasAsync(string name, string expansion, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO aliases (name, display_name, expansion, updated_utc) VALUES ($name, $display, $expansion, $updated) " +
            "ON CONFLICT(name) DO UPDATE SET display_name = excluded.display_name, expansion = excluded.expansion, updated_utc = excluded.updated_utc;";
        command.Parameters.AddWithValue("$name", MemoryCommandText.NormalizeName(name));
        command.Parameters.AddWithValue("$display", name.Trim());
        command.Parameters.AddWithValue("$expansion", expansion.Trim());
        command.Parameters.AddWithValue("$updated", FormatTimestamp(DateTimeOffset.UtcNow));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<string?> GetAliasExpansionAsync(string name, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT expansion FROM aliases WHERE name = $name;";
        command.Parameters.AddWithValue("$name", MemoryCommandText.NormalizeName(name));
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value as string;
    }

    public async Task<IReadOnlyList<CommandAlias>> ListAliasesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT display_name, expansion, updated_utc FROM aliases ORDER BY name;";

        var results = new List<CommandAlias>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(new CommandAlias(reader.GetString(0), reader.GetString(1), ParseTimestamp(reader.GetString(2))));
        }

        return results;
    }

    public async Task<bool> DeleteAliasAsync(string name, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM aliases WHERE name = $name;";
        command.Parameters.AddWithValue("$name", MemoryCommandText.NormalizeName(name));
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
    }

    public async Task SetAppPathAsync(string name, string path, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO app_paths (name, display_name, path, updated_utc) VALUES ($name, $display, $path, $updated) " +
            "ON CONFLICT(name) DO UPDATE SET display_name = excluded.display_name, path = excluded.path, updated_utc = excluded.updated_utc;";
        command.Parameters.AddWithValue("$name", MemoryCommandText.NormalizeName(name));
        command.Parameters.AddWithValue("$display", name.Trim());
        command.Parameters.AddWithValue("$path", path.Trim());
        command.Parameters.AddWithValue("$updated", FormatTimestamp(DateTimeOffset.UtcNow));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<string?> GetAppPathAsync(string name, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT path FROM app_paths WHERE name = $name;";
        command.Parameters.AddWithValue("$name", MemoryCommandText.NormalizeName(name));
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value as string;
    }

    public async Task<IReadOnlyList<LearnedAppPath>> ListAppPathsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT display_name, path, updated_utc FROM app_paths ORDER BY name;";

        var results = new List<LearnedAppPath>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(new LearnedAppPath(reader.GetString(0), reader.GetString(1), ParseTimestamp(reader.GetString(2))));
        }

        return results;
    }

    public async Task<bool> DeleteAppPathAsync(string name, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM app_paths WHERE name = $name;";
        command.Parameters.AddWithValue("$name", MemoryCommandText.NormalizeName(name));
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _initializeLock.Dispose();
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private async Task OpenAndMigrateAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await ExecuteAsync(connection, "PRAGMA journal_mode=WAL;", cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(
            connection,
            "CREATE TABLE IF NOT EXISTS schema_meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);",
            cancellationToken).ConfigureAwait(false);

        var version = await GetSchemaVersionAsync(connection, cancellationToken).ConfigureAwait(false);
        if (version >= CurrentSchemaVersion)
        {
            return;
        }

        using var transaction = connection.BeginTransaction();
        if (version < 1)
        {
            await ExecuteAsync(connection, transaction,
                "CREATE TABLE IF NOT EXISTS tasks (" +
                "id TEXT PRIMARY KEY, input_text TEXT NOT NULL, status TEXT NOT NULL, " +
                "message TEXT NOT NULL, created_utc TEXT NOT NULL, updated_utc TEXT NOT NULL);",
                cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(connection, transaction,
                "CREATE TABLE IF NOT EXISTS command_history (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT, input_text TEXT NOT NULL, status TEXT NOT NULL, " +
                "message TEXT NOT NULL, executed_utc TEXT NOT NULL);",
                cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(connection, transaction,
                "CREATE TABLE IF NOT EXISTS aliases (" +
                "name TEXT PRIMARY KEY, display_name TEXT NOT NULL, expansion TEXT NOT NULL, updated_utc TEXT NOT NULL);",
                cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(connection, transaction,
                "CREATE TABLE IF NOT EXISTS app_paths (" +
                "name TEXT PRIMARY KEY, display_name TEXT NOT NULL, path TEXT NOT NULL, updated_utc TEXT NOT NULL);",
                cancellationToken).ConfigureAwait(false);
        }

        await ExecuteAsync(connection, transaction,
            "INSERT INTO schema_meta (key, value) VALUES ('schema_version', $version) " +
            "ON CONFLICT(key) DO UPDATE SET value = excluded.value;",
            cancellationToken,
            ("$version", CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture))).ConfigureAwait(false);
        transaction.Commit();
    }

    private static async Task<int> GetSchemaVersionAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM schema_meta WHERE key = 'schema_version';";
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is string text && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var version)
            ? version
            : 0;
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, string Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, string Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private void QuarantineDatabaseFiles()
    {
        SqliteConnection.ClearAllPools();
        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        TryMove(_databasePath, $"{_databasePath}.corrupt-{stamp}");
        TryMove(_databasePath + "-wal", $"{_databasePath}-wal.corrupt-{stamp}");
        TryMove(_databasePath + "-shm", $"{_databasePath}-shm.corrupt-{stamp}");
    }

    private static void TryMove(string source, string destination)
    {
        try
        {
            if (File.Exists(source))
            {
                File.Move(source, destination);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: initialization will surface a real error if the file is unusable.
        }
    }

    private static bool IsCorruption(SqliteException exception)
    {
        // SQLITE_CORRUPT (11) / SQLITE_NOTADB (26).
        return exception.SqliteErrorCode is 11 or 26 ||
               exception.Message.Contains("not a database", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatTimestamp(DateTimeOffset value)
    {
        return value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset ParseTimestamp(string value)
    {
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : DateTimeOffset.MinValue;
    }
}
