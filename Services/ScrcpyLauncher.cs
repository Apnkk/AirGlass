using System.Diagnostics;
using System.IO;
using System.Text;

namespace AirGlass.Services;

/// <summary>
/// Runs scrcpy.exe (separate process, own window) to mirror an Android phone over USB or Wi-Fi.
/// The process is tied to a kill-on-close job so it never outlives AirGlass.
/// </summary>
public sealed class ScrcpyLauncher : IDisposable
{
    private const int MaxOutputChars = 2000;

    private readonly object _gate = new();
    private readonly StringBuilder _output = new();
    private KillOnCloseJob? _job;
    private Process? _process;
    private bool _stopRequested;
    private bool _disposed;

    /// <summary>Raised when scrcpy exits on its own: (exit code, last output lines).</summary>
    public event Action<int, string>? Exited;

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _process is { HasExited: false };
            }
        }
    }

    /// <summary>Deployed: &lt;app&gt;\scrcpy-win. Development: walks up to external\scrcpy-win.</summary>
    internal static string ResolvePackageDir()
    {
        var baseDir = AppContext.BaseDirectory;

        var deployed = Path.Combine(baseDir, "scrcpy-win");
        if (File.Exists(Path.Combine(deployed, "scrcpy.exe")))
            return deployed;

        var dir = new DirectoryInfo(baseDir);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "external", "scrcpy-win");
            if (File.Exists(Path.Combine(candidate, "scrcpy.exe")))
                return candidate;
            dir = dir.Parent;
        }

        return deployed;
    }

    /// <summary>Starts scrcpy. Returns an error message, or null on success.</summary>
    public string? Start(int maxSize)
    {
        lock (_gate)
        {
            if (_disposed) return "Lanceur fermé.";
            if (_process is { HasExited: false }) return null;

            var packageDir = ResolvePackageDir();
            var exePath = Path.Combine(packageDir, "scrcpy.exe");
            if (!File.Exists(exePath))
                return $"scrcpy.exe introuvable : {exePath}";

            _output.Clear();
            _stopRequested = false;

            var info = new ProcessStartInfo(exePath)
            {
                WorkingDirectory = packageDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            info.ArgumentList.Add("--window-title=AirGlass Android");
            info.ArgumentList.Add($"--max-size={Math.Max(0, maxSize)}");

            try
            {
                var process = new Process { StartInfo = info, EnableRaisingEvents = true };
                process.OutputDataReceived += (_, e) => AppendOutput(e.Data);
                process.ErrorDataReceived += (_, e) => AppendOutput(e.Data);
                process.Exited += OnProcessExited;

                if (!process.Start())
                {
                    process.Dispose();
                    return "scrcpy n'a pas démarré.";
                }

                try
                {
                    _job ??= new KillOnCloseJob();
                    _job.Assign(process);
                }
                catch (Exception ex)
                {
                    AppLog.Error("scrcpy : job object indisponible", ex);
                }

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                _process = process;
                return null;
            }
            catch (Exception ex)
            {
                AppLog.Error("scrcpy : démarrage impossible", ex);
                return ex.Message;
            }
        }
    }

    public void Stop()
    {
        Process? process;
        lock (_gate)
        {
            _stopRequested = true;
            process = _process;
            _process = null;
        }

        if (process is null) return;
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            AppLog.Error("scrcpy : arrêt impossible", ex);
        }
        finally
        {
            process.Dispose();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }

        Stop();
        _job?.Dispose();
        _job = null;
    }

    private void AppendOutput(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        lock (_gate)
        {
            _output.AppendLine(line);
            if (_output.Length > MaxOutputChars)
                _output.Remove(0, _output.Length - MaxOutputChars);
        }
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        int code;
        string output;
        bool requested;
        lock (_gate)
        {
            requested = _stopRequested || _disposed;
            try { code = (sender as Process)?.ExitCode ?? -1; }
            catch (InvalidOperationException) { code = -1; }
            output = _output.ToString().Trim();
        }

        if (requested) return;
        Exited?.Invoke(code, output);
    }
}