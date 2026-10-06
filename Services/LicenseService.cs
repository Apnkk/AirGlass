using System.Buffers.Text;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace AirGlass.Services;

public enum LicenseStatus
{
    Licensed,
    Trial,
    Expired,
}

public sealed record LicenseInfo(LicenseStatus Status, string? Email, int TrialDaysLeft);

/// <summary>
/// Offline license check. A key is "AS1-" + base64url(payload) + "." + base64url(signature),
/// signed with the vendor's private ECDSA key (tools/KeyGen). Only the public key is embedded here.
/// Without a valid key the app works for <see cref="TrialDays"/> days.
/// </summary>
public static class LicenseService
{
    public const string PublicKeyBase64 =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEUfIbOoRDET+Kr87KBXr7lQWKSmgwtTb8/Xz92qtif8RE+BxWGXaJ3U13xQ5KIaZ6K0HKYB+vxttXf23fcfRMwA==";

    /// <summary>Checkout page opened by the "Acheter" button. Empty = button hidden.</summary>
    public const string PurchaseUrl = "";

    public const int TrialDays = 7;

    private const string Prefix = "AS1-";
    private const string RegistryPath = @"Software\AirGlass";
    private const string RegistryValue = "FirstRun";

    private static string LicensePath => Path.Combine(AppLog.DataDir, "license.key");
    private static string TrialPath => Path.Combine(AppLog.DataDir, "trial.dat");

    public static LicenseInfo Evaluate()
    {
        var stored = ReadStoredKey();
        if (stored is not null && TryValidate(stored, out var email))
            return new LicenseInfo(LicenseStatus.Licensed, email, 0);

        var firstRun = GetOrCreateFirstRunUtc();
        var elapsedDays = Math.Max(0, (DateTime.UtcNow - firstRun).TotalDays);
        var left = TrialDays - (int)Math.Floor(elapsedDays);

        return left > 0
            ? new LicenseInfo(LicenseStatus.Trial, null, left)
            : new LicenseInfo(LicenseStatus.Expired, null, 0);
    }

    public static bool TryActivate(string? key, out string error, out string email)
    {
        error = "";
        email = "";

        if (string.IsNullOrWhiteSpace(key))
        {
            error = "Colle ta clé de licence.";
            return false;
        }

        if (!TryValidate(key, out email))
        {
            error = "Clé invalide. Vérifie qu'elle est complète (elle commence par AS1-).";
            return false;
        }

        try
        {
            Directory.CreateDirectory(AppLog.DataDir);
            File.WriteAllText(LicensePath, Normalize(key));
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error("Enregistrement de la licence impossible", ex);
            error = "Impossible d'enregistrer la licence : " + ex.Message;
            return false;
        }
    }

    public static bool TryValidate(string key, out string email)
    {
        email = "";
        try
        {
            var clean = Normalize(key);
            if (!clean.StartsWith(Prefix, StringComparison.Ordinal)) return false;

            var parts = clean[Prefix.Length..].Split('.');
            if (parts.Length != 2) return false;

            var payload = Base64Url.DecodeFromChars(parts[0]);
            var signature = Base64Url.DecodeFromChars(parts[1]);

            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(PublicKeyBase64), out _);
            if (!ecdsa.VerifyData(payload, signature, HashAlgorithmName.SHA256)) return false;

            var fields = Encoding.UTF8.GetString(payload).Split('|');
            if (fields.Length < 3 || fields[0] != "AS1" || fields[1].Length == 0) return false;

            email = fields[1];
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string Normalize(string key) =>
        string.Concat(key.Where(c => !char.IsWhiteSpace(c)));

    private static string? ReadStoredKey()
    {
        try
        {
            return File.Exists(LicensePath) ? File.ReadAllText(LicensePath) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Earliest known first-run date (file + registry copies), rewritten to both.</summary>
    private static DateTime GetOrCreateFirstRunUtc()
    {
        var dates = new List<DateTime>();
        if (TryParseTicks(ReadFirstRunFile(), out var fromFile)) dates.Add(fromFile);
        if (TryParseTicks(ReadFirstRunRegistry(), out var fromRegistry)) dates.Add(fromRegistry);

        var first = dates.Count > 0 ? dates.Min() : DateTime.UtcNow;
        WriteFirstRun(first);
        return first;
    }

    private static bool TryParseTicks(string? text, out DateTime value)
    {
        value = default;
        if (!long.TryParse(text, out var ticks)) return false;
        if (ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks) return false;
        value = new DateTime(ticks, DateTimeKind.Utc);
        return true;
    }

    private static string? ReadFirstRunFile()
    {
        try
        {
            return File.Exists(TrialPath) ? File.ReadAllText(TrialPath).Trim() : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadFirstRunRegistry()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath);
            return key?.GetValue(RegistryValue) as string;
        }
        catch
        {
            return null;
        }
    }

    private static void WriteFirstRun(DateTime firstRunUtc)
    {
        var text = firstRunUtc.Ticks.ToString();
        try
        {
            Directory.CreateDirectory(AppLog.DataDir);
            File.WriteAllText(TrialPath, text);
        }
        catch { /* best-effort */ }

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegistryPath);
            key?.SetValue(RegistryValue, text);
        }
        catch { /* best-effort */ }
    }
}
