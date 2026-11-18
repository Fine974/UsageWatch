using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace AppUsage;

internal static class Native
{
    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    internal static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    [StructLayout(LayoutKind.Sequential)]
    internal struct LASTINPUTINFO
    {
        internal uint cbSize;
        internal uint dwTime;
    }
}

internal readonly record struct Snapshot(
    string Proc,
    string Title,
    string ClassName,
    int IdleSeconds,
    bool HasWindow,
    bool IsLockScreen);

internal static class Collector
{
    internal static Snapshot Take()
    {
        var hwnd = Native.GetForegroundWindow();
        var proc = "";
        var title = "";
        var cls = "";
        var hasWindow = false;

        if (hwnd != IntPtr.Zero)
        {
            _ = Native.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid != 0)
            {
                hasWindow = true;
                try { proc = Process.GetProcessById((int)pid).ProcessName; }
                catch { proc = "unknown"; }

                var sb = new StringBuilder(512);
                var n = Native.GetWindowText(hwnd, sb, sb.Capacity);
                if (n > 0) title = sb.ToString(0, n);

                var cb = new StringBuilder(256);
                var m = Native.GetClassName(hwnd, cb, cb.Capacity);
                if (m > 0) cls = cb.ToString(0, m);
            }
        }

        var lii = new Native.LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<Native.LASTINPUTINFO>() };
        _ = Native.GetLastInputInfo(ref lii);
        var now = (uint)Environment.TickCount;
        var delta = unchecked(now - lii.dwTime);
        var idleSec = (int)(delta / 1000);
        if (idleSec < 0 || idleSec > 86400 * 7) idleSec = 0;

        var isLock = !hasWindow || IsLockScreenProc(proc, title, cls);

        return new Snapshot(proc, title, cls, idleSec, hasWindow, isLock);
    }

    /// <summary>
    /// 锁屏判定。Win10/11 锁屏后前台窗口属于 LockApp.exe（窗口标题「Windows 默认锁屏界面」），
    /// 早期版本是 LogonUI.exe。漏判会导致锁屏期间被当成正常应用累计时长。
    /// </summary>
    internal static bool IsLockScreenProc(string proc, string title, string cls)
    {
        if (proc.Equals("LogonUI", StringComparison.OrdinalIgnoreCase)
            || proc.Equals("LockApp", StringComparison.OrdinalIgnoreCase)
            || proc.Equals("LockScreenBackdrop", StringComparison.OrdinalIgnoreCase))
            return true;

        if (cls.Equals("LockScreenBackdrop", StringComparison.OrdinalIgnoreCase))
            return true;

        // 标题精确匹配，避免把浏览「锁屏」相关网页误判成离开
        return title.Equals("Windows 默认锁屏界面", StringComparison.Ordinal)
               || title.Equals("Windows Default Lock Screen", StringComparison.OrdinalIgnoreCase);
    }
}
