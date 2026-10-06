using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;

namespace AirGlass.Services;

public enum LauncherState
{
    Stopped,
    Starting,
    Running,
    Restarting,
    Failed,
    MissingBinary,
}

/// <summary>
/// Runs the native Windows uxplay.exe (separate process) as AirPlay receiver,
/// relays its output to the log and restarts it after a crash (exponential
/// backoff, gives up after repeated rapid crashes).
/// </summary>
public sealed class UxPlayLauncher : IDisposable
{
    private const int MaxRapidCrashes = 5;
    private static readonly TimeSpan RapidCrashWindow = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan HealthyRunTime = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PortRetryInterval = TimeSpan.FromSeconds(3);

    private readonly object _gate = new();
    private readonly List<DateTime> _crashTimes = new();
    private string _receiverName;
    private Process? _process;
    private CancellationTokenSource _restartCts = new();
    private DateTime _startedAtUtc;
    private bool _stopRequested = true;
    private bool _disposed;
    private string? _registryFile;
    private KillOnCloseJob? _job;

    public event Action<string>? Log;
    public event Action<LauncherState, string>? StateChanged;

    public LauncherState State { get; private set; } = LauncherState.Stopped;

    /// <summary>Drop the current connection when a new client connects (-nohold).</summary>
    public bool NoHold { get; set; }

    /// <summary>Maximum streaming framerate. 0 or 30 = uxplay default.</summary>
    public int MaxFps { get; set; }

    /// <summary>Enable H.265 and request a 3840x2160 display.</summary>
    /// <summary>Requested video height: 2160 (H.265), 1440, 1080 (default), 720 or 480.</summary>
    public int QualityHeight { get; set; } = 1080;

    /// <summary>Disable audio/video timestamp sync for lower latency (-vsync no).</summary>
    public bool LowLatency { get; set; }

    /// <summary>Video only, no audio (-as 0).</summary>
    public bool AudioOff { get; set; }

    /// <summary>4-digit PIN required from the client (-pin). Null or invalid = no PIN.</summary>
    public string? Pin { get; set; }

    /// <summary>Random PIN shown at each connection (-pin without value), used when no fixed PIN is set.</summary>
    public bool RandomPin { get; set; }

    /// <summary>deviceIDs always refused (-block).</summary>
    public string[] BlockedDeviceIds { get; set; } = Array.Empty<string>();

    /// <summary>
    /// HWND of the app's video area. When non-zero it is passed to uxplay
    /// (UXPLAY_PARENT_HWND) so the video renders inside the app window.
    /// </summary>
    public static IntPtr ParentWindowHandle { get; set; } = IntPtr.Zero;

    /// <summary>PID of the running uxplay.exe, or null when it is not running.</summary>
    public int? ProcessId
    {
        get
        {
            lock (_gate)
            {
                try { return _process is { HasExited: false } p ? p.Id : null; }
                catch { return null; }
            }
        }
    }

    public UxPlayLauncher(string receiverName)
    {
        _receiverName = string.IsNullOrWhiteSpace(receiverName) ? "AirGlass" : receiverName;

        // If the app crashes, Windows kills uxplay.exe with it.
        try { _job = new KillOnCloseJob(); }
        catch (Exception ex) { AppLog.Error("Job Object indisponible", ex); }
    }

