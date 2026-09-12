# =============================================================================
# Chrome Account Switcher - Production Build & Installer Packaging Pipeline
# Produces: release/ChromeAccountSwitcherSetup.exe
# =============================================================================
param(
    [switch]$SkipExtensionBuild = $false
)

$ErrorActionPreference = "Stop"

$rootDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$releaseDir = Join-Path $rootDir "release"
$extensionDir = Join-Path $rootDir "chrome-account-switcher\extension"
$helperProjDir = Join-Path $rootDir "chrome-account-switcher\helper\ChromeAccountSwitcher.Helper"
$installerProjDir = Join-Path $rootDir "chrome-account-switcher\installer\ChromeAccountSwitcher.Installer"
$installerResourcesDir = Join-Path $installerProjDir "Resources"

# Locate dotnet CLI (supports user-profile local install or system PATH)
$dotnetCmd = "dotnet"
$userDotnet = Join-Path $env:USERPROFILE ".dotnet\dotnet.exe"
if (Test-Path $userDotnet) {
    $dotnetCmd = $userDotnet
}

Write-Host "=================================================================" -ForegroundColor Cyan
Write-Host " Building Chrome Account Switcher - Standalone Windows Installer " -ForegroundColor Cyan
Write-Host "=================================================================" -ForegroundColor Cyan
Write-Host "Root Directory:     $rootDir"
Write-Host "Release Directory:  $releaseDir"
Write-Host "Dotnet Executable:  $dotnetCmd"
Write-Host ""

# -----------------------------------------------------------------------------
# 1. Build Chrome Extension (Vite / TypeScript)
# -----------------------------------------------------------------------------
if (-not $SkipExtensionBuild) {
    Write-Host "[1/4] Building Chrome Extension..." -ForegroundColor Yellow
    Push-Location $extensionDir
    try {
        npm run build
        if ($LASTEXITCODE -ne 0) {
            throw "npm run build failed with exit code $LASTEXITCODE"
        }
        Write-Host "  -> Extension built successfully in dist/" -ForegroundColor Green
    } finally {
        Pop-Location
    }
} else {
    Write-Host "[1/4] Skipping extension build (flag specified)." -ForegroundColor Gray
}

# -----------------------------------------------------------------------------
# 2. Publish C# Native Helper in Release Mode (Self-Contained Single-File)
# -----------------------------------------------------------------------------
Write-Host ""
Write-Host "[2/4] Publishing Release Self-Contained Native Helper..." -ForegroundColor Yellow
$helperPublishDir = Join-Path $helperProjDir "bin\Release\net8.0-windows\win-x64\publish"

& $dotnetCmd publish $helperProjDir `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    --output $helperPublishDir

if ($LASTEXITCODE -ne 0) {
    throw "Helper publish failed with exit code $LASTEXITCODE"
}

$helperExe = Join-Path $helperPublishDir "ChromeAccountSwitcher.Helper.exe"
if (-not (Test-Path $helperExe)) {
    throw "Published helper executable not found at: $helperExe"
}

$helperSizeMb = [math]::Round(((Get-Item $helperExe).Length / 1MB), 2)
Write-Host "  -> Helper published: $helperExe ($helperSizeMb MB)" -ForegroundColor Green

# -----------------------------------------------------------------------------
# 3. Compress Helper Binary into Installer Resources
# -----------------------------------------------------------------------------
Write-Host ""
Write-Host "[3/4] Compressing helper binary into installer payload..." -ForegroundColor Yellow
if (-not (Test-Path $installerResourcesDir)) {
    New-Item -ItemType Directory -Path $installerResourcesDir -Force | Out-Null
}

$compressedPayload = Join-Path $installerResourcesDir "helper.gz"

$inputStream = [System.IO.File]::OpenRead($helperExe)
$outputStream = [System.IO.File]::Create($compressedPayload)
$gzipStream = New-Object System.IO.Compression.GZipStream($outputStream, [System.IO.Compression.CompressionLevel]::Optimal)
$inputStream.CopyTo($gzipStream)
$gzipStream.Dispose()
$outputStream.Dispose()
$inputStream.Dispose()

$payloadSizeMb = [math]::Round(((Get-Item $compressedPayload).Length / 1MB), 2)
Write-Host "  -> Payload compressed: $compressedPayload ($payloadSizeMb MB, was $helperSizeMb MB)" -ForegroundColor Green

# -----------------------------------------------------------------------------
# 4. Build & Publish Installer Executable (ChromeAccountSwitcherSetup.exe)
# -----------------------------------------------------------------------------
Write-Host ""
Write-Host "[4/4] Publishing standalone installer executable..." -ForegroundColor Yellow

if (-not (Test-Path $releaseDir)) {
    New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null
}

$installerTempPublishDir = Join-Path $installerProjDir "bin\Release\net8.0-windows\win-x64\publish"

& $dotnetCmd publish $installerProjDir `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    --output $installerTempPublishDir

if ($LASTEXITCODE -ne 0) {
    throw "Installer publish failed with exit code $LASTEXITCODE"
}

$setupSourceExe = Join-Path $installerTempPublishDir "ChromeAccountSwitcherSetup.exe"
if (-not (Test-Path $setupSourceExe)) {
    throw "Published installer executable not found at: $setupSourceExe"
}

$finalReleaseExe = Join-Path $releaseDir "ChromeAccountSwitcherSetup.exe"
Copy-Item $setupSourceExe $finalReleaseExe -Force

$finalSizeMb = [math]::Round(((Get-Item $finalReleaseExe).Length / 1MB), 2)

Write-Host ""
Write-Host "=================================================================" -ForegroundColor Green
Write-Host " BUILD & PACKAGING COMPLETE!" -ForegroundColor Green
Write-Host "=================================================================" -ForegroundColor Green
Write-Host "Output Installer: $finalReleaseExe" -ForegroundColor Green
Write-Host "File Size:        $finalSizeMb MB" -ForegroundColor Green
Write-Host "Type:             Single-File Standalone Windows Executable (win-x64)" -ForegroundColor Green
Write-Host "Requires .NET:    NO (Fully self-contained)" -ForegroundColor Green
Write-Host ""
