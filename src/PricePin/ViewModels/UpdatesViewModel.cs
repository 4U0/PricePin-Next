using System.Diagnostics;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PricePin.Services;
using Velopack;

namespace PricePin.ViewModels;

public enum UpdateState { Portable, Idle, Checking, UpToDate, Available, Downloading, Ready, Failed }

/// <summary>
/// Background updates. With automatic updates on, PricePin checks shortly after starting and
/// then every few hours, downloads a new version quietly, and offers a restart. If the user
/// doesn't restart, Velopack applies the download the next time PricePin starts.
/// </summary>
public sealed partial class UpdatesViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    private readonly UpdateService _service;
    private readonly AppViewModel _app;
    private readonly DispatcherTimer _timer;
    private readonly CancellationTokenSource _lifetime = new();
    private UpdateInfo? _update;
    private VelopackAsset? _ready;
    private string? _error;

    public UpdatesViewModel(UpdateService service, AppViewModel app)
    {
        _service = service;
        _app = app;
        _autoUpdate = app.Settings.AutoUpdate;

        if (!service.IsInstalled)
            _state = UpdateState.Portable;
        else if (service.PendingRestart is { } pending)
        {
            _ready = pending;
            _state = UpdateState.Ready;
        }
        else
            _state = UpdateState.Idle;

        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = FirstCheckDelay };
        _timer.Tick += async (_, _) =>
        {
            _timer.Interval = CheckInterval;
            if (AutoUpdate)
                await CheckAsync(download: true);
        };
    }

    /// <summary>Raised once a new version is downloaded and waiting for a restart.</summary>
    public event EventHandler? ReadyToRestart;

    /// <summary>The user asked to restart into the new version; the app tidies up and calls <see cref="ApplyAndRestart"/>.</summary>
    public event EventHandler? RestartRequested;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(IsInstalled), nameof(IsPortable), nameof(IsReady), nameof(CanCheck), nameof(CanDownload), nameof(RestartMenuText))]
    private UpdateState _state;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private int _progress;

    [ObservableProperty]
    private bool _autoUpdate;

    public bool IsInstalled => State != UpdateState.Portable;
    public bool IsPortable => !IsInstalled;
    public bool IsReady => State == UpdateState.Ready;
    public bool CanCheck => State is UpdateState.Idle or UpdateState.UpToDate or UpdateState.Failed;
    public bool CanDownload => State == UpdateState.Available;

    public string NewVersion => (_ready?.Version ?? _update?.TargetFullRelease.Version)?.ToString() ?? "";

    public string RestartMenuText => Loc.F("Menu.RestartToUpdate", NewVersion);

    public string StatusText => State switch
    {
        UpdateState.Portable => Loc.T("Update.Portable"),
        UpdateState.Checking => Loc.T("Update.Checking"),
        UpdateState.UpToDate => Loc.F("Update.UpToDate", _service.CurrentVersion),
        UpdateState.Available => Loc.F("Update.Available", NewVersion),
        UpdateState.Downloading => Loc.F("Update.Downloading", NewVersion, Progress),
        UpdateState.Ready => Loc.F("Update.Ready", NewVersion),
        UpdateState.Failed => Loc.F("Update.Failed", _error),
        _ => Loc.T("Update.Idle"),
    };

    public void Start()
    {
        if (IsInstalled && !IsReady)
            _timer.Start();
    }

    public void RefreshText()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(RestartMenuText));
    }

    partial void OnAutoUpdateChanged(bool value)
    {
        _app.Settings.AutoUpdate = value;
        _app.Save();
    }

    [RelayCommand]
    private Task CheckNow() => CheckAsync(download: AutoUpdate);

    [RelayCommand]
    private Task Download() => _update is null ? Task.CompletedTask : DownloadAsync(_update);

    [RelayCommand]
    private void Restart() => RestartRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private static void OpenReleases() =>
        Process.Start(new ProcessStartInfo(UpdateService.ReleasesUrl) { UseShellExecute = true });

    /// <summary>Called by the app once windows are closed and settings saved.</summary>
    public void ApplyAndRestart()
    {
        if (_ready is not null)
            _service.ApplyAndRestart(_ready);
    }

    private async Task CheckAsync(bool download)
    {
        if (!CanCheck)
            return;
        State = UpdateState.Checking;
        try
        {
            _update = await _service.CheckAsync();
            if (_update is null)
            {
                State = UpdateState.UpToDate;
                return;
            }
            State = UpdateState.Available;
            if (download)
                await DownloadAsync(_update);
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
    }

    private async Task DownloadAsync(UpdateInfo update)
    {
        if (State is UpdateState.Downloading or UpdateState.Ready)
            return;
        State = UpdateState.Downloading;
        Progress = 0;
        try
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            await _service.DownloadAsync(update, p => dispatcher.BeginInvoke(() => Progress = p), _lifetime.Token);
            _ready = update.TargetFullRelease;
            _timer.Stop();
            State = UpdateState.Ready;
            ReadyToRestart?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
    }

    private void Fail(Exception ex)
    {
        _error = ex is System.Net.Http.HttpRequestException ? ex.Message : ex.GetType().Name + ": " + ex.Message;
        State = UpdateState.Failed;
        Debug.WriteLine($"PricePin: update failed: {ex}");
    }

    public void Dispose()
    {
        _timer.Stop();
        _lifetime.Cancel();
    }
}
