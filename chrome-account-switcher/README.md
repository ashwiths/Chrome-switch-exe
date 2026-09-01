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
 
Run the PowerShell registration script to configure the Windows Registry and restrict communication exclusively to your Extension ID:
 
```powershell
cd helper
powershell -ExecutionPolicy Bypass -File .\register-host.ps1 -ExtensionId <YOUR_EXTENSION_ID>
```

### 4. Switch Between Profiles

- **Alt + Shift + 1**: Switch to Profile Slot 1 (`Default`)
- **Alt + Shift + 2**: Switch to Profile Slot 2 (`Profile 1`)
- **Alt + Shift + 3**: Switch to Profile Slot 3 (`Profile 12`)
- **Alt + Shift + 4**: Switch to Profile Slot 4 (`Profile 13`)
- **Popup / Click**: Switch to Slot 5 (`Profile 14`) and additional slots

*(Note: `Alt + Shift + 1..4` prevents conflicts with Chrome's native `Ctrl + 1..8` tab-switching shortcuts. Chrome Manifest V3 limits global extension command shortcuts to a maximum of 4. Additional slots are accessible directly through the extension popup).*
