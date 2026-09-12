# Chrome Account Switcher - Windows Installer

This directory contains the standalone Windows installer project for **Chrome Account Switcher**.

## Structure

```
installer/
├── ChromeAccountSwitcher.Installer/
│   ├── Configuration/
│   │   └── InstallerConfig.cs   # PRODUCTION_EXTENSION_ID and application constants
│   ├── Core/
│   │   ├── InstallEngine.cs     # Binary extraction, manifest generation, registry, startup
│   │   └── UninstallEngine.cs   # Process termination, unregistration, file cleanup
│   ├── UI/
│   │   └── MainForm.cs          # Windows Forms GUI (Install, progress, completion states)
│   ├── Program.cs               # Entry point supporting GUI, silent, and uninstall modes
│   └── ChromeAccountSwitcher.Installer.csproj
└── README.md
```

## How to Build the Installer

Run the root packaging script:

```powershell
.\build-installer.ps1
```

This single command will:
1. Compile the Chrome extension (`npm run build`).
2. Publish the C# Native Helper in Release mode as a self-contained single file (`win-x64`).
3. Compress and package the helper into the installer's embedded resources.
4. Publish `release/ChromeAccountSwitcherSetup.exe` as a single, standalone Windows executable.

## Configuring Production Extension ID

Before public release to the Chrome Web Store:
1. Open [`InstallerConfig.cs`](file:///c:/Users/infan/Desktop/Chrome%20switch%20exe/chrome-account-switcher/installer/ChromeAccountSwitcher.Installer/Configuration/InstallerConfig.cs).
2. Set `PRODUCTION_EXTENSION_ID` to your official 32-character Chrome Web Store extension ID:
   ```csharp
   public const string PRODUCTION_EXTENSION_ID = "abcdefghijklmnopabcdefghijklmnop";
   ```
3. Re-run `.\build-installer.ps1`.

### Developer / Local Mode
If `PRODUCTION_EXTENSION_ID` is left as `"YOUR_CHROME_STORE_EXTENSION_ID_HERE"`, the installer will automatically detect any active unpacked extension from Chrome's `Secure Preferences` or accept `--extension-id <ID>` from the command line.

## Target Machine Requirements

- **Supported OS**: Windows 10, Windows 11 (64-bit).
- **Runtime Requirements**: **None**. The installer and helper are self-contained (`win-x64`) and do not require .NET SDK, .NET Runtime, or Visual Studio on the user's computer.

## Installation Targets

- **Helper Directory**: `%LOCALAPPDATA%\ChromeAccountSwitcher\`
- **Native Messaging Host Manifest**: `%LOCALAPPDATA%\ChromeAccountSwitcher\com.chrome_account_switcher.helper.json`
- **Native Messaging Registry Key**: `HKCU\Software\Google\Chrome\NativeMessagingHosts\com.chrome_account_switcher.helper`
- **Global Hotkey Daemon Hook**: `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\ChromeAccountSwitcherDaemon.vbs`
- **Add/Remove Programs**: `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\ChromeAccountSwitcher`
