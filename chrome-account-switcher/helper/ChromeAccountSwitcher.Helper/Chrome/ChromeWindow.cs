namespace ChromeAccountSwitcher.Helper.Chrome;

/// <summary>
/// Represents a running Chrome browser window and its profile mapping.
/// </summary>
public class ChromeWindow
{
    /// <summary>
    /// Win32 Window Handle (HWND).
    /// </summary>
    public IntPtr Hwnd { get; set; }

    /// <summary>
    /// Title text displayed on the window title bar.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Process ID owning this window handle.
    /// </summary>
    public int ProcessId { get; set; }

    /// <summary>
    /// Win32 window class name (typically Chrome_WidgetWin_1).
    /// </summary>
    public string ClassName { get; set; } = string.Empty;

    /// <summary>
    /// Full command line arguments of the process owning the window.
    /// </summary>
    public string? CommandLine { get; set; }

    /// <summary>
    /// Whether the window is visible to the user.
    /// </summary>
    public bool IsVisible { get; set; }

    /// <summary>
    /// Whether the window is currently minimized.
    /// </summary>
    public bool IsMinimized { get; set; }

    /// <summary>
    /// Whether the window is currently maximized.
    /// </summary>
    public bool IsMaximized { get; set; }

    /// <summary>
    /// Human readable window state (Focused, Normal, Minimized, Maximized).
    /// </summary>
    public string WindowState
    {
        get
        {
            if (IsMinimized) return "Minimized";
            if (IsMaximized) return "Maximized";
            return "Normal";
        }
    }

    /// <summary>
    /// Whether this window currently holds foreground input focus.
    /// </summary>
    public bool IsFocused { get; set; }

    /// <summary>
    /// Indicates whether this appears to be a normal browser window vs utility/widget surface.
    /// </summary>
    public bool IsNormalBrowserWindow { get; set; }

    /// <summary>
    /// Mapped Chrome Profile directory name (e.g. "Default", "Profile 1", "Profile 7").
    /// </summary>
    public string? ProfileDirectory { get; set; }

    /// <summary>
    /// Mapped Chrome Profile display name (e.g. "Personal", "Work", "College").
    /// </summary>
    public string? ProfileDisplayName { get; set; }

    /// <summary>
    /// Mapped Google Account email.
    /// </summary>
    public string? ProfileEmail { get; set; }

    /// <summary>
    /// Indicates if the window was reliably mapped to a profile.
    /// </summary>
    public bool IsReliablyMapped => !string.IsNullOrEmpty(ProfileDirectory);

    /// <summary>
    /// How the profile mapping was established (e.g., Process Command Line, Window Title & Local State, Parent Process).
    /// </summary>
    public string MappingSource { get; set; } = string.Empty;
}
