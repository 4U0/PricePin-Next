using PricePin.Services;
using Velopack;

namespace PricePin;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Must run first: when the installer or updater launches PricePin with a hook
        // argument, Velopack handles it here and exits. Otherwise it returns at once.
        VelopackApp.Build()
            .OnBeforeUninstallFastCallback(_ => StartupService.SetEnabled(false))
            .Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
