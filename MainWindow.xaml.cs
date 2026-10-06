using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AirGlass.Services;

namespace AirGlass;

/// <summary>
/// Main window: receiver status, firewall banner, tray integration.
/// The window is created hidden; <see cref="Begin"/> is called by App on startup.
/// </summary>
public partial class MainWindow : Window
{
    private AppSettings _settings = new();
    private UxPlayLauncher? _launcher;
    private ScrcpyLauncher? _scrcpy;
    private bool _androidMode;
    private TrayService? _tray;
    private bool _begun;
    private bool _exiting;
    private System.Windows.Threading.DispatcherTimer? _videoTimer;
    private System.Windows.Threading.DispatcherTimer? _networkTimer;
    private string? _lastLanIp;
    private bool _forceRestart;

    public MainWindow()
    {
        InitializeComponent();

        // Borderless maximized windows overflow the screen by the resize border: compensate.
        StateChanged += (_, _) =>
            RootGrid.Margin = WindowState == WindowState.Maximized ? new Thickness(8) : new Thickness(0);

        // The embedded uxplay window is only resized on demand: keep it in sync while the
        // window animates between the status view and the portrait video layout.
        SizeChanged += (_, _) =>
        {
            if (VideoPanel.Visibility == Visibility.Visible) VideoView.ResizeChildren();
        };
    }

    /// <summary>Starts services. Shows the window unless launched hidden (autostart).</summary>
    public void Begin(bool startHidden)
    {
        if (_begun) return;
        _begun = true;

        _settings = AppSettings.Load();

        if (_settings.StartWithWindows)
        {
            // Refresh the registered path in case the app was moved or updated.
            try { StartupRegistration.Set(true); }
            catch (Exception ex) { AppLog.Error("Mise à jour du démarrage Windows impossible", ex); }
        }

        Application.Current.SessionEnding += (_, _) => _exiting = true;

        _androidMode = ResolveMode(startHidden) == ModeWindow.Android;
        if (_androidMode)
        {
            ReceiverLabel.Text = "Mode";
            ReceiverName.Text = "Android (USB)";
            FooterHint.Text = "Android : branche le téléphone en USB, débogage USB activé, puis accepte l'autorisation sur l'écran";
        }

        UpdateSwitchButton();
        CreateTray();
        PrepareVideoSurface(startHidden && _settings.StartMinimized);
        if (_androidMode) StartAndroid();
        else
        {
            StartReceiver();
            _ = CheckFirewallAsync();
        }

        _videoTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _videoTimer.Tick += (_, _) => PollVideo();
        _videoTimer.Start();

        _keyTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        _keyTimer.Tick += (_, _) => OnKeyTick();
        _keyTimer.Start();

        StartBackgroundWatchers();

        var hidden = startHidden && _settings.StartMinimized;
        if (!hidden) Show();
    }

    public void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    private void CreateTray()
    {
        _tray = new TrayService("AirGlass");
        _tray.OpenRequested += () => OnUi(ShowFromTray);
        _tray.ExitRequested += () => OnUi(ExitApp);
        _tray.Show();
    }

    private void OnUi(Action action) => Dispatcher.BeginInvoke(action);

    private bool _videoSessionActive;
    private string _lastVideoDesc = string.Empty;
    private System.Windows.Size? _savedWindowSize;
    private double _savedMinWidth;
    private double? _lastVideoAspect;

