using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using ChromeAccountSwitcher.Installer.Configuration;

namespace ChromeAccountSwitcher.Installer.Core;

public class UninstallResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}

public static class UninstallEngine
{
    public static UninstallResult Uninstall(Action<string, int>? progressCallback = null)
    {
        var result = new UninstallResult();

        try
        {
            progressCallback?.Invoke("Stopping Chrome Account Switcher background processes...", 20);
            InstallEngine.TerminateExistingHelperProcesses();

            progressCallback?.Invoke("Removing startup daemon...", 40);
            string startupScript = Path.Combine(InstallEngine.GetStartupDirectory(), InstallerConfig.DaemonStartupScript);
            if (File.Exists(startupScript))
            {
                try { File.Delete(startupScript); } catch { }
            }

            progressCallback?.Invoke("Unregistering Chrome Native Messaging Host...", 60);
            string nativeRegKey = $@"Software\Google\Chrome\NativeMessagingHosts\{InstallerConfig.HostName}";
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(nativeRegKey, false);
            }
            catch { }

            progressCallback?.Invoke("Removing Windows Add/Remove Programs entry...", 80);
            string uninstallRegKey = $@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{InstallerConfig.TargetFolderName}";
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(uninstallRegKey, false);
            }
            catch { }

            progressCallback?.Invoke("Removing installed helper files...", 95);
            string installDir = InstallEngine.GetInstallDirectory();

            if (Directory.Exists(installDir))
            {
                // Delete helper exe and manifest
                string helperPath = Path.Combine(installDir, InstallerConfig.HelperExeName);
                if (File.Exists(helperPath))
                {
                    try { File.Delete(helperPath); } catch { }
                }

                string manifestPath = Path.Combine(installDir, InstallerConfig.ManifestName);
                if (File.Exists(manifestPath))
                {
                    try { File.Delete(manifestPath); } catch { }
                }

                // Delete any remaining files except uninstaller if running from it
                string currentExe = Environment.ProcessPath ?? string.Empty;
                foreach (var file in Directory.GetFiles(installDir))
                {
                    if (!string.Equals(file, currentExe, StringComparison.OrdinalIgnoreCase))
                    {
                        try { File.Delete(file); } catch { }
                    }
                }

                // If running uninstaller from within the folder, schedule folder deletion via cmd
                ScheduleSelfDeletion(installDir);
            }

            progressCallback?.Invoke("Uninstallation complete!", 100);
            result.Success = true;
            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            return result;
        }
    }

    private static void ScheduleSelfDeletion(string installDir)
    {
        try
        {
            // Execute background cmd to wait 1 second for uninstaller to exit, then remove directory
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/C timeout /t 2 /nobreak > NUL & rmdir /s /q \"{installDir}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            Process.Start(psi);
        }
        catch { }
    }
}
