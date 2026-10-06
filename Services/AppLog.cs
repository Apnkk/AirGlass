using System.IO;

namespace AirGlass.Services;

/// <summary>
/// File log in %APPDATA%\AirGlass (writable even when the app is installed
/// in Program Files). Rotates at 1 MB. Never throws.
/// </summary>
public static class AppLog
{
    private const long MaxBytes = 1_000_000;
    private static readonly object Gate = new();

    public static string DataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AirGlass");

    public static string FilePath { get; } = Path.Combine(DataDir, "airglass.log");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(DataDir);
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > MaxBytes)
                    File.Move(FilePath, FilePath + ".old", true);

                File.AppendAllText(
                    FilePath,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine,
                    System.Text.Encoding.UTF8);
            }
        }
        catch
        {
            // logging is best-effort
        }
    }

    public static void Error(string context, Exception ex) => Write($"{context}: {ex}");
}
