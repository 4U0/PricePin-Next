using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using PricePin.ViewModels;

namespace PricePin.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _vm;
    private Point? _dragStart;
    private CoinEntryViewModel? _dragItem;

    public SettingsWindow(SettingsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // Windows 11: keep rounded corners even though we draw our own title bar. Ignored on Windows 10.
        var hwnd = new WindowInteropHelper(this).Handle;
        var round = 2; // DWMWCP_ROUND
        _ = DwmSetWindowAttribute(hwnd, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref round, sizeof(int));
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // ---- Currency segmented control ----------------------------------------------------

    private void Quote_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string quote } rb)
            rb.IsChecked = quote == _vm.QuoteCurrency;
    }

    private void Quote_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string quote, IsLoaded: true } && quote != _vm.QuoteCurrency)
            _vm.QuoteCurrency = quote;
    }

    // ---- Hotkey capture ----------------------------------------------------------------

    private void Hotkey_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;

        string? gesture;
        if (modifiers == ModifierKeys.None && key is Key.Back or Key.Delete)
            gesture = string.Empty; // clear the shortcut
        else if (modifiers == ModifierKeys.None && key is Key.Tab or Key.Escape)
        {
            e.Handled = false; // let focus move / window handle Esc
            return;
        }
        else
            gesture = Services.HotkeyService.Format(modifiers, key);

        if (gesture is null) return; // modifier only so far, or not a valid combination

        if (ReferenceEquals(sender, HotkeyWidgets))
            _vm.App.HotkeyToggleWidgets = gesture;
        else if (ReferenceEquals(sender, HotkeyClickThrough))
            _vm.App.HotkeyToggleClickThrough = gesture;
        else if (ReferenceEquals(sender, HotkeyPrivacy))
            _vm.App.HotkeyTogglePrivacy = gesture;
        else if (ReferenceEquals(sender, HotkeyQuickSearch))
            _vm.App.HotkeyQuickSearch = gesture;
    }

    // ---- Add by double-click -----------------------------------------------------------

    private void SearchResults_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemUnder<CatalogItemViewModel>(e.OriginalSource) is { } item)
            _vm.AddCoinCommand.Execute(item);
    }

    // ---- Drag to reorder ---------------------------------------------------------------

    private void CoinList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsInsideButton(e.OriginalSource))
        {
            _dragItem = null;
            return;
        }
        _dragStart = e.GetPosition(CoinList);
        _dragItem = ItemUnder<CoinEntryViewModel>(e.OriginalSource);
    }

    private void CoinList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragItem is null || _dragStart is not { } start)
            return;

        var delta = e.GetPosition(CoinList) - start;
        if (Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        var item = _dragItem;
        _dragItem = null;
        DragDrop.DoDragDrop(CoinList, item, DragDropEffects.Move);
    }

    private void CoinList_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(CoinEntryViewModel)) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void CoinList_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(CoinEntryViewModel)) is not CoinEntryViewModel dragged)
            return;

        var target = ItemUnder<CoinEntryViewModel>(e.OriginalSource);
        var index = target is null ? _vm.SelectedCoins.Count - 1 : _vm.SelectedCoins.IndexOf(target);
        _vm.MoveTo(dragged, index);
    }

    private static T? ItemUnder<T>(object source) where T : class
    {
        for (var d = source as DependencyObject; d is not null; d = ParentOf(d))
        {
            if (d is ListBoxItem { DataContext: T item })
                return item;
        }
        return null;
    }

    private static bool IsInsideButton(object source)
    {
        for (var d = source as DependencyObject; d is not null and not ListBoxItem; d = ParentOf(d))
        {
            if (d is ButtonBase)
                return true;
        }
        return false;
    }

    /// <summary>Walks up through text runs (not visuals) as well as normal elements.</summary>
    private static DependencyObject? ParentOf(DependencyObject d) =>
        d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}
