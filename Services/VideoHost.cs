using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AirGlass.Services;

/// <summary>
/// Hosts the native video window created by uxplay (GStreamer d3d11videosink)
/// inside the WPF window by re-parenting it as a child window.
/// </summary>
public sealed class VideoHost : HwndHost
{
    private const int WS_CHILD = 0x40000000;
    private const int WS_VISIBLE = 0x10000000;
    private const int WS_CLIPCHILDREN = 0x02000000;
    private const int WS_CLIPSIBLINGS = 0x04000000;
    private const long WS_POPUP = 0x80000000L;
    private const long WS_CAPTION = 0x00C00000L;
    private const long WS_THICKFRAME = 0x00040000L;
    private const long WS_SYSMENU = 0x00080000L;
    private const long WS_MINIMIZEBOX = 0x00020000L;
    private const long WS_MAXIMIZEBOX = 0x00010000L;
    private const long WS_EX_APPWINDOW = 0x00040000L;
    private const long WS_EX_WINDOWEDGE = 0x00000100L;
    private const long WS_EX_CLIENTEDGE = 0x00000200L;
    private const long WS_EX_DLGMODALFRAME = 0x00000001L;
    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;
    private const int WM_SIZE = 0x0005;
    private const int WM_PAINT = 0x000F;
    private const int WM_ERASEBKGND = 0x0014;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const uint SWP_SHOWWINDOW = 0x0040;

    private IntPtr _host;
    private IntPtr _child;

    public bool HasChild => _child != IntPtr.Zero;

    /// <summary>HWND of the host area (valid once the control has been shown at least once).</summary>
    public IntPtr HostHandle => _host;

    /// <summary>True when a window (created by uxplay/GStreamer) is parented inside the host.</summary>
    public bool HasNativeChild => _host != IntPtr.Zero && GetWindow(_host, 5) != IntPtr.Zero;

    /// <summary>Resizes the native child to fill the host.</summary>
    public void ResizeChildren() => Fit();

    public bool ChildAlive => _child != IntPtr.Zero && IsWindow(_child);

    /// <summary>One-line diagnostic of the host and its first native child (for logs).</summary>
    public string Describe()
    {
        if (_host == IntPtr.Zero) return "host=none";

        GetClientRect(_host, out var hostRect);
        var text = $"host=0x{_host.ToString("X")} valid={IsWindow(_host)} visible={IsWindowVisible(_host)} " +
                   $"client={hostRect.Right - hostRect.Left}x{hostRect.Bottom - hostRect.Top}";

        var child = GetWindow(_host, 5);
        if (child == IntPtr.Zero) return text + " child=none";

        GetWindowRect(child, out var childRect);
        return text + $" child=0x{child.ToString("X")} visible={IsWindowVisible(child)} " +
               $"size={childRect.Right - childRect.Left}x{childRect.Bottom - childRect.Top}";
    }

    /// <summary>Finds a visible top-level window owned by the given process.</summary>
    public static IntPtr FindWindowOfProcess(int processId)
    {
        var found = IntPtr.Zero;
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid != (uint)processId) return true;
            if (!IsWindowVisible(hwnd) || GetParent(hwnd) != IntPtr.Zero) return true;
            if (!GetWindowRect(hwnd, out var r) || r.Right - r.Left < 100 || r.Bottom - r.Top < 100) return true;
            found = hwnd;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    /// <summary>Width / height of an external window, or null if it cannot be measured.</summary>
    public static double? GetAspect(IntPtr window)
    {
        if (window == IntPtr.Zero || !GetWindowRect(window, out var r)) return null;
        var w = r.Right - r.Left;
        var h = r.Bottom - r.Top;
        return w > 0 && h > 0 ? (double)w / h : null;
    }

    /// <summary>Moves an external window into this host. Returns false if the host is not ready.</summary>
    public bool Embed(IntPtr window)
    {
        if (_host == IntPtr.Zero || window == IntPtr.Zero || !IsWindow(window)) return false;

        SetParent(window, _host);

        var style = GetWindowLongPtr(window, GWL_STYLE).ToInt64();
        style &= ~(WS_POPUP | WS_CAPTION | WS_THICKFRAME | WS_SYSMENU | WS_MINIMIZEBOX | WS_MAXIMIZEBOX);
        style |= WS_CHILD | WS_VISIBLE | WS_CLIPCHILDREN | WS_CLIPSIBLINGS;
        SetWindowLongPtr(window, GWL_STYLE, new IntPtr(style));

        var ex = GetWindowLongPtr(window, GWL_EXSTYLE).ToInt64();
        ex &= ~(WS_EX_APPWINDOW | WS_EX_WINDOWEDGE | WS_EX_CLIENTEDGE | WS_EX_DLGMODALFRAME);
        SetWindowLongPtr(window, GWL_EXSTYLE, new IntPtr(ex));

        _child = window;
        Fit();
        return true;
    }

    /// <summary>Lets go of the embedded window without destroying it.</summary>
    public void Release()
    {
        if (_child != IntPtr.Zero && IsWindow(_child))
        {
            ShowWindow(_child, 0);
            SetParent(_child, IntPtr.Zero);
        }
        _child = IntPtr.Zero;
    }

    private void Fit()
    {
        if (_host == IntPtr.Zero) return;
        var target = _child != IntPtr.Zero ? _child : GetWindow(_host, 5);
        if (target == IntPtr.Zero || !IsWindow(target)) return;
        if (!GetClientRect(_host, out var r)) return;
        SetWindowPos(target, IntPtr.Zero, 0, 0, Math.Max(1, r.Right - r.Left), Math.Max(1, r.Bottom - r.Top),
            SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED | SWP_SHOWWINDOW);
    }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        _host = CreateWindowEx(0, "static", string.Empty,
            WS_CHILD | WS_VISIBLE | WS_CLIPCHILDREN,
            0, 0, 1, 1, hwndParent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        return new HandleRef(this, _host);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        Release();
        DestroyWindow(hwnd.Handle);
        _host = IntPtr.Zero;
    }

    protected override IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_SIZE) Fit();

        // The d3d11 sink draws straight into this window: never let the host repaint over it.
        if (msg == WM_ERASEBKGND)
        {
            handled = true;
            return new IntPtr(1);
        }
        if (msg == WM_PAINT)
        {
            ValidateRect(hwnd, IntPtr.Zero);
            handled = true;
            return IntPtr.Zero;
        }

        return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool ValidateRect(IntPtr hwnd, IntPtr rect);

    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern IntPtr SetParent(IntPtr child, IntPtr newParent);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int cmd);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(int exStyle, string className, string windowName, int style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
}
