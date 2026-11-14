using System.Runtime.InteropServices;
using static PricePin.Interop.NativeMethods;

namespace PricePin.Interop;

/// <summary>
/// Registers a window as a Windows "app bar" (like the taskbar): it reserves a strip at
/// the top or bottom of a monitor so maximised windows don't cover it.
/// </summary>
internal sealed class AppBar : IDisposable
{
    private const int ABM_NEW = 0, ABM_REMOVE = 1, ABM_QUERYPOS = 2, ABM_SETPOS = 3;
    private const int ABE_TOP = 1, ABE_BOTTOM = 3;
    private const int ABN_POSCHANGED = 1, ABN_FULLSCREENAPP = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA
    {
        public int cbSize;
        public nint hWnd;
        public int uCallbackMessage;
        public int uEdge;
        public RECT rc;
        public nint lParam;
    }

    [DllImport("shell32.dll")]
    private static extern nint SHAppBarMessage(int dwMessage, ref APPBARDATA pData);

    private readonly nint _hwnd;
    private bool _registered;
    private bool _top;
    private int _height;

    public AppBar(nint hwnd)
    {
        _hwnd = hwnd;
        CallbackMessage = RegisterWindowMessage("PricePin.AppBar");
    }

    /// <summary>Windows sends this message when the bar needs to react to changes.</summary>
    public int CallbackMessage { get; }

    public bool IsRegistered => _registered;

    /// <summary>Raised with true when a full-screen app opens on this monitor (and false when it closes).</summary>
    public event Action<bool>? FullScreenAppChanged;

    /// <summary>Reserves a strip of <paramref name="heightPx"/> physical pixels and moves the window into it.</summary>
    public void Dock(bool top, int heightPx)
    {
        _top = top;
        _height = heightPx;
        var data = NewData();
        if (!_registered)
        {
            data.uCallbackMessage = CallbackMessage;
            SHAppBarMessage(ABM_NEW, ref data);
            _registered = true;
        }
        Position();
    }

    public void Undock()
    {
        if (!_registered)
            return;
        var data = NewData();
        SHAppBarMessage(ABM_REMOVE, ref data);
        _registered = false;
    }

    /// <summary>Call from the window procedure; returns true if the message was the app bar's.</summary>
    public bool HandleMessage(int msg, nint wParam, nint lParam)
    {
        if (!_registered || msg != CallbackMessage)
            return false;

        switch ((int)wParam)
        {
            case ABN_POSCHANGED:
                Position();
                break;
            case ABN_FULLSCREENAPP:
                FullScreenAppChanged?.Invoke(lParam != 0);
                break;
        }
        return true;
    }

    public void Dispose() => Undock();

    private void Position()
    {
        if (!TryGetMonitor(_hwnd, out var monitor, out _))
            return;

        var data = NewData();
        data.uEdge = _top ? ABE_TOP : ABE_BOTTOM;
        data.rc = monitor;
        if (_top)
            data.rc.Bottom = data.rc.Top + _height;
        else
            data.rc.Top = data.rc.Bottom - _height;

        // Windows may move the strip (e.g. below the taskbar); keep our height after it does.
        SHAppBarMessage(ABM_QUERYPOS, ref data);
        if (_top)
            data.rc.Bottom = data.rc.Top + _height;
        else
            data.rc.Top = data.rc.Bottom - _height;
        SHAppBarMessage(ABM_SETPOS, ref data);

        SetWindowPos(_hwnd, 0, data.rc.Left, data.rc.Top, data.rc.Width, data.rc.Height, SWP_NOZORDER | SWP_NOACTIVATE);
    }

    private APPBARDATA NewData() => new() { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = _hwnd };
}
