using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ChromeAccountSwitcher.Helper.NativeMessaging;

public class TabItemDto
{
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("active")]
    public bool Active { get; set; }
}

public class NativeMessageRequest
{
    [JsonPropertyName("action")]
    public string? Action { get; set; }

    [JsonPropertyName("slot")]
    public int? Slot { get; set; }

    [JsonPropertyName("profileDirectory")]
    public string? ProfileDirectory { get; set; }

    [JsonPropertyName("copyTabs")]
    public bool? CopyTabs { get; set; }

    [JsonPropertyName("sourceProfile")]
    public string? SourceProfile { get; set; }

    [JsonPropertyName("tabs")]
    public List<TabItemDto>? Tabs { get; set; }

    [JsonPropertyName("shortcut")]
    public string? Shortcut { get; set; }

    [JsonPropertyName("slots")]
    public List<SlotConfigEntry>? Slots { get; set; }
}

public class ChromeProfileDto
{
    [JsonPropertyName("directory")]
    public string Directory { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("gaiaName")]
    public string? GaiaName { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("avatarIcon")]
    public string? AvatarIcon { get; set; }

    [JsonPropertyName("orderIndex")]
    public int OrderIndex { get; set; }

    [JsonPropertyName("isCurrent")]
    public bool IsCurrent { get; set; }

    [JsonPropertyName("shortcut")]
    public string? Shortcut { get; set; }
}

public class NativeMessageResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("profile")]
    public string? Profile { get; set; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("sourceProfile")]
    public string? SourceProfile { get; set; }

    [JsonPropertyName("targetProfile")]
    public string? TargetProfile { get; set; }

    [JsonPropertyName("tabsCopied")]
    public int TabsCopied { get; set; }

    [JsonPropertyName("tabsSkipped")]
    public int TabsSkipped { get; set; }

    [JsonPropertyName("windowHandle")]
    public long? WindowHandle { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("slots")]
    public IReadOnlyList<SlotConfigEntry>? Slots { get; set; }

    [JsonPropertyName("profiles")]
    public List<ChromeProfileDto>? Profiles { get; set; }

    [JsonPropertyName("currentProfile")]
    public string? CurrentProfile { get; set; }

    [JsonPropertyName("slot")]
    public int? Slot { get; set; }

    [JsonPropertyName("shortcut")]
    public string? Shortcut { get; set; }

    [JsonPropertyName("hotkeys")]
    public List<Hotkeys.HotkeyDefinition>? Hotkeys { get; set; }
}

public class ShortcutDto
{
    [JsonPropertyName("ctrl")]
    public bool Ctrl { get; set; }

    [JsonPropertyName("alt")]
    public bool Alt { get; set; }

    [JsonPropertyName("shift")]
    public bool Shift { get; set; }

    [JsonPropertyName("win")]
    public bool Win { get; set; }

    [JsonPropertyName("key")]
    public string? Key { get; set; }

    public string ToShortcutString()
    {
        var parts = new List<string>();
        if (Ctrl) parts.Add("Ctrl");
        if (Alt) parts.Add("Alt");
        if (Shift) parts.Add("Shift");
        if (Win) parts.Add("Win");
        if (!string.IsNullOrWhiteSpace(Key)) parts.Add(Key.Trim());
        return string.Join(" + ", parts);
    }
}
