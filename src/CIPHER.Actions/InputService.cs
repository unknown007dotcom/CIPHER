using System.ComponentModel;
using System.Runtime.InteropServices;

namespace CIPHER.Actions;

/// <summary>Unicode text and keyboard shortcut injection through Win32 SendInput.</summary>
public sealed class InputService
{
    private const uint InputKeyboard = 1;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint KeyEventUnicode = 0x0004;

    private const ushort VkBack = 0x08;
    private const ushort VkTab = 0x09;
    private const ushort VkReturn = 0x0D;
    private const ushort VkShift = 0x10;
    private const ushort VkControl = 0x11;
    private const ushort VkMenu = 0x12;
    private const ushort VkCapital = 0x14;
    private const ushort VkEscape = 0x1B;
    private const ushort VkSpace = 0x20;
    private const ushort VkPrior = 0x21;
    private const ushort VkNext = 0x22;
    private const ushort VkEnd = 0x23;
    private const ushort VkHome = 0x24;
    private const ushort VkLeft = 0x25;
    private const ushort VkUp = 0x26;
    private const ushort VkRight = 0x27;
    private const ushort VkDown = 0x28;
    private const ushort VkSnapshot = 0x2C;
    private const ushort VkInsert = 0x2D;
    private const ushort VkDelete = 0x2E;
    private const ushort VkLWin = 0x5B;
    private const ushort VkVolumeMute = 0xAD;
    private const ushort VkVolumeDown = 0xAE;
    private const ushort VkVolumeUp = 0xAF;
    private const ushort VkMediaNextTrack = 0xB0;
    private const ushort VkMediaPrevTrack = 0xB1;
    private const ushort VkMediaStop = 0xB2;
    private const ushort VkMediaPlayPause = 0xB3;

