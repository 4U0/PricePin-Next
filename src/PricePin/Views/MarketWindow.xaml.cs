using System.Windows;
using System.Windows.Input;
using PricePin.ViewModels;

namespace PricePin.Views;

public partial class MarketWindow : Window
{
    public MarketWindow(MarketViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        Closed += (_, _) => vm.Dispose();
        Loaded += (_, _) => vm.Start();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                Close();
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowCorners.Round(this);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