    /// <summary>Embeds uxplay's video window when a mirroring session starts, restores the status view when it ends.</summary>
    private void PrepareVideoSurface(bool keepHidden)
    {
        try
        {
            // The HwndHost only gets its native handle once the window has been shown.
            // No UXPLAY_PARENT_HWND: the d3d11 swapchain cannot target a window owned by another
            // process. uxplay opens its own window and PollVideo re-parents it with SetParent.
            VideoPanel.Visibility = Visibility.Visible;
            Show();
            UpdateLayout();
            VideoPanel.Visibility = Visibility.Collapsed;
            if (keepHidden) Hide();
        }
        catch (Exception ex)
        {
            AppLog.Error("Surface vidéo indisponible", ex);
            UxPlayLauncher.ParentWindowHandle = IntPtr.Zero;
            VideoPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void PollVideo()
    {
        if (_connectIntro) return;

        if (UxPlayLauncher.ParentWindowHandle != IntPtr.Zero)
        {
            CheckSessionWatchdog();

            if (VideoPanel.Visibility == Visibility.Visible)
            {
                VideoView.ResizeChildren();
                var desc = $"window visible={IsVisible} state={WindowState} panel={VideoPanel.ActualWidth:0}x{VideoPanel.ActualHeight:0} {VideoView.Describe()}";
                if (desc != _lastVideoDesc)
                {
                    _lastVideoDesc = desc;
                    AppLog.Write("[video] " + desc);
                }
            }
            return;
        }

        if (VideoView.HasChild)
        {
            if (!VideoView.ChildAlive)
            {
                VideoView.Release();
                VideoPanel.Visibility = Visibility.Collapsed;
                ExitVideoLayout();
                _introPlayed = false;
                ApplyState(LauncherState.Running, "Prêt à recevoir");
            }
            return;
        }

        if (!IsVisible) return;
        var pid = _launcher?.ProcessId;
        if (pid is null) return;

        var window = VideoHost.FindWindowOfProcess(pid.Value);
        if (window == IntPtr.Zero) return;

        // Play the connection animation first; the next poll tick embeds the video.
        if (!_introPlayed)
        {
            StartIntroOnce();
            return;
        }

        VideoPanel.Visibility = Visibility.Visible;
        VideoPanel.UpdateLayout();
        var aspect = VideoHost.GetAspect(window);
        if (VideoView.Embed(window))
            EnterVideoLayout(aspect);
        else
            VideoPanel.Visibility = Visibility.Collapsed;
    }

    private const int WatchdogMissingTicks = 4; // 4 x 500 ms without video window = session over
    private int _missingChildTicks;
    private bool _childSeen;

    /// <summary>
    /// Fallback for log-based end detection: if the embedded video window existed and then vanished
    /// (uxplay crashed or closed it), end the session even when no expected log line was printed.
    /// </summary>
    private void CheckSessionWatchdog()
    {
        if (!_videoSessionActive || VideoPanel.Visibility != Visibility.Visible)
        {
            _childSeen = false;
            _missingChildTicks = 0;
            return;
        }

        if (VideoView.HasNativeChild)
        {
            _childSeen = true;
            _missingChildTicks = 0;
            return;
        }

        if (!_childSeen || ++_missingChildTicks < WatchdogMissingTicks) return;

        AppLog.Write("[video] watchdog : fenêtre vidéo disparue, fin de session");
        _videoSessionActive = false;
        _childSeen = false;
        _missingChildTicks = 0;
        _introPlayed = false;
        VideoPanel.Visibility = Visibility.Collapsed;
        ExitVideoLayout();
        RecycleVideoSession();
    }

    private static readonly System.Text.RegularExpressions.Regex VideoSizeRegex = new(
        @"begin video stream wxh = (\d+)x(\d+)",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static readonly System.Text.RegularExpressions.Regex PinCodeRegex = new(
        "ENTER PIN = \"(\\d{4})\"",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static readonly System.Text.RegularExpressions.Regex DeviceRequestRegex = new(
        @"connection request from (.+?) \(.*?\) with deviceID = ([0-9A-Fa-f:]{17})",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private string? _lastDeviceId;
    private string? _lastDeviceName;

    /// <summary>Shows the one-time PIN uxplay generated so it can be typed on the iPhone.</summary>
    private void ShowPinCode(string code)
    {
        ShowFromTray();
        ApplyStatus("Code PIN : " + code,
            "Saisis ce code sur l'iPhone pour te connecter.",
            Res("AccentColor"), pulse: true);
    }

    private static bool TryParseVideoSize(string line, out double aspect)
    {
        aspect = 0;
        var match = VideoSizeRegex.Match(line);
        if (!match.Success) return false;
        if (!int.TryParse(match.Groups[1].Value, out var w) || !int.TryParse(match.Groups[2].Value, out var h)) return false;
        if (w <= 0 || h <= 0) return false;
        aspect = (double)w / h;
        return true;
    }

    /// <summary>Resizes the window to match the video ratio (portrait or landscape) so the phone screen is shown large.</summary>
    private void EnterVideoLayout(double? videoAspect = null)
    {
        if (_savedWindowSize is not null || WindowState != WindowState.Normal) return;

        Topmost = _settings.KeepOnTopWhileMirroring;
        _savedWindowSize = new System.Windows.Size(Width, Height);
        _savedMinWidth = MinWidth;
        MinWidth = VideoMinWidth;
        ApplyVideoSize(videoAspect);
    }

    /// <summary>Re-fits the window to a new video ratio while a mirroring session is already displayed.</summary>
    private void UpdateVideoLayout(double videoAspect)
    {
        if (_savedWindowSize is null || WindowState != WindowState.Normal || _fullscreen) return;
        ApplyVideoSize(videoAspect);
    }

    private void ApplyVideoSize(double? videoAspect)
    {
        var area = SystemParameters.WorkArea;
        // Video area = window height minus the title bar; width follows the video ratio (no black bars).
        var aspect = videoAspect is > 0.2 and < 5 ? videoAspect.Value : DefaultVideoAspect;
        var targetHeight = Math.Min(area.Height * VideoHeightRatio, VideoMaxHeight);
        var targetWidth = (targetHeight - TitleBarHeight) * aspect;

        // Landscape: never exceed the work area width, shrink the height to keep the ratio.
        var maxWidth = area.Width * VideoHeightRatio;
        if (targetWidth > maxWidth)
        {
            targetWidth = maxWidth;
            targetHeight = targetWidth / aspect + TitleBarHeight;
        }

        targetWidth = Math.Max(MinWidth, targetWidth);
        var targetLeft = Math.Max(area.Left, Math.Min(Left + (ActualWidth - targetWidth) / 2, area.Right - targetWidth));
        var targetTop = area.Top + (area.Height - targetHeight) / 2;
        AnimateWindow(targetWidth, targetHeight, targetLeft, targetTop, null);
    }

    /// <summary>Restores the window size used before the mirroring session.</summary>
    private void ExitVideoLayout()
    {
        ExitFullscreen();
        Topmost = false;
        if (_savedWindowSize is not { } size) return;

        _savedWindowSize = null;
        var minWidth = _savedMinWidth;
        var area = SystemParameters.WorkArea;
        var targetLeft = Left + (ActualWidth - size.Width) / 2;
        var targetTop = area.Top + (area.Height - size.Height) / 2;
        AnimateWindow(size.Width, size.Height, targetLeft, targetTop, () => MinWidth = minWidth);
    }

    private const int WindowAnimationMs = 420;
    private const double TitleBarHeight = 40;
    private const double VideoMinWidth = 360;
    private const double VideoHeightRatio = 0.92;
    private const double VideoMaxHeight = 1100;
    private const double DefaultVideoAspect = 498.0 / 1080.0;

    /// <summary>Smoothly moves/resizes the window; jumps straight to the target when animations are disabled in Windows.</summary>
    private void AnimateWindow(double width, double height, double left, double top, Action? done)
    {
        if (!SystemParameters.ClientAreaAnimation)
        {
            Width = width;
            Height = height;
            Left = left;
            Top = top;
            done?.Invoke();
            return;
        }

        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        var duration = TimeSpan.FromMilliseconds(WindowAnimationMs);
        AnimateProperty(WidthProperty, width, ease, duration, done);
        AnimateProperty(HeightProperty, height, ease, duration, null);
        AnimateProperty(LeftProperty, left, ease, duration, null);
        AnimateProperty(TopProperty, top, ease, duration, null);
    }

    private void AnimateProperty(DependencyProperty property, double to, IEasingFunction ease, TimeSpan duration, Action? done)
    {
        var animation = new DoubleAnimation { To = to, Duration = duration, EasingFunction = ease };
        animation.Completed += (_, _) =>
        {
            // Release the animation clock and keep the final value as a normal local value.
            BeginAnimation(property, null);
            SetValue(property, to);
            done?.Invoke();
        };
        BeginAnimation(property, animation);
    }

    private void OnLauncherLog(string line)
    {
        var deviceMatch = DeviceRequestRegex.Match(line);
        if (deviceMatch.Success)
        {
            _lastDeviceName = deviceMatch.Groups[1].Value;
            _lastDeviceId = deviceMatch.Groups[2].Value.ToUpperInvariant();
        }

        var pinMatch = PinCodeRegex.Match(line);
        if (pinMatch.Success)
        {
            ShowPinCode(pinMatch.Groups[1].Value);
            return;
        }

        // uxplay announces the size on every new SPS: at session start and on each phone rotation.
        // Handled before the mode checks so it works with and without UXPLAY_PARENT_HWND.
        if (TryParseVideoSize(line, out var parsedAspect))
        {
            _lastVideoAspect = parsedAspect;
            OnUi(() => UpdateVideoLayout(parsedAspect));
            return;
        }

        if (UxPlayLauncher.ParentWindowHandle == IntPtr.Zero)
        {
            if (line.Contains("Begin streaming to GStreamer video pipeline", StringComparison.Ordinal))
                StartIntroOnce();
            return;
        }

        if (line.Contains("Begin streaming to GStreamer video pipeline", StringComparison.Ordinal))
        {
            _videoSessionActive = true;
            PlayConnectedIntro(() =>
            {
                if (!_videoSessionActive) return;
                VideoPanel.Visibility = Visibility.Visible;
                VideoPanel.UpdateLayout();
                VideoView.ResizeChildren();
                EnterVideoLayout(_lastVideoAspect);
            });
        }
        else if (line.Contains("raop_rtp_mirror->running is no longer true", StringComparison.Ordinal) ||
                 line.Contains("Connection closed on socket", StringComparison.Ordinal) ||
                 line.Contains("UxPlay s'est arrêté", StringComparison.Ordinal))
        {
            VideoPanel.Visibility = Visibility.Collapsed;

            // Several log lines announce the same end of session: recycle only once per session.
            if (_videoSessionActive)
            {
                _videoSessionActive = false;
                _lastVideoAspect = null;
                OnUi(() =>
                {
                    ExitVideoLayout();
                    RecycleVideoSession();
                });
            }
        }
    }

    /// <summary>
    /// uxplay reads UXPLAY_PARENT_HWND once and reuses that HWND for every renderer re-init, so the
    /// d3d11 swapchain of the previous session stays bound to it. After each mirroring session we
    /// therefore stop uxplay, build a brand-new host window and start uxplay again with the new HWND.
    /// </summary>
    private void RecycleVideoSession()
    {
        if (UxPlayLauncher.ParentWindowHandle == IntPtr.Zero) return;

        // Kill uxplay first so nothing still draws into the old HWND while it is destroyed.
        _launcher?.Stop();
        RecreateVideoSurface();
        StartReceiver();
    }

    private void RecreateVideoSurface()
    {
        try
        {
            var old = VideoView;
            old.Release();

            var fresh = new VideoHost();
            VideoPanel.Child = fresh;
            old.Dispose();
            VideoView = fresh;

            // The HwndHost only gets its native handle once it is shown in a visible window.
            var wasVisible = IsVisible;
            VideoPanel.Visibility = Visibility.Visible;
            if (!wasVisible) Show();
            UpdateLayout();
            UxPlayLauncher.ParentWindowHandle = VideoView.HostHandle;
            VideoPanel.Visibility = Visibility.Collapsed;
            if (!wasVisible) Hide();
        }
        catch (Exception ex)
        {
            AppLog.Error("Recréation de la surface vidéo impossible", ex);
            UxPlayLauncher.ParentWindowHandle = IntPtr.Zero;
            VideoPanel.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>Restarts the receiver on network change / resume from sleep.</summary>
    private void StartBackgroundWatchers()
    {
        _lastLanIp = LanAddressSelector.FindBestIPv4();

        // Debounce: Windows fires several network events in a row.
        _networkTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _networkTimer.Tick += (_, _) => OnNetworkSettled();

        System.Net.NetworkInformation.NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;
        Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    private void OnNetworkAddressChanged(object? sender, EventArgs e) => OnUi(RestartNetworkTimer);

    private void OnPowerModeChanged(object? sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        if (e.Mode != Microsoft.Win32.PowerModes.Resume) return;
        OnUi(() =>
        {
            _forceRestart = true;
            RestartNetworkTimer();
        });
    }

    private void RestartNetworkTimer()
    {
        _networkTimer?.Stop();
        _networkTimer?.Start();
    }

    private void OnNetworkSettled()
    {
        _networkTimer?.Stop();
        var force = _forceRestart;
        _forceRestart = false;

        var ip = LanAddressSelector.FindBestIPv4();
        if (ip is null)
        {
            _lastLanIp = null;
            return;
        }

        if (!force && string.Equals(ip, _lastLanIp, StringComparison.Ordinal)) return;

        _lastLanIp = ip;
        AppLog.Write($"[network] adresse LAN {ip}, redémarrage du récepteur");
        StartReceiver();
    }

    private void ExitApp()
    {
        _exiting = true;
        Close();
    }

    /// <summary>Remembered mode wins. A hidden autostart never blocks on the chooser (defaults to Apple).</summary>
    private string ResolveMode(bool startHidden)
    {
        var remembered = _settings.RememberedMode;
        if (remembered is ModeWindow.Apple or ModeWindow.Android) return remembered;
        if (startHidden && _settings.StartMinimized) return ModeWindow.Apple;

        var dialog = new ModeWindow();
        if (dialog.ShowDialog() != true || dialog.Choice is null) return ModeWindow.Apple;

        if (dialog.Remember)
        {
            _settings.RememberedMode = dialog.Choice;
            _settings.Save();
        }
        return dialog.Choice;
    }

    private void StartAndroid()
    {
        ApplyStatus("Android",
            "Branche ton téléphone en USB, débogage USB activé. La fenêtre scrcpy s'ouvre toute seule.",
            Res("AccentColor"), pulse: false);

        _scrcpy = new ScrcpyLauncher();
        _scrcpy.Exited += (code, output) => OnUi(() => OnScrcpyExited(code, output));

        var error = _scrcpy.Start(_settings.QualityHeight);
        if (error is not null)
            ApplyStatus("Android indisponible", error, Res("DangerColor"), pulse: false);
    }

    private void OnScrcpyExited(int code, string output)
    {
        var lastLine = output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault();

        if (code == 0)
        {
            ApplyStatus("Session Android terminée", "Relance AirGlass pour reconnecter.",
                Res("AccentColor"), pulse: false);
            return;
        }

        var detail = string.IsNullOrEmpty(lastLine) ? $"scrcpy s'est arrêté (code {code})." : lastLine;
        if (detail.Contains("Server connection failed", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("Could not find any ADB device", StringComparison.OrdinalIgnoreCase))
        {
            detail = "Aucun téléphone Android détecté. Vérifie le câble de données, le débogage USB "
                + "et l'autorisation sur le téléphone, puis relance AirGlass.";
        }

        ApplyStatus("Android déconnecté", detail, Res("DangerColor"), pulse: false);
    }

    private void UpdateSwitchButton()
    {
        // Show the logo of the mode we would switch TO.
        SwitchAppleLogo.Visibility = _androidMode ? Visibility.Visible : Visibility.Collapsed;
        SwitchAndroidLogo.Visibility = _androidMode ? Visibility.Collapsed : Visibility.Visible;

        var label = _androidMode ? "Passer sur Apple" : "Passer sur Android";
        SwitchModeButton.ToolTip = label;
        System.Windows.Automation.AutomationProperties.SetName(SwitchModeButton, label);
    }

    private void OnSwitchModeClick(object sender, RoutedEventArgs e)
    {
        var toAndroid = !_androidMode;

        // Tear down the current mode first.
        _videoSessionActive = false;
        _introPlayed = false;
        _connectIntro = false;
        if (_androidMode)
        {
            _scrcpy?.Dispose();
            _scrcpy = null;
        }
        else
        {
            _launcher?.Stop();
        }
        VideoView.Release();
        VideoPanel.Visibility = Visibility.Collapsed;
        ExitVideoLayout();

        _androidMode = toAndroid;

        // Keep the remembered choice in sync so the next launch opens in this mode.
        if (_settings.RememberedMode is ModeWindow.Apple or ModeWindow.Android)
        {
            _settings.RememberedMode = toAndroid ? ModeWindow.Android : ModeWindow.Apple;
            _settings.Save();
        }

        if (toAndroid)
        {
            ReceiverLabel.Text = "Mode";
            ReceiverName.Text = "Android (USB)";
            FooterHint.Text = "Android : branche le téléphone en USB, débogage USB activé, puis accepte l'autorisation sur l'écran";
            StartAndroid();
        }
        else
        {
            ReceiverLabel.Text = "Nom affiché sur l'iPhone";
            FooterHint.Text = "Sur l'iPhone : Centre de contrôle → Recopie de l'écran → choisis ce PC";
            StartReceiver();
            _ = CheckFirewallAsync();
        }

        UpdateSwitchButton();
    }

    private void StartReceiver()
    {
        if (_androidMode) return;

        var name = _settings.EffectiveReceiverName;
        ReceiverName.Text = name;

        if (_launcher is null)
        {
            _launcher = new UxPlayLauncher(name);
            ApplyLauncherOptions();
            _launcher.Log += line => OnUi(() => OnLauncherLog(line));
            _launcher.StateChanged += (state, message) => OnUi(() => ApplyState(state, message));
            _launcher.Start();
        }
        else
        {
            ApplyLauncherOptions();
            _launcher.Restart(name);
        }
    }

    private void ApplyLauncherOptions()
    {
        if (_launcher is null) return;
        _launcher.NoHold = _settings.NewClientReplacesCurrent;
        _launcher.MaxFps = _settings.Fps60 ? 60 : 0;
        _launcher.QualityHeight = _settings.QualityHeight;
        _launcher.LowLatency = _settings.LowLatency;
        _launcher.AudioOff = _settings.AudioOff;
        var pinOn = _settings.UsePin;
        var fixedPin = pinOn && AppSettings.IsValidPin(_settings.Pin) ? _settings.Pin : null;
        _launcher.Pin = fixedPin;
        _launcher.RandomPin = pinOn && fixedPin is null;
        _launcher.BlockedDeviceIds = _settings.BlockedDevices.Select(d => d.Id).ToArray();
    }

    private void ApplyState(LauncherState state, string message)
    {
        // uxplay events must not overwrite the Android status after a mode switch.
        if (_androidMode) return;

        switch (state)
        {
            case LauncherState.Running:
                ApplyStatus(message,
                    "Ouvre le Centre de contrôle de ton iPhone, touche « Recopie de l'écran », "
                        + $"puis choisis « {_settings.EffectiveReceiverName} » dans la liste.",
                    Res("Accent2Color"), pulse: true);
                break;
            case LauncherState.Starting:
                ApplyStatus("Démarrage…", message, Res("AccentColor"), pulse: false);
                break;
            case LauncherState.Restarting:
                ApplyStatus("Redémarrage du récepteur…", message, Res("AccentColor"), pulse: false);
                break;
            case LauncherState.MissingBinary:
                ApplyStatus("Composant manquant",
                    message + " Réinstalle AirGlass.", Res("DangerColor"), pulse: false);
                break;
            case LauncherState.Failed:
                ApplyStatus("Le récepteur ne démarre pas", message, Res("DangerColor"), pulse: false);
                break;
            default:
                ApplyStatus("Récepteur arrêté", message, Res("DangerColor"), pulse: false);
                break;
        }
    }

    private bool _connectIntro;
    private bool _introPlayed;

    private void StartIntroOnce()
    {
        if (_introPlayed) return;
        _introPlayed = true;
        PlayConnectedIntro(() => { });
    }

    private const int ConnectIntroMs = 900;

    /// <summary>Green ring pop + one-shot shockwave, then calls <paramref name="done"/> (shows the video).</summary>
    private void PlayConnectedIntro(Action done)
    {
        ApplyStatus("Connecté", "Ouverture de l'écran…", Color.FromRgb(0x28, 0xC8, 0x40), pulse: false);
        StatusGlyph.Text = "\uE73E";

        if (!SystemParameters.ClientAreaAnimation)
        {
            done();
            return;
        }

        _connectIntro = true;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var burst = TimeSpan.FromMilliseconds(800);

        StatusHaloScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.0, 2.1, burst) { EasingFunction = ease });
        StatusHaloScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.0, 2.1, burst) { EasingFunction = ease });
        StatusHalo.BeginAnimation(OpacityProperty, new DoubleAnimation(0.8, 0.0, burst) { EasingFunction = ease });

        var pop = TimeSpan.FromMilliseconds(500);
        var back = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 };
        StatusRingScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.85, 1.0, pop) { EasingFunction = back });
        StatusRingScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.85, 1.0, pop) { EasingFunction = back });

        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ConnectIntroMs) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _connectIntro = false;
            done();
        };
        timer.Start();
    }

    private void ApplyStatus(string title, string detail, Color color, bool pulse)
    {
        StatusGlyph.Text = "\uE7F4";
        StatusText.Text = title;
        StatusDetail.Text = detail;
        StatusRing.BorderBrush = new SolidColorBrush(color);
        StatusHalo.Stroke = new SolidColorBrush(color);
        _tray?.SetTooltip("AirGlass — " + title);

        if (pulse && SystemParameters.ClientAreaAnimation)
        {
            // Halo that expands and fades out, like a radar ping.
            var duration = TimeSpan.FromSeconds(2.2);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            StatusHaloScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.0, 1.5, duration)
            {
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = ease,
            });
            StatusHaloScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.0, 1.5, duration)
            {
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = ease,
            });
            StatusHalo.BeginAnimation(OpacityProperty, new DoubleAnimation(0.55, 0.0, duration)
            {
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = ease,
            });
        }
        else
        {
            StatusHaloScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            StatusHaloScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            StatusHalo.BeginAnimation(OpacityProperty, null);
            StatusHalo.Opacity = 0;
        }
    }

    private static Color Res(string key) => (Color)Application.Current.Resources[key];

    private async Task CheckFirewallAsync()
    {
        var ok = await Task.Run(FirewallHelper.AllRulesPresent);
        FirewallBanner.Visibility = ok ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void OnFixFirewallClick(object sender, RoutedEventArgs e)
    {
        FixFirewallButton.IsEnabled = false;
        try
        {
            var ok = await Task.Run(FirewallHelper.Repair);
            if (ok)
            {
                FirewallBanner.Visibility = Visibility.Collapsed;
            }
            else
            {
                MessageBox.Show(this,
                    "Le pare-feu n'a pas été modifié (autorisation refusée ou erreur). "
                        + "Relance et accepte la demande Windows.",
                    "AirGlass", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        finally
        {
            FixFirewallButton.IsEnabled = true;
        }
    }

    // Every close path (red button, Alt+F4, taskbar) goes through OnClosing.
    private void OnTitleClose(object sender, RoutedEventArgs e) => Close();

    private bool IsMirroring =>
        VideoPanel.Visibility == Visibility.Visible || VideoView.HasChild || _videoSessionActive;

    /// <summary>Stops the mirroring session, drops the video surface and restores the status view.</summary>
    private void EndMirroringSession()
    {
        _videoSessionActive = false;
        _introPlayed = false;
        _connectIntro = false;

        // Kill uxplay first so the iPhone is disconnected and nothing draws into the host anymore.
        _launcher?.Stop();
        VideoView.Release();
        VideoPanel.Visibility = Visibility.Collapsed;
        ExitVideoLayout();

        // Back to "ready to receive": starts a fresh receiver so the iPhone can reconnect.
        StartReceiver();
    }

    // ---- Mirroring tools: fullscreen (F11 / Esc) and screenshot (F12) ----

    private const long ToolbarIdleMs = 2000;
    private System.Windows.Threading.DispatcherTimer? _keyTimer;
    private MirrorToolbar? _toolbar;
    private System.Drawing.Point _lastCursor;
    private long _lastMoveTick;
    private long _forceToolbarUntil;
    private bool _fullscreen;
    private bool _preFullscreenTopmost;
    private Rect _preFullscreenBounds;
    private bool _f11Was;
    private bool _escWas;
    private bool _f12Was;

    /// <summary>Polled keys work even when focus sits inside the embedded native video window.</summary>
    private void OnKeyTick()
    {
        var mirroring = VideoPanel.Visibility == Visibility.Visible;
        UpdateToolbar(mirroring);

        if (!mirroring)
        {
            ExitFullscreen();
            _f11Was = _escWas = _f12Was = false;
            return;
        }

        var focused = KeyState.IsForeground(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        var f11 = focused && KeyState.IsDown(KeyState.VkF11);
        var esc = focused && KeyState.IsDown(KeyState.VkEscape);
        var f12 = focused && KeyState.IsDown(KeyState.VkF12);

        if (f11 && !_f11Was) ToggleFullscreen();
        if (esc && !_escWas) ExitFullscreen();
        if (f12 && !_f12Was) TakeScreenshot();

        _f11Was = f11;
        _escWas = esc;
        _f12Was = f12;
    }

    private void ToggleFullscreen()
    {
        if (_fullscreen)
        {
            ExitFullscreen();
            return;
        }
        if (VideoPanel.Visibility != Visibility.Visible) return;

        if (WindowState != WindowState.Normal) WindowState = WindowState.Normal;

        // Stop any running size animation so it cannot override the fullscreen bounds.
        BeginAnimation(WidthProperty, null);
        BeginAnimation(HeightProperty, null);
        BeginAnimation(LeftProperty, null);
        BeginAnimation(TopProperty, null);

        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        var bounds = System.Windows.Forms.Screen.FromHandle(handle).Bounds;
        var dpi = VisualTreeHelper.GetDpi(this);

        _preFullscreenBounds = new Rect(Left, Top, Width, Height);
        _preFullscreenTopmost = Topmost;
        _fullscreen = true;
        _toolbar?.SetFullscreen(true);

        RootGrid.RowDefinitions[0].Height = new GridLength(0);
        Topmost = true;
        Left = bounds.Left / dpi.DpiScaleX;
        Top = bounds.Top / dpi.DpiScaleY;
        Width = bounds.Width / dpi.DpiScaleX;
        Height = bounds.Height / dpi.DpiScaleY;
    }

    private void ExitFullscreen()
    {
        if (!_fullscreen) return;
        _fullscreen = false;
        _toolbar?.SetFullscreen(false);

        RootGrid.RowDefinitions[0].Height = new GridLength(40);
        Topmost = _preFullscreenTopmost;
        Left = _preFullscreenBounds.Left;
        Top = _preFullscreenBounds.Top;
        Width = _preFullscreenBounds.Width;
        Height = _preFullscreenBounds.Height;
    }

    private void TakeScreenshot()
    {
        _forceToolbarUntil = 0;
        _lastMoveTick = 0;

        if (!HideToolbar())
        {
            DoScreenshot();
            return;
        }

        // Let the overlay leave the screen before grabbing pixels.
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            DoScreenshot();
        };
        timer.Start();
    }

    private void DoScreenshot()
    {
        if (VideoPanel.Visibility != Visibility.Visible || VideoView.ActualWidth < 2 || VideoView.ActualHeight < 2)
            return;

        try
        {
            var dpi = VisualTreeHelper.GetDpi(VideoView);
            var origin = VideoView.PointToScreen(new System.Windows.Point(0, 0));
            var path = ScreenCapture.SaveRegion(
                (int)origin.X, (int)origin.Y,
                (int)Math.Round(VideoView.ActualWidth * dpi.DpiScaleX),
                (int)Math.Round(VideoView.ActualHeight * dpi.DpiScaleY));
            AppLog.Write("[capture] " + path);
            ShowToolHint("Capture enregistrée");
        }
        catch (Exception ex)
        {
            AppLog.Error("Capture d'écran impossible", ex);
            ShowToolHint("Capture impossible");
        }
    }

    private void ShowToolHint(string text)
    {
        _forceToolbarUntil = Environment.TickCount64 + 2500;
        EnsureToolbar().ShowHint(text);
        UpdateToolbar(true);
    }

    private MirrorToolbar EnsureToolbar()
    {
        if (_toolbar is not null) return _toolbar;

        var toolbar = new MirrorToolbar { Owner = this };
        toolbar.FullscreenRequested += ToggleFullscreen;
        toolbar.CaptureRequested += TakeScreenshot;
        toolbar.QualityRequested += ChangeQuality;
        toolbar.SetQuality(_settings.QualityHeight);
        toolbar.SetFullscreen(_fullscreen);
        _toolbar = toolbar;
        return toolbar;
    }

    /// <summary>Quality is negotiated at connection time: save it, never interrupt a live mirror.</summary>
    private void ChangeQuality(int height)
    {
        if (height == _settings.QualityHeight || !AppSettings.QualityHeights.Contains(height)) return;

        _settings.QualityHeight = height;
        _settings.Save();
        _toolbar?.SetQuality(height);

        if (IsMirroring) _toolbar?.ShowHint($"{height}p à la prochaine connexion");
        else StartReceiver();
    }

    private bool HideToolbar()
    {
        if (_toolbar is null || !_toolbar.IsVisible) return false;
        _toolbar.Hide();
        return true;
    }

    /// <summary>Shows the floating bar while the cursor moves over the video, hides it after 2 s of idle.</summary>
    private void UpdateToolbar(bool mirroring)
    {
        if (!mirroring || !IsVisible || WindowState == WindowState.Minimized
            || VideoView.ActualWidth < 2 || VideoView.ActualHeight < 2)
        {
            HideToolbar();
            return;
        }

        var now = Environment.TickCount64;
        var cursor = System.Windows.Forms.Cursor.Position;
        var dpi = VisualTreeHelper.GetDpi(VideoView);
        var origin = VideoView.PointToScreen(new System.Windows.Point(0, 0));
        var width = VideoView.ActualWidth * dpi.DpiScaleX;
        var height = VideoView.ActualHeight * dpi.DpiScaleY;
        var inside = cursor.X >= origin.X && cursor.X < origin.X + width
                     && cursor.Y >= origin.Y && cursor.Y < origin.Y + height;

        if (cursor != _lastCursor)
        {
            _lastCursor = cursor;
            if (inside) _lastMoveTick = now;
        }

        var show = (inside && now - _lastMoveTick < ToolbarIdleMs)
                   || (_toolbar is { IsVisible: true, IsMenuOpen: true })
                   || (_toolbar is { IsVisible: true, IsMouseOver: true })
                   || now < _forceToolbarUntil;
        if (!show)
        {
            HideToolbar();
            return;
        }

        var toolbar = EnsureToolbar();
        if (!toolbar.IsVisible) toolbar.Show();
        toolbar.UpdateLayout();
        toolbar.Left = (origin.X + width / 2) / dpi.DpiScaleX - toolbar.ActualWidth / 2;
        toolbar.Top = origin.Y / dpi.DpiScaleY + 24;
    }

    private void OnTitleMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnTitleMaximize(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        var before = _settings.EffectiveReceiverName;
        var beforeNoHold = _settings.NewClientReplacesCurrent;
        var beforeFps60 = _settings.Fps60;
        var beforeQuality = _settings.QualityHeight;
        var beforeLowLatency = _settings.LowLatency;
        var beforeAudioOff = _settings.AudioOff;
        var beforePin = _settings.UsePin ? "on:" + _settings.Pin : "";
        var beforeBlocked = string.Join(",", _settings.BlockedDevices.Select(d => d.Id));
        var window = new SettingsWindow(_settings, _lastDeviceId, _lastDeviceName) { Owner = this };
        if (window.ShowDialog() != true) return;

        var receiverChanged =
            !string.Equals(before, _settings.EffectiveReceiverName, StringComparison.Ordinal) ||
            beforeNoHold != _settings.NewClientReplacesCurrent ||
            beforeFps60 != _settings.Fps60 ||
            beforeLowLatency != _settings.LowLatency ||
            beforeAudioOff != _settings.AudioOff ||
            beforePin != (_settings.UsePin ? "on:" + _settings.Pin : "") ||
            beforeBlocked != string.Join(",", _settings.BlockedDevices.Select(d => d.Id));
        _toolbar?.SetQuality(_settings.QualityHeight);
        var qualityChanged = beforeQuality != _settings.QualityHeight;
        // Quality alone never interrupts a live mirror; it applies at the next connection.
        if (qualityChanged && !receiverChanged && !IsMirroring) StartReceiver();
        if (receiverChanged)
        {
            // Mirroring: end the session, which restarts uxplay with the new options.
            if (IsMirroring) EndMirroringSession();
            else StartReceiver();
        }
        if (IsMirroring) Topmost = _settings.KeepOnTopWhileMirroring;
    }

    private void OnAboutClick(object sender, RoutedEventArgs e)
    {
        new AboutWindow { Owner = this }.ShowDialog();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // While mirroring, closing ends the session and brings the status view back.
        if (!_exiting && IsMirroring)
        {
            e.Cancel = true;
            EndMirroringSession();
            return;
        }

        if (!_exiting && _settings.MinimizeToTray)
        {
            e.Cancel = true;
            Hide();
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _videoTimer?.Stop();
        _keyTimer?.Stop();
        _toolbar?.Close();
        _networkTimer?.Stop();
        System.Net.NetworkInformation.NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
        Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        VideoView.Release();
        _launcher?.Dispose();
        _scrcpy?.Dispose();
        _tray?.Dispose();
        Application.Current.Shutdown();
    }
}
