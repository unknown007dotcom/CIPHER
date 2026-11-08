namespace CIPHER.Core;

/// <summary>High-level states surfaced by the Dynamic Island UI.</summary>
public enum AssistantState
{
    Idle,
    Listening,
    Processing,
    Validating,
    Executing,
    Verifying,
    WaitingForConfirmation,
    Completed,
    Failed,
    Cancelled
}

/// <summary>Relative risk assigned before an action is allowed to run.</summary>
public enum RiskLevel
{
    Low = 0,
    Medium = 1,
    High = 2,
    Critical = 3
}

/// <summary>Deterministic intents understood by CIPHER's offline command engine.</summary>
public enum IntentKind
{
    Unknown,
    Help,
    GetTime,
    GetDate,
    GetSystemInfo,
    OpenApplication,
    CloseApplication,
    SwitchWindow,
    TypeText,
    PressKeys,
    ReadWindowText,
    VerifyWindowText,
    OpenPath,
    CreateFolder,
    CreateFile,
    ListDirectory,
    DeletePath,
    MovePath,
    CopyPath,
    RenamePath,
    RunCommand
}

/// <summary>Terminal and intermediate status for a user task.</summary>
public enum CipherTaskStatus
{
    Pending,
    Running,
    WaitingForConfirmation,
    Succeeded,
    SucceededUnverified,
    Failed,
    Blocked,
    Cancelled
}
