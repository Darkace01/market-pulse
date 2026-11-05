using System;
using System.Globalization;
using System.IO;
using System.Threading;

namespace MarketTicker;

/// <summary>
/// Appends timestamped lines to diagnostics.log next to settings.json. CmdPal's own log doesn't show
/// what happens inside an extension, so this records band lifecycle, feed activity and errors.
/// </summary>
internal static class Diagnostics
{
    private const long MaxBytes = 512 * 1024;

    private static readonly Lock _lock = new();

    public static string LogPath { get; } = Path.Combine(Path.GetDirectoryName(SettingsManager.SettingsJsonPath())!, "diagnostics.log");

    public static void Log(string message)
    {
        try
        {
            lock (_lock)
            {
                var file = new FileInfo(LogPath);
                if (file.Exists && file.Length > MaxBytes)
                {
                    file.Delete();
                }

                File.AppendAllText(LogPath, string.Create(CultureInfo.InvariantCulture, $"{DateTime.Now:HH:mm:ss.fff} [{Environment.CurrentManagedThreadId}] {message}{Environment.NewLine}"));
            }
        }
        catch (IOException)
        {
            // Diagnostics must never break the extension.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
