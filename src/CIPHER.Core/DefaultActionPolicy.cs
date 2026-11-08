namespace CIPHER.Core;

/// <summary>Fail-closed policy for deterministic commands.</summary>
public sealed class DefaultActionPolicy : IActionPolicy
{
    private static readonly HashSet<string> PrivilegedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe",
        ".msi",
        ".bat",
        ".cmd",
        ".ps1",
        ".vbs",
        ".js",
        ".com",
        ".scr"
    };

    public PolicyDecision Evaluate(ParsedCommand command, CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);

        switch (command.Intent)
        {
            case IntentKind.Unknown:
                return Block(RiskLevel.Low, "The command was not recognized, so nothing was executed.");

            case IntentKind.RunCommand:
                return Block(
                    RiskLevel.Critical,
                    "Arbitrary shell execution is disabled in CIPHER v1. Use a structured command instead.");

            case IntentKind.DeletePath:
                if (PathSafety.IsProtectedPath(command.Target, out var deleteReason))
                {
                    return Block(RiskLevel.Critical, deleteReason);
                }

                return Allow(
                    RiskLevel.Critical,
                    requiresConfirmation: true,
                    "Deleting data requires explicit confirmation. CIPHER will move the item to its reversible quarantine folder.");

            case IntentKind.MovePath:
            case IntentKind.RenamePath:
                if (PathSafety.IsProtectedPath(command.Target, out var sourceReason))
                {
                    return Block(RiskLevel.High, sourceReason);
                }

                if (PathSafety.IsProtectedPath(command.SecondaryTarget, out var destinationReason))
                {
                    return Block(RiskLevel.High, destinationReason);
                }

                return Allow(
                    RiskLevel.High,
                    requiresConfirmation: true,
                    "Moving or renaming data requires explicit confirmation.");

            case IntentKind.CopyPath:
                if (PathSafety.IsProtectedPath(command.SecondaryTarget, out var copyReason))
                {
                    return Block(RiskLevel.Medium, copyReason);
                }

                return Allow(RiskLevel.Medium, false, "Copying creates new data but does not alter the source.");

            case IntentKind.CreateFolder:
            case IntentKind.CreateFile:
                if (PathSafety.IsProtectedPath(command.Target, out var createReason))
                {
                    return Block(RiskLevel.Medium, createReason);
                }

                return Allow(command.Risk, false, "The target is outside protected Windows locations.");

            case IntentKind.OpenApplication:
                var application = ApplicationCatalog.Resolve(command.Target);
                if (application?.IsSensitive == true)
                {
                    return Allow(
                        RiskLevel.High,
                        requiresConfirmation: true,
                        $"'{application.DisplayName}' can change system state and requires confirmation.");
                }

                return Allow(RiskLevel.Low, false, "Launching a known or user-requested application is permitted.");

            case IntentKind.OpenPath:
                if (!string.IsNullOrWhiteSpace(command.Target) &&
                    PrivilegedExtensions.Contains(Path.GetExtension(PathSafety.Unquote(command.Target))))
                {
                    return Allow(
                        RiskLevel.High,
                        requiresConfirmation: true,
                        "Opening an executable or script requires explicit confirmation.");
                }

                return Allow(RiskLevel.Medium, false, "Opening a non-executable path with its registered handler is permitted.");

            case IntentKind.PressKeys:
                if (IsDangerousShortcut(command.Argument))
                {
                    return Allow(
                        RiskLevel.High,
                        requiresConfirmation: true,
                        "This shortcut can close windows or open privileged system UI and requires confirmation.");
                }

                return Allow(RiskLevel.Medium, false, "The keyboard shortcut was explicitly requested.");

            case IntentKind.CloseApplication:
                return Allow(
                    RiskLevel.Medium,
                    false,
                    "CIPHER requests a graceful close and never force-kills the target in v1.");

            case IntentKind.TypeText:
                return Allow(RiskLevel.Medium, false, "Text entry was explicitly requested for the currently focused control.");

            case IntentKind.SwitchWindow:
            case IntentKind.ListDirectory:
            case IntentKind.Help:
            case IntentKind.GetTime:
            case IntentKind.GetDate:
            case IntentKind.GetSystemInfo:
                return Allow(RiskLevel.Low, false, "This is a read-only or navigation action.");

            default:
                return Block(command.Risk, $"Intent '{command.Intent}' is not enabled.");
        }
    }

    private static bool IsDangerousShortcut(string? keys)
    {
        if (string.IsNullOrWhiteSpace(keys))
        {
            return false;
        }

        var normalized = keys.Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
        return normalized.Contains("alt+f4", StringComparison.Ordinal) ||
               normalized.Contains("win+r", StringComparison.Ordinal) ||
               normalized.Contains("windows+r", StringComparison.Ordinal) ||
               normalized.Contains("ctrl+shift+enter", StringComparison.Ordinal);
    }

    private static PolicyDecision Allow(RiskLevel risk, bool requiresConfirmation, string reason)
    {
        return new PolicyDecision(true, requiresConfirmation, risk, reason);
    }

    private static PolicyDecision Block(RiskLevel risk, string reason)
    {
        return new PolicyDecision(false, false, risk, reason);
    }
}
