# Unregister Chrome Native Messaging Host from Windows Registry
$hostName = "com.chrome_account_switcher.helper"
$regPath = "HKCU:\Software\Google\Chrome\NativeMessagingHosts\$hostName"

if (Test-Path $regPath) {
    Remove-Item -Path $regPath -Recurse -Force
    Write-Host "Unregistered $hostName from Windows Registry." -ForegroundColor Yellow
} else {
    Write-Host "Registry key $regPath not found."
}

# Remove from User Startup folder
$startupFile = Join-Path "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Startup" "ChromeAccountSwitcherDaemon.vbs"
if (Test-Path $startupFile) {
    Remove-Item $startupFile -Force
}

# Stop daemon process if running
Stop-Process -Name "ChromeAccountSwitcher.Helper" -ErrorAction SilentlyContinue
Write-Host "Cleaned up Startup entry and stopped helper daemon." -ForegroundColor Yellow

