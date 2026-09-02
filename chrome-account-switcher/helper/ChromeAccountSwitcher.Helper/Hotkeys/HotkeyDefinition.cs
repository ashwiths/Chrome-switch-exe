using System;

namespace ChromeAccountSwitcher.Helper.Hotkeys;

public class HotkeyDefinition
{
    public int Id { get; set; }
    public int Slot { get; set; }
    public string ProfileDirectory { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string Shortcut { get; set; } = string.Empty;
    public uint Modifiers { get; set; }
    public uint Vk { get; set; }
    public bool IsRegistered { get; set; }
    public string? RegistrationError { get; set; }
}
