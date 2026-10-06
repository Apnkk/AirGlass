using System.IO;
using System.Text.Json;

namespace AirGlass.Services;

/// <summary>User settings persisted as JSON in %APPDATA%\AirGlass\settings.json.</summary>
public sealed class AppSettings
{
    public const int MaxNameLength = 40;

    /// <summary>Custom receiver name. Empty = use the PC name.</summary>
    public string ReceiverName { get; set; } = "";

    public bool StartWithWindows { get; set; }

    /// <summary>Closing the window hides it in the tray instead of quitting.</summary>
    public bool MinimizeToTray { get; set; } = true;

    /// <summary>When launched by Windows at logon (--minimized), start hidden in the tray.</summary>
    public bool StartMinimized { get; set; } = true;

    /// <summary>Keep the window above all others while a mirroring session is displayed.</summary>
    public bool KeepOnTopWhileMirroring { get; set; }

    /// <summary>A new iPhone connecting replaces the current one (uxplay -nohold).</summary>
    public bool NewClientReplacesCurrent { get; set; }

    /// <summary>Allow up to 60 fps instead of the default 30 (uxplay -fps 60).</summary>
    public bool Fps60 { get; set; }

    /// <summary>Remembered startup mode: "apple" (UxPlay) or "android" (scrcpy). Empty = ask at every launch.</summary>
    public string RememberedMode { get; set; } = "";

    /// <summary>Legacy 4K flag, only read to migrate old settings.json files to <see cref="QualityHeight"/>.</summary>
    public bool Use4K { get; set; }

    /// <summary>Mirroring quality as video height: 2160, 1440, 1080, 720 or 480 (uxplay -s, -h265 for 2160).</summary>
    public int QualityHeight { get; set; } = 1080;

    public static readonly int[] QualityHeights = { 2160, 1440, 1080, 720, 480 };

    /// <summary>Lower latency by disabling audio/video timestamp sync (uxplay -vsync no).</summary>
    public bool LowLatency { get; set; }

    /// <summary>Turn audio off, video only (uxplay -as 0).</summary>
    public bool AudioOff { get; set; }

    /// <summary>Ask for a 4-digit PIN on the iPhone before mirroring (uxplay -pin).</summary>
    public bool UsePin { get; set; }

    public string Pin { get; set; } = "";

    public static bool IsValidPin([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? pin) =>
        pin is { Length: 4 } && pin.All(char.IsAsciiDigit) && pin != "0000";

    /// <summary>Returns an error message in French, or null when the PIN setting is valid.</summary>
    public static string? ValidatePin(bool enabled, string? pin) =>
        enabled && !string.IsNullOrWhiteSpace(pin) && !IsValidPin(pin.Trim())
            ? "Le code PIN doit contenir 4 chiffres (0001 à 9999), ou rester vide pour un code aléatoire."
            : null;

    /// <summary>A device the user refused (uxplay -block &lt;deviceID&gt;).</summary>
    public sealed class BlockedDevice
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
    }

    /// <summary>Devices that are always refused, persisted in settings.json.</summary>
    public List<BlockedDevice> BlockedDevices { get; set; } = new();

    private static string SettingsPath => Path.Combine(AppLog.DataDir, "settings.json");

    public string EffectiveReceiverName =>
        string.IsNullOrWhiteSpace(ReceiverName) ? AirPlayInfo.ReceiverName : ReceiverName.Trim();

    /// <summary>Returns an error message in French, or null when the name is valid.</summary>
    public static string? ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null; // empty = default PC name
        var trimmed = name.Trim();
        if (trimmed.Length > MaxNameLength)
            return $"Le nom ne doit pas dépasser {MaxNameLength} caractères.";
        if (trimmed.Any(char.IsControl))
            return "Le nom contient des caractères invalides.";
        return null;
    }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath));
                if (loaded is not null)
                {
                    if (loaded.Use4K)
                    {
                        loaded.QualityHeight = 2160;
                        loaded.Use4K = false;
                    }
                    if (!QualityHeights.Contains(loaded.QualityHeight)) loaded.QualityHeight = 1080;
                    return loaded;
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Lecture des réglages impossible", ex);
        }
        return new AppSettings();
    }

    public bool Save()
    {
        try
        {
            Directory.CreateDirectory(AppLog.DataDir);
            File.WriteAllText(SettingsPath,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error("Écriture des réglages impossible", ex);
            return false;
        }
    }
}
