# Chrome Account Switcher - Windows Native Helper

A lightweight C# .NET 8 helper application for detecting, mapping, and focusing active Google Chrome profile windows on Windows.

## Architecture

- **Protocol**: Chrome Native Messaging stdio binary protocol (32-bit little-endian length prefix + UTF-8 JSON).
- **Host Name**: `com.chrome_account_switcher.helper`
- **Window Activation**: Win32 `SetForegroundWindow`, `ShowWindow(SW_RESTORE)`, `BringWindowToTop`, and `AttachThreadInput`.
- **Profile Detection**: Process command line inspection via `NtQueryInformationProcess` + `%LOCALAPPDATA%\Google\Chrome\User Data\Local State`.

## Registration (Windows Registry & Security)

To secure the Native Messaging host, register it specifically with your Chrome Extension ID (wildcard origins are not permitted):

```powershell
powershell -ExecutionPolicy Bypass -File .\register-host.ps1 -ExtensionId <YOUR_EXTENSION_ID>
```

This creates the registry key:
`HKCU\Software\Google\Chrome\NativeMessagingHosts\com.chrome_account_switcher.helper` pointing to `com.chrome_account_switcher.helper.json` with `allowed_origins` restricted solely to `chrome-extension://<YOUR_EXTENSION_ID>/`.

## Testing / Diagnostics

Run CLI Diagnostics:
```bash
dotnet run --project ChromeAccountSwitcher.Helper
```

Test Slot Switch:
```bash
dotnet run --project ChromeAccountSwitcher.Helper -- --switch-slot 1
```

Focus specific HWND:
```bash
dotnet run --project ChromeAccountSwitcher.Helper -- --focus-hwnd 0x0010088C
```
