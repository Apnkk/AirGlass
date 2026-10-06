Add-Type @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class WinTree {
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc p, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumProc p, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr GetParent(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }

    static string Describe(IntPtr h) {
        var sb = new StringBuilder(256);
        GetClassName(h, sb, 256);
        RECT r; GetWindowRect(h, out r);
        return string.Format("hwnd={0} class={1} parent={2} visible={3} size={4}x{5}",
            h.ToInt64(), sb, GetParent(h).ToInt64(), IsWindowVisible(h), r.R - r.L, r.B - r.T);
    }

    public static List<string> Dump(HashSet<uint> pids) {
        var lines = new List<string>();
        EnumWindows((h, l) => {
            uint pid; GetWindowThreadProcessId(h, out pid);
            if (!pids.Contains(pid)) return true;
            lines.Add("TOP pid=" + pid + " " + Describe(h));
            EnumChildWindows(h, (c, l2) => {
                uint cp; GetWindowThreadProcessId(c, out cp);
                lines.Add("   CHILD pid=" + cp + " " + Describe(c));
                return true;
            }, IntPtr.Zero);
            return true;
        }, IntPtr.Zero);
        return lines;
    }
}
"@

$deadline = (Get-Date).AddSeconds(120)
$last = ""
while ((Get-Date) -lt $deadline) {
    $procs = Get-Process -Name AirGlass, uxplay -ErrorAction SilentlyContinue
    $pids = New-Object 'System.Collections.Generic.HashSet[uint32]'
    foreach ($p in $procs) { [void]$pids.Add([uint32]$p.Id) }
    $dump = ([WinTree]::Dump($pids)) -join "`n"
    if ($dump -ne $last) {
        "--- {0}" -f (Get-Date -Format HH:mm:ss)
        $dump
        $last = $dump
    }
    Start-Sleep -Seconds 2
}
"== fin"