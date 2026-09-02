using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ChromeAccountSwitcher.Helper.Chrome;
using ChromeAccountSwitcher.Helper.Models;

namespace ChromeAccountSwitcher.Helper.NativeMessaging;

public class SlotConfigEntry
{
    public int Slot { get; set; }
    public string ProfileDirectory { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? Shortcut { get; set; }
}

public class SlotConfigManager
{
    private readonly Dictionary<int, SlotConfigEntry> _slots = new();
    private readonly string _configFilePath;

    public SlotConfigManager(ChromeWindowDetector detector)
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string configDir = Path.Combine(appData, "ChromeAccountSwitcher");
        Directory.CreateDirectory(configDir);
        _configFilePath = Path.Combine(configDir, "slots.json");

        LoadSlots(detector);
    }

    private void LoadSlots(ChromeWindowDetector detector)
    {
        if (File.Exists(_configFilePath))
        {
            try
            {
                string json = File.ReadAllText(_configFilePath);
                var list = JsonSerializer.Deserialize<List<SlotConfigEntry>>(json);
                if (list != null && list.Count > 0)
                {
                    foreach (var entry in list)
                    {
                        if (string.IsNullOrEmpty(entry.Shortcut) && entry.Slot <= 5)
                        {
                            entry.Shortcut = $"Alt + Shift + {entry.Slot}";
                        }
                        _slots[entry.Slot] = entry;
                    }
                    return;
                }
            }
            catch
            {
                // Fallback to auto-detection
            }
        }

        // Initialize default slot assignments from ALL registered profiles dynamically
        var known = detector.GetKnownProfiles();
        for (int i = 0; i < known.Count; i++)
        {
            var p = known[i];
            int slotNum = i + 1;
            _slots[slotNum] = new SlotConfigEntry
            {
                Slot = slotNum,
                ProfileDirectory = p.DirectoryName,
                DisplayName = p.DisplayName,
                Shortcut = slotNum <= 4 ? $"Alt + Shift + {slotNum}" : null
            };
        }

        // Fallbacks if no profiles found
        if (!_slots.ContainsKey(1)) _slots[1] = new SlotConfigEntry { Slot = 1, ProfileDirectory = "Default", DisplayName = "Default", Shortcut = "Alt + Shift + 1" };
        if (!_slots.ContainsKey(2)) _slots[2] = new SlotConfigEntry { Slot = 2, ProfileDirectory = "Profile 1", DisplayName = "Slot 2", Shortcut = "Alt + Shift + 2" };

        SaveSlots();
    }

    public void SaveSlots()
    {
        try
        {
            var list = _slots.Values.OrderBy(s => s.Slot).ToList();
            string json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configFilePath, json);
        }
        catch
        {
            // Ignore
        }
    }

    public SlotConfigEntry? GetSlot(int slot)
    {
        return _slots.TryGetValue(slot, out var entry) ? entry : null;
    }

    public IReadOnlyList<SlotConfigEntry> GetAllSlots()
    {
        return _slots.Values.OrderBy(s => s.Slot).ToList();
    }

    public void SetSlot(int slot, string profileDirectory, string? displayName = null, string? shortcut = null)
    {
        _slots[slot] = new SlotConfigEntry
        {
            Slot = slot,
            ProfileDirectory = profileDirectory,
            DisplayName = displayName,
            Shortcut = shortcut ?? _slots.GetValueOrDefault(slot)?.Shortcut
        };
        SaveSlots();
    }

    public void SetSlotShortcut(int slot, string? shortcut)
    {
        if (_slots.TryGetValue(slot, out var entry))
        {
            entry.Shortcut = shortcut;
            SaveSlots();
        }
    }

    public void SyncSlots(IEnumerable<SlotConfigEntry> updatedSlots)
    {
        foreach (var s in updatedSlots)
        {
            if (s.Slot >= 1 && s.Slot <= 50)
            {
                if (_slots.TryGetValue(s.Slot, out var existing))
                {
                    if (!string.IsNullOrEmpty(s.ProfileDirectory)) existing.ProfileDirectory = s.ProfileDirectory;
                    if (!string.IsNullOrEmpty(s.DisplayName)) existing.DisplayName = s.DisplayName;
                    existing.Shortcut = s.Shortcut;
                }
                else
                {
                    _slots[s.Slot] = s;
                }
            }
        }
        SaveSlots();
    }
}
