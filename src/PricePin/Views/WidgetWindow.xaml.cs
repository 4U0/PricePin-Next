using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using PricePin.Interop;
using PricePin.ViewModels;
using static PricePin.Interop.NativeMethods;

namespace PricePin.Views;

/// <summary>
/// The floating widget. Window-level behaviour that XAML can't express lives here:
/// dragging, snapping to screen edges, click-through, and keeping it on a visible monitor.
/// </summary>
public partial class WidgetWindow : Window
{
    private const double OpacityWheelStep = 0.05;

    private readonly WidgetViewModel _vm;
    private readonly double? _savedLeft;
    private readonly double? _savedTop;
    private nint _hwnd;
    private bool _allowClose;

    public WidgetWindow(WidgetViewModel vm, double? savedLeft, double? savedTop)
    {
        InitializeComponent();
        _vm = vm;
        _savedLeft = savedLeft;
        _savedTop = savedTop;
        DataContext = vm;

        if (savedLeft is { } l && savedTop is { } t)
        {
            Left = l;
            Top = t;
        }
        else
        {
            // Off-screen until the size is known; then moved to the default corner in OnContentRendered.
            Left = -10_000;
            Top = -10_000;
        }

        // Long coin lists scroll instead of running off the screen.
        RowsScroller.MaxHeight = SystemParameters.WorkArea.Height * 0.75;

        vm.PropertyChanged += OnViewModelPropertyChanged;
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        PreviewMouseWheel += OnPreviewMouseWheel;
    }

    /// <summary>Lets the window really close (used on exit); otherwise closing hides it to the tray.</summary>
    public void CloseForExit()
    {
        _allowClose = true;
        Close();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;

        // Tool window: no taskbar button and not listed in Alt+Tab.
        var ex = GetWindowLongPtr(_hwnd, GWL_EXSTYLE);
        SetWindowLongPtr(_hwnd, GWL_EXSTYLE, (ex | WS_EX_TOOLWINDOW) & ~(nint)WS_EX_APPWINDOW);

        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
        ApplyClickThrough(_vm.ClickThrough);
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        if (_savedLeft is null || _savedTop is null || !IsOnAnyMonitor())
            MoveToDefaultPosition();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Alt+F4 hides the widget to the tray instead of quitting.
        if (!_allowClose)
        {
            e.Cancel = true;
            _vm.ToggleVisibilityCommand.Execute(null);
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _vm.PropertyChanged -= OnViewModelPropertyChanged;
        base.OnClosed(e);
    }

    protected override void OnLocationChanged(EventArgs e)
    {
        base.OnLocationChanged(e);
        if (IsLoaded && Left > -9_000)
            _vm.SavePosition(Left, Top);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WidgetViewModel.ClickThrough))
            ApplyClickThrough(_vm.ClickThrough);
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_vm.LockPosition || e.ButtonState != MouseButtonState.Pressed || e.OriginalSource is ButtonBase)
            return;
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // Mouse was released before DragMove started; nothing to do.
        }
    }

    /// <summary>Ctrl + mouse wheel over the widget changes its opacity.</summary>
    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
            return;
        _vm.Opacity = Math.Round((_vm.Opacity + Math.Sign(e.Delta) * OpacityWheelStep) / OpacityWheelStep) * OpacityWheelStep;
        e.Handled = true;
    }

    private void PinButton_Click(object sender, RoutedEventArgs e) => _vm.Topmost = !_vm.Topmost;

    private void MenuButton_Click(object sender, RoutedEventArgs e)
    {
        if (ContextMenu is { } menu)
        {
            menu.PlacementTarget = (UIElement)sender;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    private void ApplyClickThrough(bool enabled)
    {
        if (_hwnd == 0)
            return;
        var ex = GetWindowLongPtr(_hwnd, GWL_EXSTYLE);
        ex = enabled ? ex | WS_EX_TRANSPARENT | WS_EX_LAYERED : ex & ~(nint)WS_EX_TRANSPARENT;
        SetWindowLongPtr(_hwnd, GWL_EXSTYLE, ex);
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WM_MOVING && _vm.SnapToEdges && !IsShiftDown())
        {
            var rect = Marshal.PtrToStructure<RECT>(lParam);
            if (SnapToWorkArea(ref rect))
            {
                Marshal.StructureToPtr(rect, lParam, false);
                handled = true;
                return 1;
            }
        }
        return 0;
    }

    /// <summary>
    /// Pulls the visible card flush against a screen edge when it comes within
    /// <see cref="WidgetMetrics.SnapDistance"/>. Works in physical pixels so it is
    /// correct on every monitor regardless of its scaling.
    /// </summary>
    private bool SnapToWorkArea(ref RECT rect)
    {
        if (!TryGetWorkArea(rect, MONITOR_DEFAULTTONEAREST, out var work))
            return false;

        var scale = GetDpiForWindow(_hwnd) / 96.0;
        var threshold = (int)Math.Round(WidgetMetrics.SnapDistance * scale);
        var inset = (int)Math.Round(WidgetMetrics.ShadowSize * scale);

        int cardLeft = rect.Left + inset, cardRight = rect.Right - inset;
        int cardTop = rect.Top + inset, cardBottom = rect.Bottom - inset;

        var dx = Math.Abs(cardLeft - work.Left) <= threshold ? work.Left - cardLeft
               : Math.Abs(cardRight - work.Right) <= threshold ? work.Right - cardRight
               : 0;
        var dy = Math.Abs(cardTop - work.Top) <= threshold ? work.Top - cardTop
               : Math.Abs(cardBottom - work.Bottom) <= threshold ? work.Bottom - cardBottom
               : 0;

        if (dx == 0 && dy == 0)
            return false;

        rect.Left += dx;
        rect.Right += dx;
        rect.Top += dy;
        rect.Bottom += dy;
        return true;
    }

    /// <summary>True if the widget overlaps some monitor (it may not after a monitor is unplugged).</summary>
    private bool IsOnAnyMonitor() =>
        _hwnd != 0 && GetWindowRect(_hwnd, out var rect) && TryGetWorkArea(rect, MONITOR_DEFAULTTONULL, out _);

    /// <summary>Top-right corner of the primary screen's work area.</summary>
    private void MoveToDefaultPosition()
    {
        var work = SystemParameters.WorkArea;
        var gap = WidgetMetrics.DefaultEdgeGap - WidgetMetrics.ShadowSize;
        Left = work.Right - ActualWidth - gap;
        Top = work.Top + gap;
        _vm.SavePosition(Left, Top);
    }

    private static bool IsShiftDown() => (GetKeyState(VK_SHIFT) & 0x8000) != 0;
}
