using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using ChromeAccountSwitcher.Helper.Chrome;
using ChromeAccountSwitcher.Helper.NativeMessaging;
using ChromeAccountSwitcher.Helper.Windows;

namespace ChromeAccountSwitcher.Helper.Hotkeys;

public class GlobalHotkeyManager : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const uint WM_USER_REFRESH = 0x0400 + 1;
    private const uint WM_QUIT = 0x0012;

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

    private Thread? _messageLoopThread;
    private uint _threadId;
    private readonly ManualResetEventSlim _startedEvent = new();
    private readonly object _lock = new();
    private readonly Dictionary<int, HotkeyDefinition> _registeredHotkeys = new();
    private readonly ChromeWindowDetector _detector;
    private readonly SlotConfigManager _slotManager;
    private Mutex? _daemonMutex;
    private bool _isPrimaryDaemon;
    private bool _disposed;

    public event Action<HotkeyDefinition>? HotkeyTriggered;

    public GlobalHotkeyManager(ChromeWindowDetector detector, SlotConfigManager slotManager)
    {
        _detector = detector;
        _slotManager = slotManager;
    }

    private FileSystemWatcher? _fileWatcher;

    public void Start()
    {
        if (_messageLoopThread != null) return;

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
            Console.Error.WriteLine("[GlobalHotkeyManager] Primary background daemon is already running. Delegating hotkeys to primary daemon.");
            return;
        }

        // Watch slots.json for changes made by the Chrome extension
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
            Console.Error.WriteLine($"[GlobalHotkeyManager] FileSystemWatcher error: {ex.Message}");
        }

        _messageLoopThread = new Thread(RunMessageLoop)
        {
            IsBackground = true,
            Name = "Win32GlobalHotkeyThread"
        };
        _messageLoopThread.SetApartmentState(ApartmentState.STA);
        _messageLoopThread.Start();

        _startedEvent.Wait(2000);
    }

    public void Refresh()
    {
        if (_threadId != 0)
        {
            PostThreadMessage(_threadId, WM_USER_REFRESH, UIntPtr.Zero, IntPtr.Zero);
        }
    }

    public (bool Success, string? Error) ValidateShortcut(string shortcut)
    {
        if (!HotKeyHelper.TryParseShortcut(shortcut, out uint mods, out uint vk))
        {
            return (false, "Invalid shortcut format or missing modifier.");
        }

        int testId = 9999;
        bool ok = HotKeyHelper.RegisterHotKey(IntPtr.Zero, testId, mods, vk);
        if (!ok)
        {
            int err = Marshal.GetLastWin32Error();
            if (err == 1409)
            {
                return (false, "Shortcut is already in use by Windows or another application.");
            }
            return (false, $"Windows rejected shortcut (Win32 Error: {err}).");
        }

        HotKeyHelper.UnregisterHotKey(IntPtr.Zero, testId);
        return (true, null);
    }

    public List<HotkeyDefinition> GetActiveHotkeys()
    {
        lock (_lock)
        {
            return new List<HotkeyDefinition>(_registeredHotkeys.Values);
        }
    }

    private void RunMessageLoop()
    {
        _threadId = GetCurrentThreadId();
        _startedEvent.Set();

        RegisterAllSlots();

        Console.Error.WriteLine($"[GlobalHotkeyManager] Entering Win32 Message Loop (Thread ID: {_threadId}). Waiting for WM_HOTKEY...");

        while (!_disposed && GetMessage(out MSG msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (msg.message == WM_HOTKEY)
            {
                int id = (int)msg.wParam;
                HotkeyDefinition? target = null;

                lock (_lock)
                {
                    _registeredHotkeys.TryGetValue(id, out target);
                }

                if (target != null)
                {
                    Console.Error.WriteLine($"[GlobalHotkeyManager] >>> WM_HOTKEY TRIGGERED at {DateTime.Now:HH:mm:ss.fff}: ID={id} -> Slot {target.Slot} ({target.DisplayName ?? target.ProfileDirectory}) Shortcut='{target.Shortcut}'");
                    OnHotkeyFired(target);
                }
                else
                {
                    Console.Error.WriteLine($"[GlobalHotkeyManager] WM_HOTKEY received for unknown ID={id}");
                }
            }
            else if (msg.message == WM_USER_REFRESH)
            {
                Console.Error.WriteLine("[GlobalHotkeyManager] WM_USER_REFRESH received. Re-registering all slots...");
                RegisterAllSlots();
            }

            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        Console.Error.WriteLine("[GlobalHotkeyManager] Message loop exited. Unregistering all hotkeys...");
        UnregisterAll();
    }

    private void RegisterAllSlots()
    {
        lock (_lock)
        {
            UnregisterAll();

            var slots = _slotManager.GetAllSlots();
            Console.Error.WriteLine($"[GlobalHotkeyManager] Registering hotkeys for {slots.Count} configured slots...");

            foreach (var slot in slots)
            {
                if (string.IsNullOrWhiteSpace(slot.Shortcut) || string.IsNullOrWhiteSpace(slot.ProfileDirectory))
                {
                    Console.Error.WriteLine($"  Slot {slot.Slot:D2}: Skipped (No shortcut configured or directory empty)");
                    continue;
                }

                if (HotKeyHelper.TryParseShortcut(slot.Shortcut, out uint mods, out uint vk))
                {
                    // Stable unique hotkey ID per slot: 1000 + slot number
                    int id = 1000 + slot.Slot;

                    bool success = HotKeyHelper.RegisterHotKey(IntPtr.Zero, id, mods, vk);
                    int err = success ? 0 : Marshal.GetLastWin32Error();

                    char keyChar = (vk >= 0x30 && vk <= 0x39) ? (char)vk : (char)('?');

                    Console.Error.WriteLine($"  Slot {slot.Slot:D2} (ID={id}) -> Shortcut: '{slot.Shortcut}' (VK=0x{vk:X2} '{keyChar}', Mods=0x{mods:X4}) -> RegisterHotKey: {(success ? "SUCCESS" : "FAILED (Error: " + err + ")")}");

                    var def = new HotkeyDefinition
                    {
                        Id = id,
                        Slot = slot.Slot,
                        ProfileDirectory = slot.ProfileDirectory,
                        DisplayName = slot.DisplayName,
                        Shortcut = slot.Shortcut,
                        Modifiers = mods,
                        Vk = vk,
                        IsRegistered = success,
                        RegistrationError = success ? null : (err == 1409 ? "Already in use" : $"Error {err}")
                    };

                    _registeredHotkeys[id] = def;
                }
                else
                {
                    Console.Error.WriteLine($"  Slot {slot.Slot:D2}: Failed to parse shortcut string '{slot.Shortcut}'");
                }
            }

            int registeredCount = _registeredHotkeys.Values.Count(h => h.IsRegistered);
            Console.Error.WriteLine($"[GlobalHotkeyManager] Total Active Global Hotkeys: {registeredCount} / {_registeredHotkeys.Count}");
        }
    }

    private void UnregisterAll()
    {
        foreach (var id in _registeredHotkeys.Keys)
        {
            HotKeyHelper.UnregisterHotKey(IntPtr.Zero, id);
        }
        _registeredHotkeys.Clear();
    }

    private void OnHotkeyFired(HotkeyDefinition hotkey)
    {
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                Console.Error.WriteLine($"[GlobalHotkeyManager] Initiating profile switch for Slot {hotkey.Slot} ('{hotkey.ProfileDirectory}')...");

                var result = NativeMessageHost.HandleRequest(new NativeMessageRequest
                {
                    Action = "switch-profile",
                    Slot = hotkey.Slot,
                    ProfileDirectory = hotkey.ProfileDirectory,
                    CopyTabs = false
                }, _detector, _slotManager);

                Console.Error.WriteLine($"[GlobalHotkeyManager] Switch Finished: Success={result.Success}, Profile='{result.Profile}', HWND=0x{result.WindowHandle:X8}, Error='{result.Error}'");

                if (HotkeyTriggered != null)
                {
                    HotkeyTriggered.Invoke(hotkey);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[GlobalHotkeyManager] Error executing switch: {ex.Message}");
            }
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_threadId != 0)
        {
            PostThreadMessage(_threadId, WM_QUIT, UIntPtr.Zero, IntPtr.Zero);
        }

        if (_daemonMutex != null)
        {
            try { _daemonMutex.ReleaseMutex(); } catch { }
            _daemonMutex.Dispose();
        }

        _startedEvent.Dispose();
    }
}
