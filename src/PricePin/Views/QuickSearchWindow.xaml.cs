using System.Windows;
using System.Windows.Input;
using PricePin.ViewModels;

namespace PricePin.Views;

/// <summary>
/// The quick search popup. It opens in the upper third of the screen the mouse is on, like a
/// launcher, and closes on Esc or when another window gets the focus.
/// </summary>
public partial class QuickSearchWindow : Window
{
    private readonly QuickSearchViewModel _vm;
    private bool _closing;

    public QuickSearchWindow(QuickSearchViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        vm.CloseRequested += (_, _) => SafeClose();
        Closed += (_, _) => vm.Dispose();
        Deactivated += (_, _) => SafeClose();
        Loaded += (_, _) =>
        {
            PlaceOnScreen();
            SearchBox.Focus();
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                SafeClose();
            }
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowCorners.Round(this);
    }

    /// <summary>Centred near the top of the monitor under the mouse.</summary>
    private void PlaceOnScreen()
    {
        var area = SystemParameters.WorkArea;
        if (Interop.NativeMethods.TryGetWorkAreaAtCursor(out var px))
        {
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
            area = new Rect(px.Left / dpi.DpiScaleX, px.Top / dpi.DpiScaleY, px.Width / dpi.DpiScaleX, px.Height / dpi.DpiScaleY);
        }
        Left = area.Left + (area.Width - ActualWidth) / 2;
        Top = area.Top + area.Height * 0.16;
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                _vm.MoveSelection(+1);
                ResultList.ScrollIntoView(_vm.Selected);
                e.Handled = true;
                break;
            case Key.Up:
                _vm.MoveSelection(-1);
                ResultList.ScrollIntoView(_vm.Selected);
                e.Handled = true;
                break;
            case Key.Enter:
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                    _vm.AddToWidgetCommand.Execute(null);
                else
                    _vm.OpenChartCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private void ResultList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => _vm.OpenChartCommand.Execute(null);

    private void SafeClose()
    {
        if (_closing)
            return;
        _closing = true;
        // Closing inside Deactivated can upset WPF's activation bookkeeping; do it right after.
        Dispatcher.BeginInvoke(Close);
    }
}
