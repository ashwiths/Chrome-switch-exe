using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Win32;

namespace ChromeAccountSwitcher.Helper.Chrome;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public static class ChromeLauncher
{
    private static string? _cachedChromePath;

    /// <summary>
    /// Locates the Google Chrome executable on the system.
    /// </summary>
    public static string? FindChromeExecutable()
    {
        if (!string.IsNullOrEmpty(_cachedChromePath) && File.Exists(_cachedChromePath))
        {
            return _cachedChromePath;
        }

        // 1. Check Standard Program Files paths
        string[] candidatePaths =
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "Application", "chrome.exe")
        };

        foreach (var path in candidatePaths)
        {
            if (File.Exists(path))
            {
                _cachedChromePath = path;
                return path;
            }
        }

        // 2. Check Registry App Paths
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe");
            var val = key?.GetValue("") as string;
            if (!string.IsNullOrEmpty(val) && File.Exists(val))
            {
                _cachedChromePath = val;
                return val;
            }
        }
        catch
        {
            // Ignore registry read errors
        }

        return null;
    }

    /// <summary>
    /// Validates and sanitizes a URL to ensure it is a safe HTTP or HTTPS URL.
    /// </summary>
    public static bool IsValidHttpUrl(string? url, out string sanitizedUrl)
    {
        sanitizedUrl = string.Empty;
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        string trimmed = url.Trim();
        // Prevent quotes or control characters that could break CLI arguments
        if (trimmed.Contains('"') || trimmed.Contains('\r') || trimmed.Contains('\n') || trimmed.Contains('\0'))
        {
            return false;
        }

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uriResult) &&
            (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps))
        {
            sanitizedUrl = uriResult.AbsoluteUri;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Launches Chrome or instructs the existing Chrome instance to open URLs in the specified profile.
    /// </summary>
    public static bool OpenUrlsInProfile(string profileDirectory, IEnumerable<string> urls)
    {
        string? chromeExe = FindChromeExecutable();
        if (string.IsNullOrEmpty(chromeExe))
        {
            return false;
        }

        var argBuilder = new StringBuilder();
        argBuilder.Append($"--profile-directory=\"{profileDirectory}\"");

        foreach (var url in urls)
        {
            if (IsValidHttpUrl(url, out var safeUrl))
            {
                argBuilder.Append($" \"{safeUrl}\"");
            }
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = chromeExe,
                Arguments = argBuilder.ToString(),
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(startInfo);
            return proc != null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to launch Chrome with URLs: {ex.Message}");
            return false;
        }
    }
}
