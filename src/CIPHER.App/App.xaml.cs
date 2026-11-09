using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace CIPHER.App;

public partial class App : System.Windows.Application
{
    /// <summary>
    /// Set when started with `--safe-mode`: external delegation and continuous wake
    /// listening stay disabled for the session so CIPHER can be opened safely to review
    /// settings, quarantine, and the audit log after a problem.
    /// </summary>
    internal static bool IsSafeMode { get; private set; }

    // Held for the process lifetime: a second CIPHER instance would double-register the
    // global hotkey, duplicate the tray icon, and interleave audit-log appends.
    private Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        IsSafeMode = e.Args.Any(arg => string.Equals(arg, "--safe-mode", StringComparison.OrdinalIgnoreCase));

        var mutexName = $"Local\\CIPHER.App.SingleInstance.{Environment.UserName}";
        _singleInstanceMutex = new Mutex(initiallyOwned: true, mutexName, out var createdNew);
        if (!createdNew)
        {
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            global::System.Windows.MessageBox.Show(
                "CIPHER is already running. Look for its icon in the taskbar or system tray.",
                "CIPHER",
                global::System.Windows.MessageBoxButton.OK,
                global::System.Windows.MessageBoxImage.Information);
            Shutdown();
            return;
        }

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                LogError("AppDomain.UnhandledException", exception);
            }
        };

        DispatcherUnhandledException += (_, args) =>
        {
            // Log and keep the shell alive: an unhandled UI exception must not kill
            // the tray app mid-task. AppDomain.UnhandledException above remains the
            // last-resort path for failures that cannot be contained.
            LogError("DispatcherUnhandledException", args.Exception);
            args.Handled = true;
        };

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_singleInstanceMutex is not null)
        {
            try
            {
                _singleInstanceMutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // The mutex was not owned by this thread; disposing is enough.
            }

            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
        }

        base.OnExit(e);
    }

    /// <summary>
    /// Last-resort local crash log. Contains exception text only — never user documents,
    /// credentials, or command contents.
    /// </summary>
    internal static void LogError(string source, Exception exception)
    {
        try
        {
            var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localApplicationData))
            {
                return;
            }

            var logDirectory = Path.Combine(localApplicationData, "CIPHER", "logs");
            Directory.CreateDirectory(logDirectory);
            File.AppendAllText(
                Path.Combine(logDirectory, "app-errors.log"),
                $"{DateTimeOffset.Now:O} [{source}] {exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Error logging must never crash the error path.
        }
    }
}
