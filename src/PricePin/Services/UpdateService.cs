using Velopack;
using Velopack.Sources;

namespace PricePin.Services;

/// <summary>
/// Checks GitHub Releases for a newer PricePin and installs it, using Velopack. Only the
/// installed app can update itself; the portable exe reports <see cref="IsInstalled"/> = false.
/// </summary>
public sealed class UpdateService
{
    public const string RepoUrl = "https://github.com/4U0/PricePin-Next";
    public const string ReleasesUrl = RepoUrl + "/releases/latest";

    private readonly UpdateManager? _manager;

    public UpdateService()
    {
        try
        {
            _manager = new UpdateManager(new GithubSource(RepoUrl, null, false));
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex);
        }
    }

    public bool IsInstalled => _manager?.IsInstalled == true;

    public string? CurrentVersion => _manager?.CurrentVersion?.ToString();

    /// <summary>An update that was downloaded earlier and applies on restart.</summary>
    public VelopackAsset? PendingRestart => IsInstalled ? _manager!.UpdatePendingRestart : null;

    /// <summary>Null when already up to date.</summary>
    public Task<UpdateInfo?> CheckAsync() => Manager.CheckForUpdatesAsync();

    public Task DownloadAsync(UpdateInfo update, Action<int> progress, CancellationToken ct) =>
        Manager.DownloadUpdatesAsync(update, progress, ct);

    /// <summary>Exits this process, installs the update and starts the new version.</summary>
    public void ApplyAndRestart(VelopackAsset target) => Manager.ApplyUpdatesAndRestart(target);

    private UpdateManager Manager =>
        IsInstalled ? _manager! : throw new InvalidOperationException("Updates need the installed version.");
}
