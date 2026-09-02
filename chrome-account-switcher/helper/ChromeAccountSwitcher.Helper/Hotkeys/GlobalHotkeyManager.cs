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
    private bool _disposed;

    public event Action<HotkeyDefinition>? HotkeyTriggered;

    public GlobalHotkeyManager(ChromeWindowDetector detector, SlotConfigManager slotManager)
    {
        _detector = detector;
        _slotManager = slotManager;
    }

    public void Start()
    {
        if (_messageLoopThread != null) return;

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
                    OnHotkeyFired(target);
                }
            }
            else if (msg.message == WM_USER_REFRESH)
            {
                RegisterAllSlots();
            }

            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        UnregisterAll();
    }

    private void RegisterAllSlots()
    {
        lock (_lock)
        {
            UnregisterAll();

            var slots = _slotManager.GetAllSlots();
            int currentId = 100;

            foreach (var slot in slots)
            {
                if (string.IsNullOrWhiteSpace(slot.Shortcut) || string.IsNullOrWhiteSpace(slot.ProfileDirectory))
                    continue;

                if (HotKeyHelper.TryParseShortcut(slot.Shortcut, out uint mods, out uint vk))
                {
                    bool success = HotKeyHelper.RegisterHotKey(IntPtr.Zero, currentId, mods, vk);
                    int err = success ? 0 : Marshal.GetLastWin32Error();

                    var def = new HotkeyDefinition
                    {
                        Id = currentId,
                        Slot = slot.Slot,
                        ProfileDirectory = slot.ProfileDirectory,
                        DisplayName = slot.DisplayName,
                        Shortcut = slot.Shortcut,
                        Modifiers = mods,
                        Vk = vk,
                        IsRegistered = success,
                        RegistrationError = success ? null : (err == 1409 ? "Already in use" : $"Error {err}")
                    };

                    _registeredHotkeys[currentId] = def;
                    currentId++;
                }
            }
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
                if (HotkeyTriggered != null)
                {
                    HotkeyTriggered.Invoke(hotkey);
                }
                else
                {
                    // Default fallback switch
                    NativeMessageHost.HandleRequest(new NativeMessageRequest
                    {
                        Action = "switch-profile",
                        Slot = hotkey.Slot,
                        ProfileDirectory = hotkey.ProfileDirectory,
                        CopyTabs = false
                    }, _detector, _slotManager);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GlobalHotkeyManager] Hotkey action error: {ex.Message}");
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

        _startedEvent.Dispose();
    }
}
