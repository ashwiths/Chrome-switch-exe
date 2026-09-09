using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace ChromeAccountSwitcher.Helper.Windows;

/// <summary>
/// High-speed, cross-process persistent cache mapping Chrome Profile Directory -> Window Handle (HWND).
/// Enables instantaneous (1ms) window switching without polling or process enumerations.
/// </summary>
public static class ProfileWindowCache
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ChromeAccountSwitcher");

    private static readonly string CacheFile = Path.Combine(ConfigDir, "windows.json");

    private static readonly ConcurrentDictionary<string, long> _cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object _lock = new();
    private static DateTime _lastFileRead = DateTime.MinValue;

    static ProfileWindowCache()
    {
        EnsureDirectory();
        LoadFromFile();
    }

    private static void EnsureDirectory()
    {
        try
        {
            if (!Directory.Exists(ConfigDir))
            {
                Directory.CreateDirectory(ConfigDir);
            }
        }
        catch { }
    }

    private static void LoadFromFile()
    {
        lock (_lock)
        {
            try
            {
                if (!File.Exists(CacheFile)) return;

                var writeTime = File.GetLastWriteTimeUtc(CacheFile);
                if (writeTime <= _lastFileRead) return;

                string json = File.ReadAllText(CacheFile);
                var dict = JsonSerializer.Deserialize<Dictionary<string, long>>(json);
                if (dict != null)
                {
                    foreach (var kvp in dict)
                    {
                        _cache[kvp.Key] = kvp.Value;
                    }
                }
                _lastFileRead = writeTime;
            }
            catch { }
        }
    }

    private static void SaveToFile()
    {
        lock (_lock)
        {
            try
            {
                EnsureDirectory();
                var dict = new Dictionary<string, long>(_cache, StringComparer.OrdinalIgnoreCase);
                string json = JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(CacheFile, json);
                _lastFileRead = File.GetLastWriteTimeUtc(CacheFile);
            }
            catch { }
        }
    }

    /// <summary>
    /// Records or updates the HWND for a given profile directory.
    /// </summary>
    public static void RecordWindow(string profileDirectory, IntPtr hwnd)
    {
        if (string.IsNullOrWhiteSpace(profileDirectory) || hwnd == IntPtr.Zero) return;

        _cache[profileDirectory] = hwnd.ToInt64();
        SaveToFile();
    }

    /// <summary>
    /// Retrieves a valid HWND for the specified profile directory.
    /// Returns null if not cached or if the window is no longer alive in Win32.
    /// </summary>
    public static IntPtr? GetWindow(string profileDirectory)
    {
        if (string.IsNullOrWhiteSpace(profileDirectory)) return null;

        LoadFromFile();

        if (_cache.TryGetValue(profileDirectory, out long rawHwnd))
        {
            IntPtr hwnd = new IntPtr(rawHwnd);
            if (WindowManager.IsWindow(hwnd))
            {
                return hwnd;
            }

            // Window was closed, invalidate entry
            _cache.TryRemove(profileDirectory, out _);
            SaveToFile();
        }

        return null;
    }

    /// <summary>
    /// Explicitly invalidates a profile's cached window handle.
    /// </summary>
    public static void Invalidate(string profileDirectory)
    {
        if (string.IsNullOrWhiteSpace(profileDirectory)) return;

        if (_cache.TryRemove(profileDirectory, out _))
        {
            SaveToFile();
        }
    }
}
