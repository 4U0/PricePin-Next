using System.Windows;
using PricePin.ViewModels;

namespace PricePin.Views;

/// <summary>The one-time notice that PricePin sends an anonymous daily usage ping, with a way to turn it off.</summary>
public partial class UsagePromptWindow : Window
{
    private readonly AppViewModel _app;
    private bool _answered;

    public UsagePromptWindow(AppViewModel app)
    {
        InitializeComponent();
        _app = app;
        // Closing the notice keeps the count on; it can be turned off in Settings → General.
        Closed += (_, _) =>
        {
            if (!_answered)
                _app.AnswerUsagePrompt(true);
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowCorners.Round(this);
    }

    private void Yes_Click(object sender, RoutedEventArgs e) => Answer(true);

    private void No_Click(object sender, RoutedEventArgs e) => Answer(false);

    private void Answer(bool yes)
    {
        _answered = true;
        _app.AnswerUsagePrompt(yes);
        Close();
    }
}
