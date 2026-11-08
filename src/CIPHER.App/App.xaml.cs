using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace CIPHER.App;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                LogError("AppDomain.UnhandledException", exception);
            }
        };

        DispatcherUnhandledException += (_, args) =>
        {
            LogError("DispatcherUnhandledException", args.Exception);
        };

        base.OnStartup(e);
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
