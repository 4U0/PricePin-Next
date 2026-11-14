using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace PricePin.Services;

/// <summary>
/// System-wide keyboard shortcuts (they work even when PricePin isn't focused).
/// Shortcuts are written like "Ctrl+Alt+P".
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8, MOD_NOREPEAT = 0x4000;

    private readonly HwndSource _window;
    private readonly Dictionary<int, Action> _actions = [];
    private int _nextId = 1;

    public HotkeyService()
    {
        // A hidden message-only window receives the hotkey messages.
        _window = new HwndSource(new HwndSourceParameters("PricePin.Hotkeys") { ParentWindow = new nint(-3) /* HWND_MESSAGE */ });
        _window.AddHook(WndProc);
    }

    /// <summary>Registers a shortcut. Returns false if it is invalid or another app already uses it.</summary>
    public bool Register(string? gesture, Action action)
    {
        if (!TryParse(gesture, out var modifiers, out var key))
            return false;

        var id = _nextId++;
        if (!RegisterHotKey(_window.Handle, id, modifiers | MOD_NOREPEAT, (uint)KeyInterop.VirtualKeyFromKey(key)))
            return false;
        _actions[id] = action;
        return true;
    }

    public void UnregisterAll()
    {
        foreach (var id in _actions.Keys)
            UnregisterHotKey(_window.Handle, id);
        _actions.Clear();
    }

    public void Dispose()
    {
        UnregisterAll();
        _window.Dispose();
    }

    /// <summary>Parses "Ctrl+Alt+P". At least one modifier is required so normal typing is never hijacked.</summary>
    public static bool TryParse(string? gesture, out uint modifiers, out Key key)
    {
        modifiers = 0;
        key = Key.None;
        if (string.IsNullOrWhiteSpace(gesture))
            return false;

        foreach (var raw in gesture.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (raw.ToUpperInvariant())
            {
                case "CTRL" or "CONTROL": modifiers |= MOD_CONTROL; break;
                case "ALT": modifiers |= MOD_ALT; break;
                case "SHIFT": modifiers |= MOD_SHIFT; break;
                case "WIN" or "WINDOWS": modifiers |= MOD_WIN; break;
                default:
                    if (key != Key.None || !Enum.TryParse(raw.Length == 1 && char.IsDigit(raw[0]) ? "D" + raw : raw, true, out key))
                        return false;
                    break;
            }
        }
        return modifiers != 0 && key != Key.None;
    }

    /// <summary>Turns a key press into "Ctrl+Alt+P", or null if it's only modifiers.</summary>
    public static string? Format(ModifierKeys modifiers, Key key)
    {
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin or Key.System or Key.None)
            return null;
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        if (parts.Count == 0)
            return null;
        parts.Add(key is >= Key.D0 and <= Key.D9 ? ((int)key - (int)Key.D0).ToString() : key.ToString());
        return string.Join("+", parts);
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && _actions.TryGetValue((int)wParam, out var action))
        {
            action();
            handled = true;
        }
        return 0;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(nint hWnd, int id);
}
