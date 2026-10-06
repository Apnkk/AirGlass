using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AirGlass.Services;

/// <summary>System tray icon (WinForms NotifyIcon) with Open / Quit menu.</summary>
public sealed class TrayService : IDisposable
{
    private const int MaxTooltipLength = 63;

    private readonly NotifyIcon _notifyIcon;
    private readonly Icon _icon;

    public event Action? OpenRequested;
    public event Action? ExitRequested;

    public TrayService(string tooltip)
    {
        _icon = CreateIcon();

        var menu = new ContextMenuStrip();
        menu.Items.Add("Ouvrir AirGlass", null, (_, _) => OpenRequested?.Invoke());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quitter", null, (_, _) => ExitRequested?.Invoke());

        _notifyIcon = new NotifyIcon
        {
            Icon = _icon,
            Text = Truncate(tooltip),
            ContextMenuStrip = menu,
            Visible = false,
        };
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) OpenRequested?.Invoke();
        };
    }

    public void Show() => _notifyIcon.Visible = true;

    public void SetTooltip(string text) => _notifyIcon.Text = Truncate(text);

    public void Notify(string title, string text) =>
        _notifyIcon.ShowBalloonTip(3000, title, text, ToolTipIcon.Info);

    private static string Truncate(string text) =>
        text.Length <= MaxTooltipLength ? text : text[..MaxTooltipLength];

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    private static GraphicsPath RoundedRect(RectangleF rect, float radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>Draws the app logo (dark tile, screen outline, accent triangle) at runtime.</summary>
    private static Icon CreateIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            using var tile = RoundedRect(new RectangleF(0.5f, 0.5f, 31f, 31f), 8f);
            using var tileFill = new LinearGradientBrush(
                new RectangleF(0, 0, 32, 32),
                Color.FromArgb(0x24, 0x2A, 0x3C),
                Color.FromArgb(0x0C, 0x0E, 0x14),
                45f);
            g.FillPath(tileFill, tile);
            using var edge = new Pen(Color.FromArgb(0x3A, 0x41, 0x55), 1f);
            g.DrawPath(edge, tile);

            using var screenPen = new Pen(Color.FromArgb(0xF2, 0xF4, 0xF8), 2f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round,
            };
            g.DrawLines(screenPen, new[]
            {
                new PointF(12.5f, 21.5f),
                new PointF(7.5f, 21.5f),
                new PointF(7.5f, 9.5f),
                new PointF(24.5f, 9.5f),
                new PointF(24.5f, 21.5f),
                new PointF(19.5f, 21.5f),
            });

            using var accent = new SolidBrush(Color.FromArgb(0x3D, 0x8B, 0xFF));
            g.FillPolygon(accent, new[]
            {
                new PointF(16f, 17.5f),
                new PointF(21.5f, 24.5f),
                new PointF(10.5f, 24.5f),
            });
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(handle);
            return (Icon)temp.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
        _icon.Dispose();
    }
}