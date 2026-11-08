using System.Diagnostics;
using System.Windows.Automation;
using CIPHER.Core;

namespace CIPHER.Actions;

/// <summary>Window discovery and focus operations backed by Windows UI Automation.</summary>
public sealed class WindowService
{
    public sealed record WindowInfo(
        string Title,
        string ProcessName,
        int ProcessId,
        IntPtr Handle);

    public IReadOnlyList<WindowInfo> GetTopLevelWindows()
    {
        var windows = new List<WindowInfo>();
        foreach (var element in GetTopLevelElements())
        {
            var info = ToWindowInfo(element);
            if (info is not null)
            {
                windows.Add(info);
            }
        }

        return windows;
    }

    public AutomationElement? FindWindow(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        var known = ApplicationCatalog.Resolve(query);
        var normalizedQuery = Normalize(query);
        var normalizedDisplayName = Normalize(known?.DisplayName ?? string.Empty);
        var normalizedProcessName = Normalize(known?.ProcessName ?? string.Empty);

        AutomationElement? best = null;
        var bestScore = -1;

        foreach (var element in GetTopLevelElements())
        {
            var info = ToWindowInfo(element);
            if (info is null)
            {
                continue;
            }

            var title = Normalize(info.Title);
            var process = Normalize(info.ProcessName);
            var score = 0;

            if (!string.IsNullOrEmpty(normalizedDisplayName))
            {
                if (title.Equals(normalizedDisplayName, StringComparison.Ordinal))
                {
                    score = Math.Max(score, 100);
                }
                else if (title.Contains(normalizedDisplayName, StringComparison.Ordinal))
                {
                    score = Math.Max(score, 85);
                }
            }

            if (!string.IsNullOrEmpty(normalizedProcessName) && process.Equals(normalizedProcessName, StringComparison.Ordinal))
            {
                score = Math.Max(score, 80);
            }

            if (title.Contains(normalizedQuery, StringComparison.Ordinal))
            {
                score = Math.Max(score, 70);
            }

            if (process.Contains(normalizedQuery, StringComparison.Ordinal))
            {
                score = Math.Max(score, 60);
            }

            if (score > bestScore)
            {
                bestScore = score;
                best = element;
            }
        }

        return bestScore > 0 ? best : null;
    }

    public bool Focus(AutomationElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        var handle = GetHandle(element);
        if (handle != IntPtr.Zero)
        {
            NativeMethods.ShowWindowAsync(handle, NativeMethods.SwRestore);
            if (NativeMethods.SetForegroundWindow(handle))
            {
                Thread.Sleep(50);
                return NativeMethods.GetForegroundWindow() == handle;
            }
        }

        try
        {
            element.SetFocus();
            Thread.Sleep(50);
            return handle == IntPtr.Zero || NativeMethods.GetForegroundWindow() == handle;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public WindowInfo? GetForegroundWindowInfo()
    {
        var handle = NativeMethods.GetForegroundWindow();
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var element = AutomationElement.FromHandle(handle);
            return ToWindowInfo(element);
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
    }

    private static List<AutomationElement> GetTopLevelElements()
    {
        var result = new List<AutomationElement>();
        try
        {
            var condition = new PropertyCondition(
                AutomationElement.ControlTypeProperty,
                ControlType.Window);
            var elements = AutomationElement.RootElement.FindAll(TreeScope.Children, condition);
            foreach (AutomationElement element in elements)
            {
                result.Add(element);
            }
        }
        catch (ElementNotAvailableException)
        {
            // The desktop may be transitioning between sessions or secure desktops.
        }

        return result;
    }

    private static WindowInfo? ToWindowInfo(AutomationElement element)
    {
        try
        {
            var processId = element.Current.ProcessId;
            var processName = string.Empty;
            try
            {
                using var process = Process.GetProcessById(processId);
                processName = process.ProcessName;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Process metadata is optional for window matching.
            }

            return new WindowInfo(
                element.Current.Name ?? string.Empty,
                processName,
                processId,
                GetHandle(element));
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
    }

    private static IntPtr GetHandle(AutomationElement element)
    {
        try
        {
            return (IntPtr)element.Current.NativeWindowHandle;
        }
        catch (ElementNotAvailableException)
        {
            return IntPtr.Zero;
        }
    }

    private static string Normalize(string value)
    {
        return value.Trim().ToLowerInvariant();
    }
}
