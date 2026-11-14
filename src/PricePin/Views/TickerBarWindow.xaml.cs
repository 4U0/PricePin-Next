using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using PricePin.Core.Settings;
using PricePin.Interop;
using PricePin.ViewModels;
using static PricePin.Interop.NativeMethods;

namespace PricePin.Views;

/// <summary>
/// The horizontal ticker. Docked to the top or bottom of a monitor (optionally reserving
/// that space like the taskbar), or floating anywhere. Coins scroll past continuously;
/// hovering pauses the scroll.
/// </summary>
public partial class TickerBarWindow : Window
{
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(33); // ~30 fps is smooth enough and cheap

    private readonly WidgetViewModel _vm;
    private readonly DispatcherTimer _scrollTimer;
    private readonly Stopwatch _clock = new();
    private nint _hwnd;
    private AppBar? _appBar;
    private bool _allowClose;
    private bool _positioning;

    public TickerBarWindow(WidgetViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;

        Width = vm.Settings.FloatingWidth;
        Left = vm.Settings.Left ?? (SystemParameters.WorkArea.Left + (SystemParameters.WorkArea.Width - Width) / 2);
        Top = vm.Settings.Top ?? SystemParameters.WorkArea.Top + 8;

        _scrollTimer = new DispatcherTimer(FrameInterval, DispatcherPriority.Render, (_, _) => ScrollStep(), Dispatcher);
        _scrollTimer.Stop();

        vm.PropertyChanged += OnViewModelPropertyChanged;
        vm.DockingChanged += OnDockingChanged;
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        CopyA.SizeChanged += (_, _) => UpdateScrolling();
        Viewport.SizeChanged += (_, _) => UpdateScrolling();
        IsVisibleChanged += (_, _) => UpdateScrolling();
    }

    public void CloseForExit()
    {
        _allowClose = true;
        Close();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;

        var ex = GetWindowLongPtr(_hwnd, GWL_EXSTYLE);
        SetWindowLongPtr(_hwnd, GWL_EXSTYLE, (ex | WS_EX_TOOLWINDOW) & ~(nint)WS_EX_APPWINDOW);

        _appBar = new AppBar(_hwnd);
        _appBar.FullScreenAppChanged += fullScreen =>
            // Step out of the way of full-screen games and videos.
            Topmost = !fullScreen && _vm.Topmost;
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
        ApplyClickThrough(_vm.ClickThrough);
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        ApplyDocking();
        UpdateScrolling();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            _vm.IsWidgetVisible = false;
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _scrollTimer.Stop();
        _appBar?.Dispose();
        _vm.PropertyChanged -= OnViewModelPropertyChanged;
        _vm.DockingChanged -= OnDockingChanged;
        base.OnClosed(e);
    }

    protected override void OnLocationChanged(EventArgs e)
    {
        base.OnLocationChanged(e);
        // Only a floating bar remembers where it was dragged.
        if (IsLoaded && !_positioning && _vm.IsFloating)
            _vm.SavePosition(Left, Top);
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Dispatcher.BeginInvoke(ApplyDocking, DispatcherPriority.Background);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(WidgetViewModel.ClickThrough):
                ApplyClickThrough(_vm.ClickThrough);
                break;
            case nameof(WidgetViewModel.Scale):
                Dispatcher.BeginInvoke(ApplyDocking, DispatcherPriority.Background);
                break;
            case nameof(WidgetViewModel.Scroll):
            case nameof(WidgetViewModel.IsWidgetVisible):
                UpdateScrolling();
                break;
        }
    }

    private void OnDockingChanged(object? sender, EventArgs e) => ApplyDocking();

    /// <summary>Places the bar according to Dock / ReserveSpace / FloatingWidth.</summary>
    private void ApplyDocking()
    {
        if (_hwnd == 0 || !IsLoaded)
            return;

        _positioning = true;
        try
        {
            Bar.CornerRadius = _vm.IsFloating ? new CornerRadius(10) : new CornerRadius(0);
            Bar.BorderThickness = _vm.IsFloating ? new Thickness(1)
                : _vm.Dock == BarDock.Top ? new Thickness(0, 0, 0, 1) : new Thickness(0, 1, 0, 0);
            UpdateLayout();

            var scale = GetDpiForWindow(_hwnd) / 96.0;
            var heightPx = (int)Math.Ceiling(ActualHeight * scale);

            if (_vm.IsFloating)
            {
                _appBar?.Undock();
                Width = _vm.FloatingWidth;
                Left = _vm.Settings.Left ?? Left;
                Top = _vm.Settings.Top ?? Top;
                return;
            }

            var top = _vm.Dock == BarDock.Top;
            if (_vm.ReserveSpace)
            {
                _appBar?.Dock(top, heightPx);
                return;
            }

            _appBar?.Undock();
            if (!TryGetMonitor(_hwnd, out _, out var work))
                return;
            var y = top ? work.Top : work.Bottom - heightPx;
            SetWindowPos(_hwnd, 0, work.Left, y, work.Width, heightPx, SWP_NOZORDER | SWP_NOACTIVATE);
        }
        finally
        {
            _positioning = false;
        }
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (_appBar?.HandleMessage(msg, wParam, lParam) == true)
            handled = true;
        return 0;
    }

    // ---- Scrolling ---------------------------------------------------------------------

    private void UpdateScrolling()
    {
        var contentWidth = CopyA.ActualWidth;
        var needsScroll = _vm.Scroll && IsVisible && contentWidth > Viewport.ActualWidth && contentWidth > 0;

        CopyB.Visibility = needsScroll ? Visibility.Visible : Visibility.Collapsed;
        if (needsScroll)
        {
            if (!_scrollTimer.IsEnabled)
            {
                _clock.Restart();
                _scrollTimer.Start();
            }
        }
        else
        {
            _scrollTimer.Stop();
            Offset.X = 0;
        }
    }

    private void ScrollStep()
    {
        var dt = _clock.Elapsed.TotalSeconds;
        _clock.Restart();
        if (IsMouseOver)
            return; // pause while the user is reading or clicking

        var loop = CopyA.ActualWidth;
        if (loop <= 0)
            return;
        var x = Offset.X - _vm.ScrollSpeed * Math.Min(dt, 0.1);
        if (x <= -loop)
            x += loop;
        Offset.X = Math.Round(x, 1);
    }

    // ---- Mouse -------------------------------------------------------------------------

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && WidgetWindow.CoinUnder(e.OriginalSource) is { } row)
        {
            e.Handled = true;
            _vm.App.OpenCoinDetail(row.Symbol);
            return;
        }
        if (!_vm.IsFloating || _vm.LockPosition || e.ButtonState != MouseButtonState.Pressed || e.OriginalSource is ButtonBase)
            return;
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
        }
    }

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
}
