using System;
using System.Runtime.InteropServices;

namespace ChromeAccountSwitcher.Helper.Windows;

public static class HotKeyHelper
{
    // Win32 Modifier Constants
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    /// <summary>
    /// Parses a human-readable shortcut string (e.g. "Alt + Shift + 2", "Ctrl + Alt + 3") into Win32 modifiers and VK code.
    /// </summary>
    public static bool TryParseShortcut(string? shortcut, out uint modifiers, out uint vk)
    {
        modifiers = 0;
        vk = 0;

        if (string.IsNullOrWhiteSpace(shortcut))
            return false;

        string[] parts = shortcut.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return false; // Must have at least one modifier and one key

        string? keyPart = null;

        foreach (var part in parts)
        {
            var p = part.Trim();
            if (p.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || p.Equals("Control", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= MOD_CONTROL;
            }
            else if (p.Equals("Alt", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= MOD_ALT;
            }
            else if (p.Equals("Shift", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= MOD_SHIFT;
            }
            else if (p.Equals("Win", StringComparison.OrdinalIgnoreCase) || p.Equals("Meta", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= MOD_WIN;
            }
            else
            {
                keyPart = p;
            }
        }

        if (modifiers == 0 || string.IsNullOrEmpty(keyPart))
            return false;

        vk = ParseVirtualKey(keyPart);
        if (vk == 0)
            return false;

        modifiers |= MOD_NOREPEAT;
        return true;
    }

    private static uint ParseVirtualKey(string key)
    {
        key = key.Trim();

        // Single alphanumeric character
        if (key.Length == 1)
        {
            char c = char.ToUpperInvariant(key[0]);
            if (c >= 'A' && c <= 'Z')
                return (uint)c;
            if (c >= '0' && c <= '9')
                return (uint)c;
        }

        // Function keys F1 - F12
        if (key.StartsWith("F", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(key.Substring(1), out int fNum) && fNum >= 1 && fNum <= 12)
        {
            return (uint)(0x70 + (fNum - 1)); // VK_F1 = 0x70
        }

        // Named keys
        return key.ToUpperInvariant() switch
        {
            "UP" or "ARROWUP" => 0x26, // VK_UP
            "DOWN" or "ARROWDOWN" => 0x28, // VK_DOWN
            "LEFT" or "ARROWLEFT" => 0x25, // VK_LEFT
            "RIGHT" or "ARROWRIGHT" => 0x27, // VK_RIGHT
            "HOME" => 0x24,
            "END" => 0x23,
            "PAGEUP" or "PGUP" => 0x21,
            "PAGEDOWN" or "PGDN" => 0x22,
            "INSERT" => 0x2D,
            "DELETE" or "DEL" => 0x2E,
            "ESC" or "ESCAPE" => 0x1B,
            "TAB" => 0x09,
            "SPACE" => 0x20,
            "ENTER" or "RETURN" => 0x0D,
            "BACKSPACE" => 0x08,
            "," => 0xBC, // VK_OEM_COMMA
            "." => 0xBE, // VK_OEM_PERIOD
            "/" => 0xBF, // VK_OEM_2
            ";" => 0xBA, // VK_OEM_1
            "'" => 0xDE, // VK_OEM_7
            "[" => 0xDB, // VK_OEM_4
            "]" => 0xDD, // VK_OEM_6
            "\\" => 0xDC, // VK_OEM_5
            "-" => 0xBD, // VK_OEM_MINUS
            "=" => 0xBB, // VK_OEM_PLUS
            "`" => 0xC0, // VK_OEM_3
            _ => 0
        };
    }
}
