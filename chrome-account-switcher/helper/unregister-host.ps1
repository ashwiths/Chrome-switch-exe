# Unregister Chrome Native Messaging Host from Windows Registry
$hostName = "com.chrome_account_switcher.helper"
$regPath = "HKCU:\Software\Google\Chrome\NativeMessagingHosts\$hostName"

if (Test-Path $regPath) {
    Remove-Item -Path $regPath -Recurse -Force
    Write-Host "Unregistered $hostName from Windows Registry." -ForegroundColor Yellow
} else {
    Write-Host "Registry key $regPath not found."
}
