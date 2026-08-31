using System.Runtime.InteropServices;
using System.Text;

namespace ChromeAccountSwitcher.Helper.Windows;

public static class WindowManager
{
    private const int SW_RESTORE = 9;
    private const int SW_SHOW = 5;

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr OpenWindowStation(string lpszWinSta, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetProcessWindowStation(IntPtr hWinSta);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr OpenDesktop(string lpszDesktop, uint dwFlags, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr OpenInputDesktop(uint dwFlags, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetThreadDesktop(IntPtr hDesktop);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool CloseDesktop(IntPtr hDesktop);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool CloseWindowStation(IntPtr hWinSta);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool EnumDesktopWindows(IntPtr hDesktop, EnumWindowsProc lpfn, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsZoomed(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    /// <summary>
    /// Gets the window title for a given HWND.
    /// </summary>
    public static string GetWindowTitle(IntPtr hWnd)
    {
        int length = GetWindowTextLength(hWnd);
        if (length == 0) return string.Empty;
        var builder = new StringBuilder(length + 1);
        GetWindowText(hWnd, builder, builder.Capacity);
        return builder.ToString();
    }

    /// <summary>
    /// Gets the Win32 class name for a given HWND.
    /// </summary>
    public static string GetWindowClass(IntPtr hWnd)
    {
        var builder = new StringBuilder(256);
        GetClassName(hWnd, builder, builder.Capacity);
        return builder.ToString();
    }

    /// <summary>
    /// Brings the specified window to the foreground without closing, restarting, or reloading it.
    /// Uses AttachThreadInput to ensure Windows focus-stealing prevention does not block the focus.
    /// </summary>
    public static bool FocusWindow(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return false;

        IntPtr foregroundHwnd = GetForegroundWindow();
        if (foregroundHwnd == hWnd)
        {
            return true; // Already focused
        }

        uint foregroundThreadId = GetWindowThreadProcessId(foregroundHwnd, out _);
        uint targetThreadId = GetWindowThreadProcessId(hWnd, out _);
        uint currentThreadId = GetCurrentThreadId();

        bool attachedToForeground = false;
        bool attachedToTarget = false;

        try
        {
            if (foregroundThreadId != 0 && foregroundThreadId != currentThreadId)
            {
                attachedToForeground = AttachThreadInput(currentThreadId, foregroundThreadId, true);
            }

            if (targetThreadId != 0 && targetThreadId != currentThreadId)
            {
                attachedToTarget = AttachThreadInput(currentThreadId, targetThreadId, true);
            }

            if (IsIconic(hWnd))
            {
                ShowWindow(hWnd, SW_RESTORE);
            }
            else
            {
                ShowWindow(hWnd, SW_SHOW);
            }

            BringWindowToTop(hWnd);
            return SetForegroundWindow(hWnd);
        }
        finally
        {
            if (attachedToForeground)
            {
                AttachThreadInput(currentThreadId, foregroundThreadId, false);
            }
            if (attachedToTarget)
            {
                AttachThreadInput(currentThreadId, targetThreadId, false);
            }
        }
    }

    /// <summary>
    /// Enumerates all top-level windows, ensuring connection to interactive desktop WinSta0 if needed.
    /// </summary>
    public static void EnumerateAllTopLevelWindows(EnumWindowsProc proc)
    {
        // Try standard EnumWindows first
        int count = 0;
        EnumWindows((hWnd, lParam) =>
        {
            count++;
            return proc(hWnd, lParam);
        }, IntPtr.Zero);

        if (count > 0) return;

        // If 0 (e.g. running in detached session or service station), try WinSta0\Default
        const uint GENERIC_ALL = 0x10000000;
        IntPtr hWinSta = OpenWindowStation("WinSta0", false, GENERIC_ALL);
        if (hWinSta != IntPtr.Zero)
        {
            SetProcessWindowStation(hWinSta);
            IntPtr hDesktop = OpenDesktop("Default", 0, false, GENERIC_ALL);
            if (hDesktop == IntPtr.Zero)
            {
                hDesktop = OpenInputDesktop(0, false, GENERIC_ALL);
            }

            if (hDesktop != IntPtr.Zero)
            {
                SetThreadDesktop(hDesktop);
                EnumDesktopWindows(hDesktop, proc, IntPtr.Zero);
                CloseDesktop(hDesktop);
            }
            CloseWindowStation(hWinSta);
        }
    }
}
