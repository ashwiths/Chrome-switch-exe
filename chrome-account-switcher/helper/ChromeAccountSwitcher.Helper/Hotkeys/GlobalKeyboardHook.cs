using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using ChromeAccountSwitcher.Helper.Chrome;
using ChromeAccountSwitcher.Helper.NativeMessaging;
using ChromeAccountSwitcher.Helper.Windows;

namespace ChromeAccountSwitcher.Helper.Hotkeys;

public class GlobalKeyboardHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const uint WM_QUIT = 0x0012;
    private const uint WM_USER_REFRESH = 0x0400 + 1;

    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12; // Alt
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public UIntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage([In] ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage([In] ref MSG lpMsg);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostThreadMessage(uint idThread, uint Msg, UIntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    private readonly ChromeWindowDetector _detector;
    private readonly SlotConfigManager _slotManager;
    private readonly object _lock = new();

    // Whitelist map: (ModifiersMask, VkCode) -> SlotConfigEntry
    // Modifiers bitmask: Alt=1, Ctrl=2, Shift=4, Win=8
    private readonly Dictionary<(uint Mods, uint Vk), SlotConfigEntry> _activeMap = new();

    private Thread? _hookThread;
    private uint _threadId;
    private IntPtr _hookId = IntPtr.Zero;
    private LowLevelKeyboardProc? _proc;
    private readonly ManualResetEventSlim _startedEvent = new();
    private FileSystemWatcher? _fileWatcher;
    private Mutex? _daemonMutex;
    private bool _isPrimaryDaemon;
    private bool _disposed;

    public GlobalKeyboardHook(ChromeWindowDetector detector, SlotConfigManager slotManager)
    {
        _detector = detector;
        _slotManager = slotManager;
    }

    public void Start()
    {
        if (_hookThread != null) return;

        try
        {
            _daemonMutex = new Mutex(true, @"Local\ChromeAccountSwitcher_HotkeyDaemon", out bool createdNew);
            _isPrimaryDaemon = createdNew;
        }
        catch
        {
            _isPrimaryDaemon = true;
        }

        if (!_isPrimaryDaemon)
        {
            Console.Error.WriteLine("[GlobalKeyboardHook] Primary background daemon is already running. Delegating shortcuts to primary daemon.");
            return;
        }

        // Setup FileSystemWatcher for slots.json so any updates reload automatically
        try
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string configDir = Path.Combine(appData, "ChromeAccountSwitcher");
            if (Directory.Exists(configDir))
            {
                _fileWatcher = new FileSystemWatcher(configDir, "slots.json")
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size
                };
                _fileWatcher.Changed += (s, e) =>
                {
                    Thread.Sleep(150); // Debounce write
                    Refresh();
                };
                _fileWatcher.EnableRaisingEvents = true;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[GlobalKeyboardHook] FileSystemWatcher error: {ex.Message}");
        }

        _hookThread = new Thread(RunHookLoop)
        {
            IsBackground = true,
            Name = "Win32GlobalKeyboardHookThread"
        };
        _hookThread.SetApartmentState(ApartmentState.STA);
        _hookThread.Start();

        _startedEvent.Wait(2000);
    }

    public void Refresh()
    {
        if (_threadId != 0)
        {
            PostThreadMessage(_threadId, WM_USER_REFRESH, UIntPtr.Zero, IntPtr.Zero);
        }
    }

    public List<HotkeyDefinition> GetActiveHotkeys()
    {
        lock (_lock)
        {
            return _activeMap.Values.Select((s, i) => new HotkeyDefinition
            {
                Id = 1000 + s.Slot,
                Slot = s.Slot,
                ProfileDirectory = s.ProfileDirectory,
                DisplayName = s.DisplayName,
                Shortcut = s.Shortcut ?? string.Empty,
                IsRegistered = true
            }).ToList();
        }
    }

    private void RunHookLoop()
    {
        _threadId = GetCurrentThreadId();
        _proc = HookCallback;

        using (Process curProcess = Process.GetCurrentProcess())
        using (ProcessModule curModule = curProcess.MainModule!)
        {
            _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(curModule.ModuleName), 0);
        }

        if (_hookId == IntPtr.Zero)
        {
            int err = Marshal.GetLastWin32Error();
            Console.Error.WriteLine($"[GlobalKeyboardHook] Failed to install WH_KEYBOARD_LL hook. Win32 Error: {err}");
            _startedEvent.Set();
            return;
        }

        RebuildShortcutMap();
        _startedEvent.Set();

        Console.Error.WriteLine($"[GlobalKeyboardHook] Low-level keyboard hook active (Hook Handle: 0x{_hookId.ToInt64():X8}). Ready.");

        while (!_disposed && GetMessage(out MSG msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (msg.message == WM_USER_REFRESH)
            {
                Console.Error.WriteLine("[GlobalKeyboardHook] WM_USER_REFRESH received. Reloading shortcut configuration...");
                RebuildShortcutMap();
            }

            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }

    private void RebuildShortcutMap()
    {
        lock (_lock)
        {
            _activeMap.Clear();
            var slots = _slotManager.GetAllSlots();

            Console.Error.WriteLine($"[GlobalKeyboardHook] Configuring {slots.Count} profile shortcuts:");

            foreach (var slot in slots)
            {
                if (string.IsNullOrWhiteSpace(slot.Shortcut) || string.IsNullOrWhiteSpace(slot.ProfileDirectory))
                    continue;

                if (HotKeyHelper.TryParseShortcut(slot.Shortcut, out uint rawMods, out uint vk))
                {
                    // Clean modifiers: strip MOD_NOREPEAT (0x4000) for exact physical key state matching
                    uint cleanMods = rawMods & 0x000F;
                    var key = (cleanMods, vk);

                    _activeMap[key] = slot;
                    char c = (vk >= 0x30 && vk <= 0x39) ? (char)vk : '?';
                    Console.Error.WriteLine($"  Slot {slot.Slot:D2}: '{slot.Shortcut}' -> VK=0x{vk:X2} ('{c}'), Mods=0x{cleanMods:X2} (Profile: '{slot.ProfileDirectory}')");
                }
            }

            Console.Error.WriteLine($"[GlobalKeyboardHook] Active configured shortcuts: {_activeMap.Count}");
        }
    }

    private const uint LLKHF_ALTDOWN = 0x20;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYUP = 0x0105;

    // Track physical modifier states directly within the hook
    private bool _isAltDown;
    private bool _isCtrlDown;
    private bool _isShiftDown;
    private bool _isWinDown;

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            KBDLLHOOKSTRUCT kbd = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            uint vk = kbd.vkCode;
            int msg = wParam.ToInt32();

            // 1. Update modifier states on KeyDown / KeyUp
            bool isKeyDown = (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN);
            bool isKeyUp = (msg == WM_KEYUP || msg == WM_SYSKEYUP);

            if (vk == 0x12 || vk == 0xA4 || vk == 0xA5) // VK_MENU, VK_LMENU, VK_RMENU
            {
                _isAltDown = isKeyDown;
            }
            else if (vk == 0x11 || vk == 0xA2 || vk == 0xA3) // VK_CONTROL, VK_LCONTROL, VK_RCONTROL
            {
                _isCtrlDown = isKeyDown;
            }
            else if (vk == 0x10 || vk == 0xA0 || vk == 0xA1) // VK_SHIFT, VK_LSHIFT, VK_RSHIFT
            {
                _isShiftDown = isKeyDown;
            }
            else if (vk == 0x5B || vk == 0x5C) // VK_LWIN, VK_RWIN
            {
                _isWinDown = isKeyDown;
            }

            // Only inspect on KeyDown events
            if (isKeyDown)
            {
                // Multi-layered modifier detection (State tracking + LLKHF_ALTDOWN + GetAsyncKeyState)
                bool alt = _isAltDown ||
                           (kbd.flags & LLKHF_ALTDOWN) != 0 ||
                           (msg == WM_SYSKEYDOWN) ||
                           (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;

                bool ctrl = _isCtrlDown || (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
                bool shift = _isShiftDown || (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;
                bool win = _isWinDown || ((GetAsyncKeyState(VK_LWIN) & 0x8000) != 0) || ((GetAsyncKeyState(VK_RWIN) & 0x8000) != 0);

                // Fast-path: If NO modifier is active, pass through immediately.
                // Standard typing (letters, numbers, backspace, enter, space) is 100% unaffected.
                if (!alt && !ctrl && !shift && !win)
                {
                    return CallNextHookEx(_hookId, nCode, wParam, lParam);
                }

                // If the key pressed IS a modifier key itself (Alt, Ctrl, Shift, Win), pass through
                if (vk == 0x12 || vk == 0xA4 || vk == 0xA5 ||
                    vk == 0x11 || vk == 0xA2 || vk == 0xA3 ||
                    vk == 0x10 || vk == 0xA0 || vk == 0xA1 ||
                    vk == 0x5B || vk == 0x5C)
                {
                    return CallNextHookEx(_hookId, nCode, wParam, lParam);
                }

                uint currentMods = (alt ? 1u : 0u) | (ctrl ? 2u : 0u) | (shift ? 4u : 0u) | (win ? 8u : 0u);

                SlotConfigEntry? targetSlot = null;
                lock (_lock)
                {
                    _activeMap.TryGetValue((currentMods, vk), out targetSlot);
                }

                if (targetSlot != null)
                {
                    char c = (vk >= 0x30 && vk <= 0x39) ? (char)vk : '?';
                    Console.Error.WriteLine($"[GlobalKeyboardHook] >>> SHORTCUT MATCH: Slot {targetSlot.Slot} ('{targetSlot.Shortcut}') [VK=0x{vk:X2} '{c}', Mods=0x{currentMods:X2}] -> Switching to '{targetSlot.ProfileDirectory}'");

                    // Execute profile switch asynchronously on a background worker thread
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        try
                        {
                            var result = NativeMessageHost.HandleRequest(new NativeMessageRequest
                            {
                                Action = "switch-profile",
                                Slot = targetSlot.Slot,
                                ProfileDirectory = targetSlot.ProfileDirectory,
                                CopyTabs = false
                            }, _detector, _slotManager);

                            Console.Error.WriteLine($"[GlobalKeyboardHook] Switch Finished: Success={result.Success}, Profile='{result.Profile}', HWND=0x{result.WindowHandle:X8}, Error='{result.Error}'");
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine($"[GlobalKeyboardHook] Switch execution error: {ex.Message}");
                        }
                    });

                    // Swallow the key combination to prevent default Windows/Chrome accelerator collisions
                    return (IntPtr)1;
                }
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_threadId != 0)
        {
            PostThreadMessage(_threadId, WM_QUIT, UIntPtr.Zero, IntPtr.Zero);
        }

        if (_fileWatcher != null)
        {
            _fileWatcher.Dispose();
        }

        if (_daemonMutex != null)
        {
            try { _daemonMutex.ReleaseMutex(); } catch { }
            _daemonMutex.Dispose();
        }

        _startedEvent.Dispose();
    }
}
