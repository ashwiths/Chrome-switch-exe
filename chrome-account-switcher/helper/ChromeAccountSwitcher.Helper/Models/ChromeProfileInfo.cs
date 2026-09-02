namespace ChromeAccountSwitcher.Helper.Models;

/// <summary>
/// Represents metadata regarding a registered Chrome Profile.
/// </summary>
public class ChromeProfileInfo
{
    /// <summary>
    /// Directory key used by Chrome on disk (e.g., "Default", "Profile 1", "Profile 2").
    /// </summary>
    public string DirectoryName { get; set; } = string.Empty;

    /// <summary>
    /// User-facing display name of the profile (e.g., "Personal", "Work", "College").
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Associated email address or account username if logged into Google.
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// Full name from Google Account (gaia_name).
    /// </summary>
    public string? GaiaName { get; set; }

    /// <summary>
    /// Full path to the profile directory on disk.
    /// </summary>
    public string? FullPath { get; set; }

    /// <summary>
    /// Shortcut name if available.
    /// </summary>
    public string? ShortcutName { get; set; }

    /// <summary>
    /// Avatar icon resource string or URL if available.
    /// </summary>
    public string? AvatarIcon { get; set; }

    /// <summary>
    /// Profile order index from profiles_order.
    /// </summary>
    public int OrderIndex { get; set; }

    /// <summary>
    /// Whether this profile belongs to the currently focused Chrome window.
    /// </summary>
    public bool IsCurrent { get; set; }
}
