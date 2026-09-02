using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using ChromeAccountSwitcher.Helper.Chrome;
using ChromeAccountSwitcher.Helper.NativeMessaging;

namespace ChromeAccountSwitcher.Helper.Windows;

public class HotKeyManager : IDisposable
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

    [DllImport("user32.dll")]
    private static extern sbyte GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage([In] ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage([In] ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint idThread, uint Msg, UIntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    private Thread? _thread;
    private uint _threadId;
    private readonly ManualResetEventSlim _readyEvent = new();
    private readonly object _lock = new();
    private readonly Dictionary<int, (uint Modifiers, uint Vk, string Directory, int Slot)> _registeredHotkeys = new();
    private readonly ChromeWindowDetector _detector;
    private readonly SlotConfigManager _slotManager;
    private bool _disposed;

    public HotKeyManager(ChromeWindowDetector detector, SlotConfigManager slotManager)
    {
        _detector = detector;
        _slotManager = slotManager;
    }

    public void Start()
    {
        if (_thread != null) return;

        _thread = new Thread(RunMessagePump)
        {
            IsBackground = true,
            Name = "Win32GlobalHotKeyThread"
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();

        _readyEvent.Wait(2000);
    }

    public void Refresh()
    {
        if (_threadId != 0)
        {
            PostThreadMessage(_threadId, WM_USER_REFRESH, UIntPtr.Zero, IntPtr.Zero);
        }
    }

    private void RunMessagePump()
    {
        _threadId = GetCurrentThreadId();
        _readyEvent.Set();

        RegisterConfiguredHotkeys();

        while (!_disposed && GetMessage(out MSG msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (msg.message == WM_HOTKEY)
            {
                int id = (int)msg.wParam;
                (uint Modifiers, uint Vk, string Directory, int Slot) target;
                bool found = false;

                lock (_lock)
                {
                    found = _registeredHotkeys.TryGetValue(id, out target);
                }

                if (found)
                {
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        try
                        {
                            NativeMessageHost.HandleRequest(new NativeMessageRequest
                            {
                                Action = "switch-profile",
                                Slot = target.Slot,
                                ProfileDirectory = target.Directory,
                                CopyTabs = false
                            }, _detector, _slotManager);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[HotKeyManager] Error switching: {ex.Message}");
                        }
                    });
                }
            }
            else if (msg.message == WM_USER_REFRESH)
            {
                RegisterConfiguredHotkeys();
            }

            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        UnregisterAllHotkeys();
    }

    private void RegisterConfiguredHotkeys()
    {
        lock (_lock)
        {
            UnregisterAllHotkeys();

            var slots = _slotManager.GetAllSlots();
            int id = 100;

            foreach (var slot in slots)
            {
                if (string.IsNullOrWhiteSpace(slot.Shortcut) || string.IsNullOrWhiteSpace(slot.ProfileDirectory))
                    continue;

                if (HotKeyHelper.TryParseShortcut(slot.Shortcut, out uint mods, out uint vk))
                {
                    bool success = HotKeyHelper.RegisterHotKey(IntPtr.Zero, id, mods, vk);
                    if (success)
                    {
                        _registeredHotkeys[id] = (mods, vk, slot.ProfileDirectory, slot.Slot);
                        id++;
                    }
                }
            }
        }
    }

    private void UnregisterAllHotkeys()
    {
        foreach (var id in _registeredHotkeys.Keys)
        {
            HotKeyHelper.UnregisterHotKey(IntPtr.Zero, id);
        }
        _registeredHotkeys.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_threadId != 0)
        {
            PostThreadMessage(_threadId, WM_QUIT, UIntPtr.Zero, IntPtr.Zero);
        }

        _readyEvent.Dispose();
    }
}
