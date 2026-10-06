using System.Runtime.InteropServices;

namespace AirGlass.Services;

/// <summary>Polls keyboard state even when focus is inside the embedded native video window.</summary>
internal static class KeyState
{
    public const int VkEscape = 0x1B;
    public const int VkF11 = 0x7A;
    public const int VkF12 = 0x7B;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    public static bool IsDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    /// <summary>True when the given top-level window is the foreground window.</summary>
    public static bool IsForeground(IntPtr hwnd) => hwnd != IntPtr.Zero && GetForegroundWindow() == hwnd;
}