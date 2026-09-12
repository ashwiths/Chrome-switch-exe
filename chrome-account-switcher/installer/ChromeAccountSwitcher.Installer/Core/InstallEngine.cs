using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using ChromeAccountSwitcher.Installer.Configuration;

namespace ChromeAccountSwitcher.Installer.Core;

public class InstallResult
{
    public bool Success { get; set; }
    public string InstallDirectory { get; set; } = string.Empty;
    public string HelperExePath { get; set; } = string.Empty;
    public string ManifestPath { get; set; } = string.Empty;
    public string RegistryKeyPath { get; set; } = string.Empty;
    public string StartupScriptPath { get; set; } = string.Empty;
    public List<string> AllowedExtensionIds { get; set; } = new();
    public string? ErrorMessage { get; set; }
}

public static class InstallEngine
{
    public static string GetInstallDirectory()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, InstallerConfig.TargetFolderName);
    }

    public static string GetStartupDirectory()
    {
        return Environment.GetFolderPath(Environment.SpecialFolder.Startup);
    }

    public static InstallResult Install(string? overrideExtensionId = null, Action<string, int>? progressCallback = null)
    {
        var result = new InstallResult();

        try
        {
            progressCallback?.Invoke("Preparing installation environment...", 10);
            string installDir = GetInstallDirectory();
            result.InstallDirectory = installDir;

            // 1. Terminate any running helper processes first
            progressCallback?.Invoke("Checking running background processes...", 20);
            TerminateExistingHelperProcesses();

            // 2. Ensure target directory exists
            if (!Directory.Exists(installDir))
            {
                Directory.CreateDirectory(installDir);
            }

            // 3. Extract embedded helper executable
            progressCallback?.Invoke("Extracting Chrome Account Switcher Native Helper...", 40);
            string helperExePath = Path.Combine(installDir, InstallerConfig.HelperExeName);
            result.HelperExePath = helperExePath;
            ExtractEmbeddedHelper(helperExePath);

            // 4. Resolve Extension IDs
            progressCallback?.Invoke("Configuring Native Messaging host...", 60);
            var extensionIds = ResolveExtensionIds(overrideExtensionId);
            result.AllowedExtensionIds = extensionIds;

            // 5. Generate and write Native Messaging Host Manifest
            string manifestPath = Path.Combine(installDir, InstallerConfig.ManifestName);
            result.ManifestPath = manifestPath;
            WriteManifest(manifestPath, helperExePath, extensionIds);

            // 6. Register Native Messaging Host in HKCU Registry
            progressCallback?.Invoke("Registering Native Messaging Host in Windows Registry...", 75);
            string regSubKey = $@"Software\Google\Chrome\NativeMessagingHosts\{InstallerConfig.HostName}";
            using (var key = Registry.CurrentUser.CreateSubKey(regSubKey))
            {
                key.SetValue(null, manifestPath); // (Default) value
            }
            result.RegistryKeyPath = $@"HKCU\{regSubKey}";

            // 7. Setup Auto-start shortcut for Global Hotkey Daemon
            progressCallback?.Invoke("Configuring global keyboard shortcuts startup daemon...", 85);
            string startupDir = GetStartupDirectory();
            string startupScript = Path.Combine(startupDir, InstallerConfig.DaemonStartupScript);
            result.StartupScriptPath = startupScript;
            WriteStartupScript(startupScript, helperExePath);

            // 8. Start Hotkey Daemon in background
            StartHotkeyDaemon(helperExePath);

            // 9. Copy installer as Uninstaller and register in Add/Remove Programs
            progressCallback?.Invoke("Registering Windows uninstall entry...", 95);
            string uninstallerPath = Path.Combine(installDir, InstallerConfig.UninstallerExeName);
            CopyCurrentExecutable(uninstallerPath);
            RegisterUninstaller(installDir, uninstallerPath, helperExePath);

            progressCallback?.Invoke("Installation completed successfully!", 100);
            result.Success = true;
            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.ToString();
            try
            {
                string logPath = Path.Combine(Path.GetTempPath(), "chrome_account_switcher_setup.log");
                File.WriteAllText(logPath, $"[{DateTime.Now}] Installation Exception:\r\n{ex}");
            }
            catch { }
            return result;
        }
    }

    public static void TerminateExistingHelperProcesses()
    {
        try
        {
            var processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(InstallerConfig.HelperExeName));
            foreach (var p in processes)
            {
                try
                {
                    p.Kill();
                    p.WaitForExit(2000);
                }
                catch { }
            }
        }
        catch { }
    }

    private static void ExtractEmbeddedHelper(string targetPath)
    {
        var assembly = typeof(InstallEngine).Assembly;

        // Check for compressed resource first
        using (var stream = assembly.GetManifestResourceStream(InstallerConfig.HelperResourceName))
        {
            if (stream != null)
            {
                using var gzip = new GZipStream(stream, CompressionMode.Decompress);
                using var fs = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None);
                gzip.CopyTo(fs);
                return;
            }
        }

        // Fallback check for uncompressed resource if named differently
        string uncompressedName = "ChromeAccountSwitcher.Installer.Resources." + InstallerConfig.HelperExeName;
        using (var stream = assembly.GetManifestResourceStream(uncompressedName))
        {
            if (stream != null)
            {
                using var fs = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None);
                stream.CopyTo(fs);
                return;
            }
        }

        string allNames = string.Join(", ", assembly.GetManifestResourceNames());
        throw new FileNotFoundException($"Embedded helper resource not found in assembly '{assembly.FullName}'. Checked '{InstallerConfig.HelperResourceName}' and '{uncompressedName}'. Available resources: [{allNames}]");
    }

    public static List<string> ResolveExtensionIds(string? overrideExtensionId)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Check override
        if (!string.IsNullOrWhiteSpace(overrideExtensionId))
        {
            string clean = CleanExtensionId(overrideExtensionId);
            if (IsValidExtensionId(clean))
            {
                ids.Add(clean);
            }
        }

        // 2. Check PRODUCTION_EXTENSION_ID configuration
        if (!string.IsNullOrWhiteSpace(InstallerConfig.PRODUCTION_EXTENSION_ID) &&
            !InstallerConfig.PRODUCTION_EXTENSION_ID.Equals("YOUR_CHROME_STORE_EXTENSION_ID_HERE", StringComparison.OrdinalIgnoreCase))
        {
            string clean = CleanExtensionId(InstallerConfig.PRODUCTION_EXTENSION_ID);
            if (IsValidExtensionId(clean))
            {
                ids.Add(clean);
            }
        }

        // 3. Fallback: Auto-detect unpacked extension from Chrome's Preferences
        if (ids.Count == 0)
        {
            string? detected = AutoDetectUnpackedExtensionId();
            if (!string.IsNullOrEmpty(detected))
            {
                ids.Add(detected);
            }
        }

        // 4. If still none, add placeholder to allow editing
        if (ids.Count == 0)
        {
            ids.Add("EXTENSION_ID_HERE");
        }

        return new List<string>(ids);
    }

    public static string CleanExtensionId(string raw)
    {
        string cleaned = raw.Trim();
        cleaned = Regex.Replace(cleaned, @"^chrome-extension://", "", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"/.*$", "");
        return cleaned.Trim().ToLowerInvariant();
    }

    public static bool IsValidExtensionId(string id)
    {
        return Regex.IsMatch(id, @"^[a-p]{32}$");
    }

    public static string? AutoDetectUnpackedExtensionId()
    {
        try
        {
            string chromeUserData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Google\Chrome\User Data");

            if (!Directory.Exists(chromeUserData))
                return null;

            var securePrefFiles = Directory.GetFiles(chromeUserData, "Secure Preferences", SearchOption.AllDirectories);
            foreach (var prefFile in securePrefFiles)
            {
                try
                {
                    string content = File.ReadAllText(prefFile);
                    if (content.Contains("chrome-account-switcher", StringComparison.OrdinalIgnoreCase))
                    {
                        using var doc = JsonDocument.Parse(content);
                        if (doc.RootElement.TryGetProperty("extensions", out var extensions) &&
                            extensions.TryGetProperty("settings", out var settings))
                        {
                            foreach (var extProp in settings.EnumerateObject())
                            {
                                if (extProp.Value.TryGetProperty("path", out var pathProp))
                                {
                                    string? p = pathProp.GetString();
                                    if (p != null && (p.Contains("chrome-account-switcher", StringComparison.OrdinalIgnoreCase) || p.Contains("extension", StringComparison.OrdinalIgnoreCase)))
                                    {
                                        string id = extProp.Name;
                                        if (IsValidExtensionId(id))
                                        {
                                            return id;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                catch { }
            }
        }
        catch { }

        return null;
    }

    private static void WriteManifest(string manifestPath, string helperExePath, List<string> extensionIds)
    {
        var allowedOrigins = new List<string>();
        foreach (var id in extensionIds)
        {
            allowedOrigins.Add($"chrome-extension://{id}/");
        }

        var manifestData = new Dictionary<string, object>
        {
            ["name"] = InstallerConfig.HostName,
            ["description"] = InstallerConfig.HostDescription,
            ["path"] = Path.GetFullPath(helperExePath),
            ["type"] = "stdio",
            ["allowed_origins"] = allowedOrigins
        };

        var options = new JsonSerializerOptions { WriteIndented = true };
        string json = JsonSerializer.Serialize(manifestData, options);
        File.WriteAllText(manifestPath, json);
    }

    private static void WriteStartupScript(string startupScriptPath, string helperExePath)
    {
        string fullExe = Path.GetFullPath(helperExePath);
        string vbs = "Set WshShell = CreateObject(\"Wscript.Shell\")\r\n" +
                     $"WshShell.Run \"\"\"{fullExe}\"\" --listen-hotkeys\", 0, False\r\n";
        File.WriteAllText(startupScriptPath, vbs);
    }

    private static void StartHotkeyDaemon(string helperExePath)
    {
        try
        {
            string startupScript = Path.Combine(GetStartupDirectory(), InstallerConfig.DaemonStartupScript);
            if (File.Exists(startupScript))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "wscript.exe",
                    Arguments = $"\"{startupScript}\"",
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                Process.Start(psi);
            }
            else
            {
                var psi = new ProcessStartInfo
                {
                    FileName = helperExePath,
                    Arguments = "--listen-hotkeys",
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                Process.Start(psi);
            }
        }
        catch { }
    }

    private static void CopyCurrentExecutable(string targetUninstallerPath)
    {
        try
        {
            string currentExe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
            if (File.Exists(currentExe) && !string.Equals(currentExe, targetUninstallerPath, StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(currentExe, targetUninstallerPath, true);
            }
        }
        catch { }
    }

    private static void RegisterUninstaller(string installDir, string uninstallerPath, string helperExePath)
    {
        try
        {
            string uninstallKeyPath = $@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{InstallerConfig.TargetFolderName}";
            using var key = Registry.CurrentUser.CreateSubKey(uninstallKeyPath);
            if (key != null)
            {
                key.SetValue("DisplayName", InstallerConfig.AppName);
                key.SetValue("DisplayVersion", InstallerConfig.AppVersion);
                key.SetValue("Publisher", InstallerConfig.Publisher);
                key.SetValue("InstallLocation", installDir);
                key.SetValue("UninstallString", $"\"{uninstallerPath}\" --uninstall");
                key.SetValue("QuietUninstallString", $"\"{uninstallerPath}\" --uninstall --silent");
                key.SetValue("DisplayIcon", $"\"{helperExePath}\",0");
                key.SetValue("HelpLink", InstallerConfig.DocumentationUrl);
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
        }
        catch { }
    }
}
