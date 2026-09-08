using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using ChromeAccountSwitcher.Helper.Models;
using ChromeAccountSwitcher.Helper.NativeMessaging;
using ChromeAccountSwitcher.Helper.Windows;

namespace ChromeAccountSwitcher.Helper.Chrome;

public class ChromeWindowDetector
{
    private readonly Dictionary<string, ChromeProfileInfo> _profilesByDirectory = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ChromeProfileInfo> _knownProfiles = new();

    public ChromeWindowDetector()
    {
        LoadKnownChromeProfiles();
    }

    /// <summary>
    /// Reads Chrome's Local State file from disk to get registered profiles and their display names.
    /// Resolves the current active profile by probe token, active tabs, email, LevelDB write time, focused window, or session recency.
    /// </summary>
    public IReadOnlyList<ChromeProfileInfo> RefreshProfiles(
        string? currentProfile = null,
        string? currentEmail = null,
        string? probeToken = null,
        List<TabItemDto>? currentTabs = null)
    {
        LoadKnownChromeProfiles();

        // 1. HIGHEST PRIORITY: Probe Token matching against each profile's LevelDB storage.
        // When the extension popup opens, it writes a unique probe token to its local storage.
        // LevelDB immediately writes it to disk in that profile's Local Extension Settings directory.
        if (!string.IsNullOrWhiteSpace(probeToken))
        {
            string? probedDir = FindProfileByProbeToken(probeToken.Trim());
            if (!string.IsNullOrEmpty(probedDir))
            {
                currentProfile = probedDir;
            }
        }

        // 2. Open Tab Signatures: inspect tab titles / URLs for known profile email or Gaia Name
        if (string.IsNullOrEmpty(currentProfile) && currentTabs != null && currentTabs.Count > 0)
        {
            foreach (var tab in currentTabs)
            {
                string combined = $"{tab.Title} {tab.Url}";
                if (string.IsNullOrWhiteSpace(combined)) continue;

                var match = _knownProfiles.FirstOrDefault(p =>
                    (!string.IsNullOrWhiteSpace(p.Email) && combined.IndexOf(p.Email, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrWhiteSpace(p.GaiaName) && combined.IndexOf(p.GaiaName, StringComparison.OrdinalIgnoreCase) >= 0));

                if (match != null)
                {
                    currentProfile = match.DirectoryName;
                    break;
                }
            }
        }

        // 3. Match by genuinely detected active user email
        if (string.IsNullOrEmpty(currentProfile) && !string.IsNullOrWhiteSpace(currentEmail))
        {
            var match = _knownProfiles.FirstOrDefault(p =>
                !string.IsNullOrWhiteSpace(p.Email) &&
                string.Equals(p.Email, currentEmail.Trim(), StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                currentProfile = match.DirectoryName;
            }
        }

        // 4. High-precision LevelDB probe: Find which profile directory's extension storage was active
        if (string.IsNullOrEmpty(currentProfile))
        {
            string? extDir = DetectActiveProfileFromExtensionStorage();
            if (!string.IsNullOrEmpty(extDir))
            {
                currentProfile = extDir;
            }
        }

        // 5. If still unresolved, try to detect from focused window
        if (string.IsNullOrEmpty(currentProfile))
        {
            try
            {
                var windows = DetectChromeWindows(out _);
                var focused = windows.FirstOrDefault(w => w.IsFocused && w.IsNormalBrowserWindow);
                if (!string.IsNullOrEmpty(focused?.ProfileDirectory))
                {
                    currentProfile = focused.ProfileDirectory;
                }
            }
            catch
            {
                // Ignore
            }
        }

        // 6. If still unresolved, detect by most recent Chrome profile session activity
        if (string.IsNullOrEmpty(currentProfile))
        {
            try
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string userDataDir = Path.Combine(localAppData, "Google", "Chrome", "User Data");
                if (Directory.Exists(userDataDir))
                {
                    DateTime latestWrite = DateTime.MinValue;
                    string? bestDir = null;

                    foreach (var p in _knownProfiles)
                    {
                        string profPath = Path.Combine(userDataDir, p.DirectoryName);
                        string sessionsPath = Path.Combine(profPath, "Sessions");
                        DateTime profLatest = DateTime.MinValue;

                        if (Directory.Exists(sessionsPath))
                        {
                            var files = new DirectoryInfo(sessionsPath).GetFiles();
                            foreach (var f in files)
                            {
                                if (f.LastWriteTimeUtc > profLatest)
                                {
                                    profLatest = f.LastWriteTimeUtc;
                                }
                            }
                        }

                        if (profLatest > latestWrite)
                        {
                            latestWrite = profLatest;
                            bestDir = p.DirectoryName;
                        }
                    }

                    if (!string.IsNullOrEmpty(bestDir) && (DateTime.UtcNow - latestWrite).TotalHours < 24)
                    {
                        currentProfile = bestDir;
                    }
                }
            }
            catch
            {
                // Ignore
            }
        }

        if (!string.IsNullOrEmpty(currentProfile))
        {
            foreach (var p in _knownProfiles)
            {
                p.IsCurrent = p.DirectoryName.Equals(currentProfile, StringComparison.OrdinalIgnoreCase);
            }
        }

        return _knownProfiles;
    }

    /// <summary>
    /// Scans LevelDB log and ldb files in all profile Local Extension Settings directories
    /// to find which profile directory contains the probe token written by the extension.
    /// </summary>
    public string? FindProfileByProbeToken(string token, string extensionId = "bmjceikfkiikbdhcolbljfaehbghamlf")
    {
        if (string.IsNullOrWhiteSpace(token)) return null;

        try
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string userDataDir = Path.Combine(localAppData, "Google", "Chrome", "User Data");
            if (!Directory.Exists(userDataDir)) return null;

            byte[] tokenBytes = System.Text.Encoding.UTF8.GetBytes(token);

            foreach (var profile in _knownProfiles)
            {
                string extSettingsDir = Path.Combine(userDataDir, profile.DirectoryName, "Local Extension Settings", extensionId);
                if (!Directory.Exists(extSettingsDir)) continue;

                var dirInfo = new DirectoryInfo(extSettingsDir);
                var files = dirInfo.GetFiles()
                    .Where(f => f.Name.EndsWith(".log", StringComparison.OrdinalIgnoreCase) || f.Name.EndsWith(".ldb", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(f => f.LastWriteTimeUtc);

                foreach (var file in files)
                {
                    try
                    {
                        using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        using var ms = new MemoryStream();
                        stream.CopyTo(ms);
                        byte[] data = ms.ToArray();

                        if (ContainsBytes(data, tokenBytes))
                        {
                            return profile.DirectoryName;
                        }
                    }
                    catch
                    {
                        // Ignore file locking/access issues
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"FindProfileByProbeToken error: {ex.Message}");
        }

        return null;
    }

    private static bool ContainsBytes(byte[] source, byte[] pattern)
    {
        if (pattern.Length == 0 || source.Length < pattern.Length) return false;
        for (int i = 0; i <= source.Length - pattern.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < pattern.Length; j++)
            {
                if (source[i + j] != pattern[j])
                {
                    match = false;
                    break;
                }
            }
            if (match) return true;
        }
        return false;
    }

    private void LoadKnownChromeProfiles()
    {
        try
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string localStatePath = Path.Combine(localAppData, "Google", "Chrome", "User Data", "Local State");

            if (!File.Exists(localStatePath))
            {
                return;
            }

            using var fileStream = File.OpenRead(localStatePath);
            using var doc = JsonDocument.Parse(fileStream);

            if (doc.RootElement.TryGetProperty("profile", out var profileElem) &&
                profileElem.TryGetProperty("info_cache", out var infoCacheElem))
            {
                string userDataDir = Path.Combine(localAppData, "Google", "Chrome", "User Data");

                var orderMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                if (profileElem.TryGetProperty("profiles_order", out var orderElem) && orderElem.ValueKind == JsonValueKind.Array)
                {
                    int idx = 0;
                    foreach (var item in orderElem.EnumerateArray())
                    {
                        string? dir = item.GetString();
                        if (!string.IsNullOrEmpty(dir))
                        {
                            orderMap[dir] = idx++;
                        }
                    }
                }

                _knownProfiles.Clear();
                _profilesByDirectory.Clear();

                foreach (var prop in infoCacheElem.EnumerateObject())
                {
                    string dirKey = prop.Name; // e.g. "Default", "Profile 1", "Profile 2"
                    string name = string.Empty;
                    string? gaiaName = null;
                    string? email = null;
                    string? shortcutName = null;
                    string? avatarIcon = null;

                    if (prop.Value.TryGetProperty("name", out var nameProp))
                    {
                        name = nameProp.GetString() ?? string.Empty;
                    }
                    if (prop.Value.TryGetProperty("gaia_name", out var gaiaProp))
                    {
                        gaiaName = gaiaProp.GetString();
                    }
                    if (prop.Value.TryGetProperty("user_name", out var userProp))
                    {
                        email = userProp.GetString();
                    }
                    if (prop.Value.TryGetProperty("shortcut_name", out var scProp))
                    {
                        shortcutName = scProp.GetString();
                    }
                    if (prop.Value.TryGetProperty("avatar_icon", out var avProp))
                    {
                        avatarIcon = avProp.GetString();
                    }

                    int orderIndex = orderMap.TryGetValue(dirKey, out int ord) ? ord : 999;

                    var profile = new ChromeProfileInfo
                    {
                        DirectoryName = dirKey,
                        DisplayName = string.IsNullOrWhiteSpace(name) ? (gaiaName ?? dirKey) : name,
                        GaiaName = gaiaName,
                        Email = email,
                        FullPath = Path.Combine(userDataDir, dirKey),
                        ShortcutName = shortcutName,
                        AvatarIcon = avatarIcon,
                        OrderIndex = orderIndex
                    };

                    _knownProfiles.Add(profile);
                    _profilesByDirectory[dirKey] = profile;
                }

                // Sort by Chrome's user profiles_order, then by directory name
                _knownProfiles.Sort((a, b) =>
                {
                    int cmp = a.OrderIndex.CompareTo(b.OrderIndex);
                    return cmp != 0 ? cmp : string.Compare(a.DirectoryName, b.DirectoryName, StringComparison.OrdinalIgnoreCase);
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error reading Chrome Local State: {ex.Message}");
        }
    }

    public IReadOnlyList<ChromeProfileInfo> GetKnownProfiles() => _knownProfiles;

    public ChromeProfileInfo? GetProfileByDirectory(string directory)
    {
        if (string.IsNullOrEmpty(directory)) return null;
        return _profilesByDirectory.TryGetValue(directory, out var p) ? p : null;
    }

    /// <summary>
    /// Scans the system for all active Chrome browser windows and produces Profile -> Window mappings.
    /// </summary>
    public List<ChromeWindow> DetectChromeWindows(out int totalScanned)
    {
        var detectedWindows = new List<ChromeWindow>();
        IntPtr currentForeground = WindowManager.GetForegroundWindow();
        int count = 0;

        WindowManager.EnumerateAllTopLevelWindows((hWnd, lParam) =>
        {
            count++;
            WindowManager.GetWindowThreadProcessId(hWnd, out uint processId);
            if (processId == 0) return true;

            string className = WindowManager.GetWindowClass(hWnd);
            if (!className.StartsWith("Chrome_WidgetWin", StringComparison.OrdinalIgnoreCase)) return true;

            string title = WindowManager.GetWindowTitle(hWnd);
            bool isVisible = WindowManager.IsWindowVisible(hWnd);
            bool isIconic = WindowManager.IsIconic(hWnd);
            bool isZoomed = WindowManager.IsZoomed(hWnd);

            // Check if process name is chrome
            string procName = string.Empty;
            try
            {
                using var proc = Process.GetProcessById((int)processId);
                procName = proc.ProcessName;
            }
            catch
            {
                return true;
            }

            if (!procName.Equals("chrome", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            bool isFocused = (hWnd == currentForeground);
            // Detect all Chrome top-level windows
            bool isNormalBrowser = !string.IsNullOrWhiteSpace(title) || isVisible || isIconic;

            var winInfo = new ChromeWindow
            {
                Hwnd = hWnd,
                Title = string.IsNullOrWhiteSpace(title) ? $"(Chrome Window without Title - Class: {className})" : title,
                ProcessId = (int)processId,
                ClassName = className,
                IsVisible = isVisible,
                IsMinimized = isIconic,
                IsMaximized = isZoomed,
                IsFocused = isFocused,
                IsNormalBrowserWindow = isNormalBrowser
            };

            // Attempt to resolve profile association reliably
            ResolveProfileMapping(winInfo);

            detectedWindows.Add(winInfo);
            return true;
        });

        totalScanned = count;
        return detectedWindows;
    }

    /// <summary>
    /// Resolves profile directory and metadata using process inspection, command-line parsing, and Local State.
    /// </summary>
    private void ResolveProfileMapping(ChromeWindow window)
    {
        // Method 1: Process Command Line inspection
        string? cmdLine = ProcessHelper.GetProcessCommandLine(window.ProcessId);
        window.CommandLine = cmdLine;

        if (!string.IsNullOrWhiteSpace(cmdLine))
        {
            string? dirFromCmd = ProcessHelper.ExtractProfileDirectoryFromCommandLine(cmdLine);
            
            // If this is a child process without --profile-directory, check parent process
            if (string.IsNullOrEmpty(dirFromCmd) && cmdLine.Contains("--type=", StringComparison.OrdinalIgnoreCase))
            {
                int parentPid = ProcessHelper.GetParentProcessId(window.ProcessId);
                if (parentPid != 0)
                {
                    string? parentCmd = ProcessHelper.GetProcessCommandLine(parentPid);
                    if (!string.IsNullOrWhiteSpace(parentCmd))
                    {
                        dirFromCmd = ProcessHelper.ExtractProfileDirectoryFromCommandLine(parentCmd);
                    }
                }
            }

            if (!string.IsNullOrEmpty(dirFromCmd))
            {
                AssignProfile(window, dirFromCmd, $"Process Command-Line inspection (--profile-directory='{dirFromCmd}')");
                return;
            }
        }

        // Method 2: Match known Profile Display Name or Shortcut Name in Window Title
        if (!string.IsNullOrWhiteSpace(window.Title))
        {
            foreach (var profile in _knownProfiles)
            {
                if (!string.IsNullOrEmpty(profile.DisplayName) &&
                    window.Title.IndexOf(profile.DisplayName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    AssignProfile(window, profile.DirectoryName, $"Matched profile display name '{profile.DisplayName}' in title");
                    return;
                }

                if (!string.IsNullOrEmpty(profile.ShortcutName) &&
                    window.Title.IndexOf(profile.ShortcutName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    AssignProfile(window, profile.DirectoryName, $"Matched profile shortcut '{profile.ShortcutName}' in title");
                    return;
                }
            }
        }

        // Method 3: Single profile fallback
        if (_knownProfiles.Count == 1)
        {
            var single = _knownProfiles[0];
            AssignProfile(window, single.DirectoryName, $"Single registered profile on system ({single.DirectoryName})");
            return;
        }

        // Unresolved / Unknown mapping
        window.ProfileDirectory = null;
        window.ProfileDisplayName = null;
        window.ProfileEmail = null;
        window.MappingSource = "UNKNOWN (Insufficient process/window profile signature)";
    }

    private void AssignProfile(ChromeWindow window, string directoryName, string source)
    {
        window.ProfileDirectory = directoryName;
        window.MappingSource = source;

        if (_profilesByDirectory.TryGetValue(directoryName, out var info))
        {
            window.ProfileDisplayName = info.DisplayName;
            window.ProfileEmail = info.Email;
        }
        else
        {
            window.ProfileDisplayName = directoryName;
            window.ProfileEmail = null;
        }
    }

    /// <summary>
    /// Detects which Chrome profile directory hosts the calling extension instance
    /// by finding which profile's Local Extension Settings directory was most recently written to.
    /// Each Chrome profile maintains an isolated LevelDB on disk for the extension.
    /// </summary>
    public string? DetectActiveProfileFromExtensionStorage(string extensionId = "bmjceikfkiikbdhcolbljfaehbghamlf")
    {
        try
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string userDataDir = Path.Combine(localAppData, "Google", "Chrome", "User Data");
            if (!Directory.Exists(userDataDir)) return null;

            DateTime latestWrite = DateTime.MinValue;
            string? bestProfile = null;

            foreach (var profile in _knownProfiles)
            {
                string extSettingsDir = Path.Combine(userDataDir, profile.DirectoryName, "Local Extension Settings", extensionId);
                if (Directory.Exists(extSettingsDir))
                {
                    var dirInfo = new DirectoryInfo(extSettingsDir);
                    if (dirInfo.LastWriteTimeUtc > latestWrite)
                    {
                        latestWrite = dirInfo.LastWriteTimeUtc;
                        bestProfile = profile.DirectoryName;
                    }

                    var files = dirInfo.GetFiles();
                    foreach (var file in files)
                    {
                        if (file.LastWriteTimeUtc > latestWrite)
                        {
                            latestWrite = file.LastWriteTimeUtc;
                            bestProfile = profile.DirectoryName;
                        }
                    }
                }
            }

            return bestProfile;
        }
        catch
        {
            return null;
        }
    }
}