    private static readonly IReadOnlyDictionary<string, ushort> NamedKeys =
        new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase)
        {
            ["ctrl"] = VkControl,
            ["control"] = VkControl,
            ["alt"] = VkMenu,
            ["shift"] = VkShift,
            ["win"] = VkLWin,
            ["windows"] = VkLWin,
            ["meta"] = VkLWin,
            ["enter"] = VkReturn,
            ["return"] = VkReturn,
            ["tab"] = VkTab,
            ["esc"] = VkEscape,
            ["escape"] = VkEscape,
            ["space"] = VkSpace,
            ["spacebar"] = VkSpace,
            ["backspace"] = VkBack,
            ["delete"] = VkDelete,
            ["del"] = VkDelete,
            ["insert"] = VkInsert,
            ["home"] = VkHome,
            ["end"] = VkEnd,
            ["pageup"] = VkPrior,
            ["pagedown"] = VkNext,
            ["left"] = VkLeft,
            ["up"] = VkUp,
            ["right"] = VkRight,
            ["down"] = VkDown,
            ["printscreen"] = VkSnapshot,
            ["capslock"] = VkCapital,
            ["volumeup"] = VkVolumeUp,
            ["volumedown"] = VkVolumeDown,
            ["volumemute"] = VkVolumeMute,
            ["mutevolume"] = VkVolumeMute,
            ["playpause"] = VkMediaPlayPause,
            ["mediaplaypause"] = VkMediaPlayPause,
            ["nexttrack"] = VkMediaNextTrack,
            ["previoustrack"] = VkMediaPrevTrack,
            ["mediastop"] = VkMediaStop
        };

    public void TypeText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var inputs = new List<NativeMethods.INPUT>();
        foreach (var character in text)
        {
            switch (character)
            {
                case '\r':
                    continue;
                case '\n':
                    AddVirtualKey(inputs, VkReturn);
                    break;
                case '\t':
                    AddVirtualKey(inputs, VkTab);
                    break;
                default:
                    inputs.Add(CreateKeyboardInput(0, character, KeyEventUnicode));
                    inputs.Add(CreateKeyboardInput(0, character, KeyEventUnicode | KeyEventKeyUp));
                    break;
            }
        }

        Send(inputs);
    }

    private const uint MouseEventMove = 0x0001;
    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;
    private const uint MouseEventAbsolute = 0x8000;
    private const uint MouseEventVirtualDesk = 0x4000;

    /// <summary>
    /// Moves the pointer to a screen coordinate and performs a left click through SendInput.
    /// Used only as a fallback for UI elements that expose no actionable UIA pattern.
    /// </summary>
    public void ClickAt(double screenX, double screenY)
    {
        var virtualX = NativeMethods.GetSystemMetrics(NativeMethods.SmXVirtualScreen);
        var virtualY = NativeMethods.GetSystemMetrics(NativeMethods.SmYVirtualScreen);
        var virtualWidth = NativeMethods.GetSystemMetrics(NativeMethods.SmCxVirtualScreen);
        var virtualHeight = NativeMethods.GetSystemMetrics(NativeMethods.SmCyVirtualScreen);
        if (virtualWidth <= 0 || virtualHeight <= 0)
        {
            throw new InvalidOperationException("Windows did not report a virtual screen size.");
        }

        var normalizedX = (int)Math.Round((screenX - virtualX) * 65535d / Math.Max(1, virtualWidth - 1));
        var normalizedY = (int)Math.Round((screenY - virtualY) * 65535d / Math.Max(1, virtualHeight - 1));
        var flags = MouseEventMove | MouseEventAbsolute | MouseEventVirtualDesk;

        Send(new List<NativeMethods.INPUT>
        {
            CreateMouseInput(normalizedX, normalizedY, flags),
            CreateMouseInput(normalizedX, normalizedY, flags | MouseEventLeftDown),
            CreateMouseInput(normalizedX, normalizedY, flags | MouseEventLeftUp)
        });
    }

    public void PressKeys(string keys)
    {
        if (string.IsNullOrWhiteSpace(keys))
        {
            throw new ArgumentException("No keys were supplied.", nameof(keys));
        }

        var virtualKeys = keys
            .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(MapKey)
            .ToList();

        if (virtualKeys.Count == 0)
        {
            throw new ArgumentException("No valid keys were supplied.", nameof(keys));
        }

        var inputs = new List<NativeMethods.INPUT>();
        foreach (var key in virtualKeys)
        {
            inputs.Add(CreateKeyboardInput(key, 0, 0));
        }

        for (var index = virtualKeys.Count - 1; index >= 0; index--)
        {
            inputs.Add(CreateKeyboardInput(virtualKeys[index], 0, KeyEventKeyUp));
        }

        Send(inputs);
    }

    private static void AddVirtualKey(List<NativeMethods.INPUT> inputs, ushort virtualKey)
    {
        inputs.Add(CreateKeyboardInput(virtualKey, 0, 0));
        inputs.Add(CreateKeyboardInput(virtualKey, 0, KeyEventKeyUp));
    }

    private static ushort MapKey(string key)
    {
        var normalized = key.Trim().ToLowerInvariant().Replace(" ", string.Empty, StringComparison.Ordinal);
        if (NamedKeys.TryGetValue(normalized, out var namedKey))
        {
            return namedKey;
        }

        if (normalized.Length == 1)
        {
            var character = normalized[0];
            if (char.IsLetterOrDigit(character))
            {
                return char.ToUpperInvariant(character);
            }
        }

        if (normalized.StartsWith("f", StringComparison.Ordinal) &&
            int.TryParse(normalized[1..], out var functionKey) &&
            functionKey is >= 1 and <= 24)
        {
            return (ushort)(0x70 + functionKey - 1);
        }

        throw new ArgumentException($"Unsupported key '{key}'.");
    }

    private static NativeMethods.INPUT CreateMouseInput(int normalizedX, int normalizedY, uint flags)
    {
        return new NativeMethods.INPUT
        {
            Type = 0,
            Data = new NativeMethods.InputUnion
            {
                Mouse = new NativeMethods.MOUSEINPUT
                {
                    X = normalizedX,
                    Y = normalizedY,
                    MouseData = 0,
                    Flags = flags,
                    Time = 0,
                    ExtraInfo = UIntPtr.Zero
                }
            }
        };
    }

    private static NativeMethods.INPUT CreateKeyboardInput(ushort virtualKey, ushort scanCode, uint flags)
    {
        return new NativeMethods.INPUT
        {
            Type = InputKeyboard,
            Data = new NativeMethods.InputUnion
            {
                Keyboard = new NativeMethods.KEYBDINPUT
                {
                    VirtualKey = virtualKey,
                    ScanCode = scanCode,
                    Flags = flags,
                    Time = 0,
                    ExtraInfo = UIntPtr.Zero
                }
            }
        };
    }

    private static void Send(List<NativeMethods.INPUT> inputs)
    {
        if (inputs.Count == 0)
        {
            return;
        }

        var array = inputs.ToArray();
        var sent = NativeMethods.SendInput(
            (uint)array.Length,
            array,
            Marshal.SizeOf<NativeMethods.INPUT>());

        if (sent != array.Length)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows rejected one or more simulated keyboard inputs.");
        }
    }
}
