# Register Chrome Native Messaging Host in Windows Registry (Current User)
param(
    [string]$ExtensionId = ""
)

$hostName = "com.chrome_account_switcher.helper"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$exePath = Join-Path $scriptDir "ChromeAccountSwitcher.Helper\bin\Debug\net8.0-windows\ChromeAccountSwitcher.Helper.exe"
if (-not (Test-Path $exePath)) {
    $exePath = Join-Path $scriptDir "ChromeAccountSwitcher.Helper\bin\Debug\net8.0\ChromeAccountSwitcher.Helper.exe"
}

$manifestPath = Join-Path $scriptDir "com.chrome_account_switcher.helper.json"

if (-not (Test-Path $exePath)) {
    Write-Host "Warning: Helper executable not found at $exePath. Building helper first..." -ForegroundColor Yellow
    & "$env:USERPROFILE\.dotnet\dotnet.exe" build (Join-Path $scriptDir "ChromeAccountSwitcher.Helper")
    $exePath = Join-Path $scriptDir "ChromeAccountSwitcher.Helper\bin\Debug\net8.0-windows\ChromeAccountSwitcher.Helper.exe"
}

# Resolve full absolute paths (Chrome requires absolute paths)
$fullExePath = [System.IO.Path]::GetFullPath($exePath)
$fullManifestPath = [System.IO.Path]::GetFullPath($manifestPath)

# Update manifest JSON with absolute path and allowed extension origins
if ($ExtensionId -ne "") {
    # Clean user input if full URL was pasted
    $cleanId = $ExtensionId -replace "^chrome-extension://", "" -replace "/.*$", ""
    $cleanId = $cleanId.Trim()
    $allowedOrigins = @("chrome-extension://$cleanId/")
} else {
    $allowedOrigins = @("chrome-extension://EXTENSION_ID_HERE/")
}

$manifestObject = [ordered]@{
    name = $hostName
    description = "Chrome Account Switcher Native Host"
    path = $fullExePath
    type = "stdio"
    allowed_origins = $allowedOrigins
}

$jsonContent = $manifestObject | ConvertTo-Json -Depth 5
[System.IO.File]::WriteAllText($fullManifestPath, $jsonContent)

# Create registry key: HKCU\Software\Google\Chrome\NativeMessagingHosts\com.chrome_account_switcher.helper
$regPath = "HKCU:\Software\Google\Chrome\NativeMessagingHosts\$hostName"
if (-not (Test-Path $regPath)) {
    New-Item -Path $regPath -Force | Out-Null
}

Set-ItemProperty -Path $regPath -Name "(default)" -Value $fullManifestPath

Write-Host "==========================================================" -ForegroundColor Green
Write-Host " Native Messaging Host Registered Successfully!" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
Write-Host "Host Name:     $hostName"
Write-Host "Manifest Path: $fullManifestPath"
Write-Host "Binary Path:   $fullExePath"
Write-Host "Registry Key:  $regPath"
if ($ExtensionId -ne "") {
    Write-Host "Allowed Origin: chrome-extension://$cleanId/" -ForegroundColor Green
} else {
    Write-Host "Allowed Origin: chrome-extension://EXTENSION_ID_HERE/ (Placeholder)" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "ACTION REQUIRED: Obtain your extension ID from chrome://extensions and run:" -ForegroundColor Yellow
    Write-Host "  .\register-host.ps1 -ExtensionId <YOUR_EXTENSION_ID>" -ForegroundColor Cyan
}
Write-Host ""
