# Chrome Account Switcher

Instant, state-preserving Chrome profile switching via keyboard shortcuts.

## Architecture

```
Keyboard Shortcut (Ctrl+1 .. Ctrl+5)
        ↓
Chrome Extension (Manifest V3 Service Worker)
        ↓
Chrome Native Messaging ("com.chrome_account_switcher.helper")
        ↓
C# .NET 8 Native Helper
        ↓
Profile → HWND Mapping Engine
        ↓
Win32 Window Activation (SetForegroundWindow + ShowWindow)
        ↓
Existing Chrome Profile Window (Focused without tab reloads/restarts)
```

## Quick Start & Installation

### 1. Build Extension & Helper

```bash
# Build the Chrome Extension
cd extension
npm install
npm run build

# Build the Native Helper
cd ../helper/ChromeAccountSwitcher.Helper
dotnet build
```

### 2. Load Extension in Chrome

1. Open Chrome and navigate to `chrome://extensions`.
2. Enable **Developer mode** (toggle in upper right).
3. Click **Load unpacked** and select the `chrome-account-switcher/extension/dist` folder.
4. Note your assigned **Extension ID** (e.g. `abcdefghijklmnop...`).

### 3. Register Native Messaging Host

Run the PowerShell registration script to configure the Windows Registry:

```powershell
cd helper
powershell -ExecutionPolicy Bypass -File .\register-host.ps1 -ExtensionId <YOUR_EXTENSION_ID>
```

### 4. Switch Between Profiles

- **Ctrl + 1**: Switch to Profile Slot 1 (`Default`)
- **Ctrl + 2**: Switch to Profile Slot 2 (`Profile 1`)
- **Ctrl + 3**: Switch to Profile Slot 3 (`Profile 12`)
- **Ctrl + 4**: Switch to Profile Slot 4 (`Profile 13`)
- **Ctrl + 5**: Switch to Profile Slot 5 (`Profile 14`)

*Note: You can customize slot mappings in `%APPDATA%\ChromeAccountSwitcher\slots.json`.*
