using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Threading;
using AirGlass.Services;

namespace AirGlass;

/// <summary>
/// Floating icon bar shown over the mirrored video (fullscreen + screenshot + quality).
/// A separate non-activating window: WPF cannot draw above the embedded native video window.
/// </summary>
public partial class MirrorToolbar : Window
{
    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;

    private readonly DispatcherTimer _hintTimer;
    private int _quality;

    public event Action? FullscreenRequested;
    public event Action? CaptureRequested;
    public event Action<int>? QualityRequested;

    /// <summary>True while the quality menu is open, so the bar stays visible under the popup.</summary>
    public bool IsMenuOpen { get; private set; }

    public MirrorToolbar()
    {
        InitializeComponent();

        _hintTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _hintTimer.Tick += (_, _) =>
        {
            _hintTimer.Stop();
            HintText.Visibility = Visibility.Collapsed;
        };

        // Never take the foreground from the main window, otherwise F11/Esc/F12 polling stops working.
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            var style = GetWindowLong(handle, GwlExStyle);
            SetWindowLong(handle, GwlExStyle, style | WsExNoActivate | WsExToolWindow);
        };
    }

    public void SetFullscreen(bool fullscreen)
    {
        FullscreenGlyph.Text = fullscreen ? "\uE73F" : "\uE740";
        var tip = fullscreen ? "Quitter le plein écran (Échap)" : "Plein écran (F11)";
        FullscreenButton.ToolTip = tip;
        System.Windows.Automation.AutomationProperties.SetName(FullscreenButton, tip);
    }

    public void SetQuality(int height)
    {
        _quality = height;
        QualityLabel.Text = $"{height}p";
        var tip = $"Qualité : {height}p (reconnexion de l'iPhone nécessaire)";
        QualityButton.ToolTip = tip;
        System.Windows.Automation.AutomationProperties.SetName(QualityButton, $"Qualité {height}p");
    }

    public void ShowHint(string text)
    {
        HintText.Text = text;
        HintText.Visibility = Visibility.Visible;
        _hintTimer.Stop();
        _hintTimer.Start();
    }

    private void OnFullscreenClick(object sender, RoutedEventArgs e) => FullscreenRequested?.Invoke();

    private void OnCaptureClick(object sender, RoutedEventArgs e) => CaptureRequested?.Invoke();

    private void OnQualityClick(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = QualityButton,
            Placement = PlacementMode.Bottom,
        };

        foreach (var height in AppSettings.QualityHeights)
        {
            var item = new MenuItem
            {
                Header = height == 2160 ? "2160p (4K)" : $"{height}p",
                IsChecked = height == _quality,
            };
            var chosen = height;
            item.Click += (_, _) => QualityRequested?.Invoke(chosen);
            menu.Items.Add(item);
        }

        menu.Opened += (_, _) => IsMenuOpen = true;
        menu.Closed += (_, _) => IsMenuOpen = false;
        menu.IsOpen = true;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}