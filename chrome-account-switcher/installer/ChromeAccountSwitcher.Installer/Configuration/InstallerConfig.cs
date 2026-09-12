using System;

namespace ChromeAccountSwitcher.Installer.Configuration;

/// <summary>
/// Installer Configuration Settings.
/// Centralizes all product identifiers, registry keys, and installation parameters.
/// </summary>
public static class InstallerConfig
{
    // =========================================================================================
    // PRODUCTION EXTENSION ID CONFIGURATION
    // =========================================================================================
    // When publishing to the official Chrome Web Store, paste your 32-character Chrome Web Store
    // Extension ID here before running the production build.
    //
    // Example:
    // public const string PRODUCTION_EXTENSION_ID = "abcdefghijklmnopabcdefghijklmnop";
    //
    // NOTE:
    // If left as the placeholder "YOUR_CHROME_STORE_EXTENSION_ID_HERE", the installer will:
    // 1. Check for command-line override: --extension-id <ID>
    // 2. Automatically detect any active local/unpacked extension ID from Chrome's Preferences.
    // This allows seamless developer & test installations while keeping production builds strict.
    // =========================================================================================
    public const string PRODUCTION_EXTENSION_ID = "YOUR_CHROME_STORE_EXTENSION_ID_HERE";

    // Application metadata
    public const string AppName = "Chrome Account Switcher";
    public const string AppVersion = "1.0.0";
    public const string Publisher = "Chrome Account Switcher";
    public const string InstallerTitle = "Install Chrome Account Switcher";
    public const string InstallerSubtitle = "Install the Windows helper required to switch between Chrome profiles.";
    
    // Native messaging host details
    public const string HostName = "com.chrome_account_switcher.helper";
    public const string HostDescription = "Chrome Account Switcher Native Host";
    
    // Target installation directory name (under %LOCALAPPDATA%)
    public const string TargetFolderName = "ChromeAccountSwitcher";
    public const string HelperExeName = "ChromeAccountSwitcher.Helper.exe";
    public const string ManifestName = "com.chrome_account_switcher.helper.json";
    public const string UninstallerExeName = "Uninstall.exe";
    
    // Hotkey daemon startup script name
    public const string DaemonStartupScript = "ChromeAccountSwitcherDaemon.vbs";
    
    // Documentation / Instructions URL
    public const string DocumentationUrl = "https://github.com/ashwiths/Chrome-switch-exe#readme";

    // Embedded resource name in assembly
    public const string HelperResourceName = "ChromeAccountSwitcher.Installer.Resources.helper.gz";
}
