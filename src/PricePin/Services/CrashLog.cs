using System.IO;

namespace PricePin.Services;

/// <summary>Appends unexpected errors to %AppData%\PricePin\crash.log so bug reports have something to attach.</summary>
public static class CrashLog
{
    private const long MaxBytes = 512 * 1024;

    public static void Write(Exception? ex)
    {
        if (ex is null)
            return;
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PricePin");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "crash.log");
            if (File.Exists(path) && new FileInfo(path).Length > MaxBytes)
                File.Delete(path);
            File.AppendAllText(path, $"[{DateTimeOffset.Now:u}] {ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never take the app down with it.
        }
    }
}
