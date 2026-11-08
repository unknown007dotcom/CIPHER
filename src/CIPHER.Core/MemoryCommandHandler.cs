namespace CIPHER.Core;

/// <summary>How a piece of input was handled by <see cref="MemoryCommandHandler"/>.</summary>
public enum MemoryHandlingKind
{
    /// <summary>Not a memory command and not a known alias; run it as a normal command.</summary>
    NotHandled,

    /// <summary>A memory-management command was applied; Response is the user-facing reply.</summary>
    Responded,

    /// <summary>The input exactly matched an alias; Expansion is the command to run instead.</summary>
    ExpandAlias
}

/// <summary>Result of <see cref="MemoryCommandHandler.TryHandleAsync"/>.</summary>
public readonly record struct MemoryHandlingResult(
    MemoryHandlingKind Kind,
    string? Response,
    string? Expansion)
{
    public static MemoryHandlingResult NotHandled { get; } = new(MemoryHandlingKind.NotHandled, null, null);

    public static MemoryHandlingResult Responded(string response) => new(MemoryHandlingKind.Responded, response, null);

    public static MemoryHandlingResult Expand(string expansion) => new(MemoryHandlingKind.ExpandAlias, null, expansion);
}

/// <summary>
/// Applies teach/forget/list memory commands and whole-phrase alias expansion before the
/// deterministic parser runs. Expansions are returned to the caller and still pass through
/// the normal parser, policy, and confirmation flow — an alias can never bypass safety
/// checks, and alias-to-alias chains are not followed (one expansion per input).
/// </summary>
public sealed class MemoryCommandHandler
{
    private readonly IMemoryStore _store;

    public MemoryCommandHandler(IMemoryStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<MemoryHandlingResult> TryHandleAsync(string input, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return MemoryHandlingResult.NotHandled;
        }

        if (MemoryCommandText.TryParse(input, out var command))
        {
            var response = command.Kind switch
            {
                MemoryCommandKind.DefineAlias => await DefineAliasAsync(command, cancellationToken).ConfigureAwait(false),
                MemoryCommandKind.DeleteAlias => await DeleteAliasAsync(command, cancellationToken).ConfigureAwait(false),
                MemoryCommandKind.ListAliases => await ListAliasesAsync(cancellationToken).ConfigureAwait(false),
                MemoryCommandKind.DefineAppPath => await DefineAppPathAsync(command, cancellationToken).ConfigureAwait(false),
                MemoryCommandKind.DeleteAppPath => await DeleteAppPathAsync(command, cancellationToken).ConfigureAwait(false),
                MemoryCommandKind.ListAppPaths => await ListAppPathsAsync(cancellationToken).ConfigureAwait(false),
                _ => null
            };

            if (response is not null)
            {
                return MemoryHandlingResult.Responded(response);
            }
        }

        var normalizedInput = MemoryCommandText.NormalizeName(input);
        if (normalizedInput.Length == 0)
        {
            return MemoryHandlingResult.NotHandled;
        }

        var expansion = await _store.GetAliasExpansionAsync(normalizedInput, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(expansion) &&
            !string.Equals(MemoryCommandText.NormalizeName(expansion), normalizedInput, StringComparison.Ordinal))
        {
            return MemoryHandlingResult.Expand(expansion.Trim());
        }

        return MemoryHandlingResult.NotHandled;
    }

    private async Task<string> DefineAliasAsync(MemoryCommand command, CancellationToken cancellationToken)
    {
        await _store.SetAliasAsync(command.Name, command.Value, cancellationToken).ConfigureAwait(false);
        return $"Alias saved: '{command.Name}' now runs '{command.Value}'. Say '{command.Name}' any time to use it.";
    }

    private async Task<string> DeleteAliasAsync(MemoryCommand command, CancellationToken cancellationToken)
    {
        var removed = await _store.DeleteAliasAsync(command.Name, cancellationToken).ConfigureAwait(false);
        return removed
            ? $"Alias '{command.Name}' was deleted."
            : $"There is no alias named '{command.Name}'.";
    }

    private async Task<string> ListAliasesAsync(CancellationToken cancellationToken)
    {
        var aliases = await _store.ListAliasesAsync(cancellationToken).ConfigureAwait(false);
        return aliases.Count == 0
            ? "You have no aliases yet. Teach one with: alias <name> = <command>."
            : "Your aliases: " + string.Join("; ", aliases.Select(alias => $"'{alias.Name}' → '{alias.Expansion}'")) + ".";
    }

    private async Task<string> DefineAppPathAsync(MemoryCommand command, CancellationToken cancellationToken)
    {
        await _store.SetAppPathAsync(command.Name, command.Value, cancellationToken).ConfigureAwait(false);
        return $"Application location saved: '{command.Name}' → {command.Value}. 'open {command.Name}' will use it.";
    }

    private async Task<string> DeleteAppPathAsync(MemoryCommand command, CancellationToken cancellationToken)
    {
        var removed = await _store.DeleteAppPathAsync(command.Name, cancellationToken).ConfigureAwait(false);
        return removed
            ? $"Learned location for '{command.Name}' was deleted."
            : $"I have no learned location for '{command.Name}'.";
    }

    private async Task<string> ListAppPathsAsync(CancellationToken cancellationToken)
    {
        var apps = await _store.ListAppPathsAsync(cancellationToken).ConfigureAwait(false);
        return apps.Count == 0
            ? "No learned application locations yet. Teach one with: remember app <name> at <path>."
            : "Learned application locations: " + string.Join("; ", apps.Select(app => $"'{app.Name}' → {app.Path}")) + ".";
    }
}
