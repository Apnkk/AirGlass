using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace AirGlass.Services;

/// <summary>
/// Checks and repairs the Windows Firewall rules the receiver needs.
/// Rule names match the ones created by the installer.
/// </summary>
public static class FirewallHelper
{
    // BindProgram: the rule only applies to uxplay.exe (not to any process using the port).
    // mDNS 5353 stays unbound: name resolution may go through another process.
    private static readonly (string Name, string Protocol, string Ports, bool BindProgram)[] Rules =
    {
        ("AirGlass (mDNS 5353 UDP)", "UDP", "5353", false),
        ("AirGlass (AirPlay 7000-7002 TCP)", "TCP", "7000-7002", true),
        ("AirGlass (AirPlay 7000-7002 UDP)", "UDP", "7000-7002", true),
    };

    /// <summary>True when all rules exist. Also true when the check itself fails (never nag on doubt).</summary>
    public static bool AllRulesPresent()
    {
        foreach (var rule in Rules)
        {
            if (!RuleExists(rule.Name)) return false;
        }
        return true;
    }

    private static bool RuleExists(string name)
    {
        try
        {
            var psi = new ProcessStartInfo("netsh.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.ArgumentList.Add("advfirewall");
            psi.ArgumentList.Add("firewall");
            psi.ArgumentList.Add("show");
            psi.ArgumentList.Add("rule");
            psi.ArgumentList.Add($"name={name}");

            using var process = Process.Start(psi);
            if (process is null) return true;
            process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            if (!process.WaitForExit(8000)) return true;

            // netsh exits with 1 when no rule matches.
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            AppLog.Error("Vérification du pare-feu impossible", ex);
            return true;
        }
    }

    /// <summary>Creates the rules in an elevated PowerShell (UAC prompt). Returns true on success.</summary>
    public static bool Repair()
    {
        var uxplayExe = Path.Combine(UxPlayLauncher.ResolvePackageDir(), "uxplay.exe");
        var programArg = File.Exists(uxplayExe)
            ? $" -Program '{uxplayExe.Replace("'", "''")}'"
            : string.Empty;

        var script = new StringBuilder();
        foreach (var (name, protocol, ports, bindProgram) in Rules)
        {
            script.AppendLine(
                $"Remove-NetFirewallRule -DisplayName '{name}' -ErrorAction SilentlyContinue");
            script.AppendLine(
                $"New-NetFirewallRule -DisplayName '{name}' -Direction Inbound -Action Allow " +
                $"-Protocol {protocol} -LocalPort {ports} -Profile Private,Domain" +
                (bindProgram ? programArg : string.Empty) + " | Out-Null");
        }

        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script.ToString()));

        try
        {
            var psi = new ProcessStartInfo("powershell.exe")
            {
                Arguments = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -EncodedCommand " + encoded,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            using var process = Process.Start(psi);
            if (process is null) return false;
            process.WaitForExit(30000);
            return process.HasExited && process.ExitCode == 0;
        }
        catch (Win32Exception)
        {
            // UAC prompt declined
            return false;
        }
        catch (Exception ex)
        {
            AppLog.Error("Réparation du pare-feu impossible", ex);
            return false;
        }
    }
}
