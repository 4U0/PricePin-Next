using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using PricePin.Core.Settings;

namespace PricePin.Services;

/// <summary>
/// Loads and saves <see cref="AppSettings"/>. Saves are debounced so dragging the
/// window or moving the opacity slider does not hammer the disk, and every write
/// goes to a temp file first so a crash mid-save can't corrupt the settings.
/// </summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private readonly string _path;
    private readonly DispatcherTimer _saveTimer;

    public SettingsService(string? directory = null)
    {
        directory ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PricePin");
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "settings.json");

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _saveTimer.Tick += (_, _) => SaveNow();

        Current = Load();
    }

    public AppSettings Current { get; }

    public string FilePath => _path;

    /// <summary>Asks for a save shortly; repeated calls are merged into one write.</summary>
    public void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public void SaveNow()
    {
        _saveTimer.Stop();
        try
        {
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Current, JsonOptions));
            File.Move(tmp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not worth crashing a widget over; the next change will try again.
            Debug.WriteLine($"PricePin: could not save settings: {ex.Message}");
        }
    }

    private AppSettings Load()
    {
        AppSettings settings;
        try
        {
            settings = File.Exists(_path)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), JsonOptions) ?? new AppSettings()
                : new AppSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Keep the broken file for inspection instead of silently overwriting it.
            Debug.WriteLine($"PricePin: settings unreadable, using defaults: {ex.Message}");
            TryBackupCorruptFile();
            settings = new AppSettings();
        }

        settings.Normalize();
        return settings;
    }

    private void TryBackupCorruptFile()
    {
        try
        {
            File.Copy(_path, Path.ChangeExtension(_path, ".corrupt.json"), overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"PricePin: could not back up corrupt settings: {ex.Message}");
        }
    }
}
