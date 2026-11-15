using System.Windows;
using System.Windows.Input;
using PricePin.ViewModels;

namespace PricePin.Views;

/// <summary>News, whale trades and liquidations, network fees, the event calendar and prediction odds.</summary>
public partial class PulseWindow : Window
{
    private readonly PulseViewModel _vm;

    public PulseWindow(PulseViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        // The services keep running for widgets and notifications; the window only tells
        // them it is looking, so the parts it shows start while it is open.
        Loaded += (_, _) => vm.WindowOpened();
        Closed += (_, _) => vm.WindowClosed();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                Close();
        };
    }

    public void SelectTab(string tab) => _vm.Tab = tab is "Radar" or "Fees" or "Calendar" or "Odds" ? tab : "News";

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowCorners.Round(this);
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
