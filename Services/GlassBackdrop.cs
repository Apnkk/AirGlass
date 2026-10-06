using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace AirGlass.Services;

/// <summary>
/// Attached property that gives a window the Windows 11 acrylic backdrop (real blur of what is
/// behind the window) and a dark title bar. On Windows 10 / older Windows 11 builds the window
/// keeps its solid dark background, so nothing breaks.
/// Usage: svc:GlassBackdrop.Enabled="True" on a Window.
/// </summary>
public static class GlassBackdrop
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwcpRound = 2;
    private const int DwmwaSystemBackdropType = 38;
    private const int DwmsbtTransientWindow = 3; // acrylic
    private const int MinBackdropBuild = 22621;  // Windows 11 22H2

    public static readonly DependencyProperty EnabledProperty =
        DependencyProperty.RegisterAttached(
            "Enabled", typeof(bool), typeof(GlassBackdrop),
            new PropertyMetadata(false, OnEnabledChanged));

    public static bool GetEnabled(DependencyObject obj) => (bool)obj.GetValue(EnabledProperty);

    public static void SetEnabled(DependencyObject obj, bool value) => obj.SetValue(EnabledProperty, value);

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Window window || e.NewValue is not true) return;

        if (new WindowInteropHelper(window).Handle != IntPtr.Zero)
            Apply(window);
        else
            window.SourceInitialized += OnSourceInitialized;
    }

    private static void OnSourceInitialized(object? sender, EventArgs e)
    {
        if (sender is not Window window) return;
        window.SourceInitialized -= OnSourceInitialized;
        Apply(window);
    }

    private static void Apply(Window window)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;

            // Dark title bar (Windows 10 2004+ and Windows 11). Failure is harmless.
            var dark = 1;
            DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));

            // Rounded corners (Windows 11). Ignored on older builds; square corners when maximized.
            var corners = DwmwcpRound;
            DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref corners, sizeof(int));

            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, MinBackdropBuild)) return;

            var source = HwndSource.FromHwnd(hwnd);
            if (source?.CompositionTarget is null) return;

            var backdrop = DwmsbtTransientWindow;
            if (DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref backdrop, sizeof(int)) != 0) return;

            source.CompositionTarget.BackgroundColor = Colors.Transparent;
            var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            DwmExtendFrameIntoClientArea(hwnd, ref margins);

            // Semi-transparent tint over the blur (see App.xaml).
            window.SetResourceReference(Control.BackgroundProperty, "WindowGlassBrush");
        }
        catch (Exception ex)
        {
            AppLog.Error("Effet verre indisponible", ex);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);
}