    /// <summary>
    /// Deployed: &lt;app&gt;\uxplay-win. Development: walks up to external\uxplay-win.
    /// </summary>
    internal static string ResolvePackageDir()
    {
        var baseDir = AppContext.BaseDirectory;

        var deployed = Path.Combine(baseDir, "uxplay-win");
        if (File.Exists(Path.Combine(deployed, "uxplay.exe")))
            return deployed;

        var dir = new DirectoryInfo(baseDir);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "external", "uxplay-win");
            if (File.Exists(Path.Combine(candidate, "uxplay.exe")))
                return candidate;
            dir = dir.Parent;
        }

        return deployed;
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _stopRequested = false;
            _crashTimes.Clear();

            var exePath = Path.Combine(ResolvePackageDir(), "uxplay.exe");
            if (File.Exists(exePath)) KillStaleProcesses(exePath);

            LaunchLocked();
        }
    }

    public void Stop()
    {
        Process? process;
        lock (_gate)
        {
            _stopRequested = true;
            _restartCts.Cancel();
            _restartCts.Dispose();
            _restartCts = new CancellationTokenSource();
            process = _process;
            _process = null;
        }

        KillProcess(process);
        CleanupRegistry();
        SetState(LauncherState.Stopped, "Récepteur arrêté.");
    }

    public void Restart(string newReceiverName)
    {
        Stop();
        lock (_gate)
        {
            if (!string.IsNullOrWhiteSpace(newReceiverName))
                _receiverName = newReceiverName;
        }
        Start();
    }

    private void LaunchLocked()
    {
        var packageDir = ResolvePackageDir();
        var exePath = Path.Combine(packageDir, "uxplay.exe");

        if (!File.Exists(exePath))
        {
            SetState(LauncherState.MissingBinary, $"uxplay.exe introuvable : {exePath}");
            return;
        }

        // Fail fast with an actionable message if another program already owns the AirPlay ports.
        var portConflict = DescribePortConflict();
        if (portConflict is not null)
        {
            Emit("[launcher] " + portConflict);
            SetState(LauncherState.Failed,
                portConflict + " Le récepteur redémarrera tout seul dès que le port sera libre.");
            SchedulePortRetry(_restartCts.Token);
            return;
        }

        SetState(LauncherState.Starting, "Démarrage du récepteur…");
        CleanupRegistry();

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            WorkingDirectory = packageDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };

        var pluginDir = Path.Combine(packageDir, "lib", "gstreamer-1.0");
        if (Directory.Exists(pluginDir))
        {
            psi.Environment["GST_PLUGIN_PATH"] = pluginDir;
            psi.Environment["GST_PLUGIN_SYSTEM_PATH"] = pluginDir;
        }

        // Fresh GStreamer registry on every start (a corrupted one hides plugins).
        _registryFile = Path.Combine(Path.GetTempPath(), $"airglass-gstreg-{Guid.NewGuid():N}.bin");
        try { File.Delete(Path.Combine(packageDir, "gstreg.bin")); } catch { /* best-effort */ }
        psi.Environment["GST_REGISTRY"] = _registryFile;

        if (ParentWindowHandle != IntPtr.Zero)
        {
            psi.Environment["UXPLAY_PARENT_HWND"] = ParentWindowHandle.ToInt64().ToString();
            Emit("[launcher] vidéo intégrée à la fenêtre de l'app");
        }

        var existingPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        psi.Environment["PATH"] = packageDir + Path.PathSeparator + existingPath;

        // Advertise on the physical LAN adapter (e.g. Intel Wi-Fi), never on a VPN.
        // A value already set in the environment wins.
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("UXPLAY_MDNS_IPV4")))
        {
            var lanIp = LanAddressSelector.FindBestIPv4();
            if (lanIp is not null)
            {
                psi.Environment["UXPLAY_MDNS_IPV4"] = lanIp;
                Emit($"[launcher] mDNS lié à {lanIp}");
            }
        }

        // Fixed ports 7000-7002 so the firewall rules are predictable.
        psi.ArgumentList.Add("-n");
        psi.ArgumentList.Add(_receiverName);
        psi.ArgumentList.Add("-p");
        psi.ArgumentList.Add(AirPlayInfo.AirPlayPort.ToString());

        if (AppSettings.IsValidPin(Pin) || RandomPin)
        {
            // "-pin" only asks at first pairing. Do NOT add "-reg": it makes UxPlay re-check
            // returning clients against a register and ask for the PIN again.
            psi.ArgumentList.Add("-pin");
            if (AppSettings.IsValidPin(Pin)) psi.ArgumentList.Add(Pin);
        }
        if (NoHold) psi.ArgumentList.Add("-nohold");
        foreach (var blockedId in BlockedDeviceIds)
        {
            if (string.IsNullOrWhiteSpace(blockedId)) continue;
            psi.ArgumentList.Add("-block");
            psi.ArgumentList.Add(blockedId.Trim());
        }
        if (MaxFps > 30)
        {
            psi.ArgumentList.Add("-fps");
            psi.ArgumentList.Add(MaxFps.ToString());
        }
        var resolution = QualityHeight switch
        {
            >= 2160 => "3840x2160",
            >= 1440 => "2560x1440",
            >= 1080 => null, // UxPlay default (1920x1080)
            >= 720 => "1280x720",
            _ => "854x480",
        };
        if (QualityHeight >= 2160) psi.ArgumentList.Add("-h265");
        if (resolution is not null)
        {
            psi.ArgumentList.Add("-s");
            psi.ArgumentList.Add(resolution);
        }
        if (LowLatency)
        {
            psi.ArgumentList.Add("-vsync");
            psi.ArgumentList.Add("no");
        }
        if (AudioOff)
        {
            psi.ArgumentList.Add("-as");
            psi.ArgumentList.Add("0");
        }

        // Debug level is always on (without per-packet data) so the app sees the video size lines
        // printed on every rotation. AIRGLASS_UXPLAY_DEBUG=1 restores the fully verbose output.
        psi.ArgumentList.Add("-d");
        if (Environment.GetEnvironmentVariable("AIRGLASS_UXPLAY_DEBUG") != "1")
            psi.ArgumentList.Add("1");

        Emit($"[launcher] args: {string.Join(' ', psi.ArgumentList)}");

        Process process;
        try
        {
            process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) Emit(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) Emit(e.Data); };
            process.Exited += (_, _) => OnProcessExited(process);
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            try { _job?.Assign(process); }
            catch (Exception ex) { AppLog.Error("Rattachement au Job Object impossible", ex); }
        }
        catch (Exception ex)
        {
            AppLog.Error("Lancement de uxplay.exe impossible", ex);
            SetState(LauncherState.Failed, "Impossible de lancer uxplay.exe : " + ex.Message);
            return;
        }

        _process = process;
        _startedAtUtc = DateTime.UtcNow;
        Emit($"UxPlay démarré sous le nom « {_receiverName} ».");
        SetState(LauncherState.Running, "Prêt — en attente de connexion");
    }

    private void OnProcessExited(Process process)
    {
        var exitCode = SafeExitCode(process);
        TimeSpan delay;
        CancellationToken token;

        lock (_gate)
        {
            if (_disposed || _stopRequested || !ReferenceEquals(process, _process))
                return;

            _process = null;
            var now = DateTime.UtcNow;
            if (now - _startedAtUtc >= HealthyRunTime) _crashTimes.Clear();
            _crashTimes.Add(now);
            _crashTimes.RemoveAll(t => now - t > RapidCrashWindow);

            Emit($"UxPlay s'est arrêté (code {exitCode}).");

            if (_crashTimes.Count >= MaxRapidCrashes)
            {
                SetState(LauncherState.Failed,
                    DescribePortConflict() ??
                    $"Le récepteur s'arrête en boucle (code {exitCode}). Un autre programme utilise " +
                    "peut-être les ports 7000-7002, ou un antivirus/pare-feu bloque uxplay.exe.");
                return;
            }

            var seconds = Math.Min(2 * Math.Pow(2, _crashTimes.Count - 1), 15);
            delay = TimeSpan.FromSeconds(seconds);
            token = _restartCts.Token;
            SetState(LauncherState.Restarting,
                $"Le récepteur s'est arrêté, redémarrage dans {seconds:0} s…");
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            lock (_gate)
            {
                if (_disposed || _stopRequested) return;
                LaunchLocked();
            }
        });
    }

    /// <summary>
    /// Polls the AirPlay ports every few seconds after a port conflict and relaunches
    /// the receiver as soon as they are free. Stops on Stop()/Dispose().
    /// </summary>
    private void SchedulePortRetry(CancellationToken token)
    {
        _ = Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    await Task.Delay(PortRetryInterval, token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                lock (_gate)
                {
                    if (_disposed || _stopRequested || token.IsCancellationRequested) return;
                    if (_process is not null) return;
                    if (DescribePortConflict() is not null) continue;

                    Emit("[launcher] Port libéré, démarrage du récepteur.");
                    LaunchLocked();
                    return;
                }
            }
        });
    }

    // Process-name fragments of well-known AirPlay/mirroring receivers that compete for ports 7000-7002.
    private static readonly (string Fragment, string Display)[] KnownReceivers =
    {
        ("airserver", "AirServer"),
        ("itunes", "iTunes"),
        ("lonelyscreen", "LonelyScreen"),
        ("reflector", "Reflector"),
        ("airparrot", "AirParrot"),
        ("uxplay", "une autre instance de UxPlay"),
    };

    /// <summary>
    /// Returns a user-facing message when a TCP port in 7000-7002 is already listening, else null.
    /// Never throws: if the check itself fails, the launch simply proceeds.
    /// </summary>
    private static string? DescribePortConflict()
    {
        try
        {
            var listeners = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();
            var busyPorts = new List<int>();
            for (var port = AirPlayInfo.AirPlayPort; port <= AirPlayInfo.AirPlayPort + 2; port++)
            {
                if (listeners.Any(l => l.Port == port)) busyPorts.Add(port);
            }
            if (busyPorts.Count == 0) return null;

            var suspects = new List<string>();
            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    var name = proc.ProcessName.ToLowerInvariant();
                    foreach (var (fragment, display) in KnownReceivers)
                    {
                        if (name.Contains(fragment) && !suspects.Contains(display))
                            suspects.Add(display);
                    }
                }
                catch
                {
                    // process exited while enumerating
                }
                finally
                {
                    proc.Dispose();
                }
            }

            var ports = string.Join(", ", busyPorts);
            var message = $"Le port {ports} est déjà utilisé par un autre programme, " +
                          "AirGlass ne peut pas recevoir l'écran de l'iPhone.";
            message += suspects.Count > 0
                ? $" Programme(s) probable(s) : {string.Join(", ", suspects)}. Ferme-le puis relance le récepteur."
                : " Ferme tout autre récepteur AirPlay (AirServer, iTunes, autre logiciel de mirroring) puis relance le récepteur.";
            return message;
        }
        catch (Exception ex)
        {
            AppLog.Error("Vérification des ports AirPlay impossible", ex);
            return null;
        }
    }

    private void KillStaleProcesses(string exePath)
    {
        var expected = Path.GetFullPath(exePath);
        foreach (var candidate in Process.GetProcessesByName("uxplay"))
        {
            try
            {
                var path = candidate.MainModule?.FileName;
                if (path is not null &&
                    string.Equals(Path.GetFullPath(path), expected, StringComparison.OrdinalIgnoreCase))
                {
                    candidate.Kill(entireProcessTree: true);
                    candidate.WaitForExit(2000);
                    Emit("Ancien processus uxplay.exe arrêté.");
                }
            }
            catch
            {
                // access denied or already gone
            }
            finally
            {
                candidate.Dispose();
            }
        }
    }

    private static void KillProcess(Process? process)
    {
        if (process is null) return;
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(3000);
            }
        }
        catch
        {
            // best-effort shutdown
        }

        try { process.Dispose(); } catch { /* ignore */ }
    }

    private void CleanupRegistry()
    {
        var file = _registryFile;
        _registryFile = null;
        if (file is null) return;
        try { File.Delete(file); } catch { /* best-effort */ }
    }

    private static int SafeExitCode(Process process)
    {
        try { return process.ExitCode; }
        catch { return -1; }
    }

    private void Emit(string line)
    {
        AppLog.Write(line);
        Log?.Invoke(line);
    }

    private void SetState(LauncherState state, string message)
    {
        State = state;
        AppLog.Write($"[state] {state}: {message}");
        StateChanged?.Invoke(state, message);
    }

    public void Dispose()
    {
        Process? process;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _stopRequested = true;
            _restartCts.Cancel();
            process = _process;
            _process = null;
        }

        KillProcess(process);
        CleanupRegistry();
        _restartCts.Dispose();
        _job?.Dispose();
        _job = null;
    }
}
