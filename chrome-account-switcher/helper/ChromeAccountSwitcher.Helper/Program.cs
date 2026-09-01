using System;
using System.Collections.Generic;
using System.Linq;
using ChromeAccountSwitcher.Helper.Chrome;
using ChromeAccountSwitcher.Helper.NativeMessaging;
using ChromeAccountSwitcher.Helper.Windows;

namespace ChromeAccountSwitcher.Helper;

internal class Program
{
    private static void Main(string[] args)
    {
        var detector = new ChromeWindowDetector();
        var slotManager = new SlotConfigManager(detector);

        // Check if invoked as Chrome Native Messaging Host
        // Chrome passes the extension origin as the first argument, e.g. "chrome-extension://<id>/"
        bool isNativeMessagingMode = args.Any(a => a.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase) ||
                                                  a.Equals("--native-messaging", StringComparison.OrdinalIgnoreCase))
                                     || (!Console.IsInputRedirected && false)
                                     || (args.Length > 0 && args[0].StartsWith("chrome-extension", StringComparison.OrdinalIgnoreCase));

        if (isNativeMessagingMode)
        {
            NativeMessageHost.Run(detector, slotManager);
            return;
        }

        // Otherwise run in diagnostic / CLI mode
        RunDiagnosticCli(args, detector, slotManager);
    }

    private static void RunDiagnosticCli(string[] args, ChromeWindowDetector detector, SlotConfigManager slotManager)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("========================================");
        Console.WriteLine(" Chrome Account Switcher - Native Helper");
        Console.WriteLine("========================================");
        Console.WriteLine();

        // 1. Slot Configuration
        Console.WriteLine("Configured Profile Slots:");
        var slots = slotManager.GetAllSlots();
        foreach (var s in slots)
        {
            Console.WriteLine($"  Slot {s.Slot} (Ctrl+{s.Slot}) -> Directory: '{s.ProfileDirectory}', Name: '{s.DisplayName}'");
        }
        Console.WriteLine();

        // 2. Active Window Mapping
        Console.WriteLine("Scanning Active Chrome Windows...");
        var detectedWindows = detector.DetectChromeWindows(out int totalScanned);
        var browserWindows = detectedWindows.Where(w => w.IsNormalBrowserWindow).ToList();

        if (browserWindows.Count == 0)
        {
            Console.WriteLine("  No active Chrome browser windows detected.");
            Console.WriteLine();
        }
        else
        {
            var grouped = browserWindows.GroupBy(w => w.ProfileDirectory ?? "UNKNOWN").ToList();
            for (int g = 0; g < grouped.Count; g++)
            {
                var group = grouped[g];
                var sampleWin = group.First();

                if (group.Key == "UNKNOWN")
                {
                    Console.WriteLine("Profile: UNKNOWN");
                    Console.WriteLine("Name:    (Unidentified Profile)");
                }
                else
                {
                    Console.WriteLine($"Profile: {sampleWin.ProfileDirectory}");
                    Console.WriteLine($"Name:    {sampleWin.ProfileDisplayName ?? sampleWin.ProfileDirectory}");
                    Console.WriteLine($"Email:   {sampleWin.ProfileEmail ?? "(No email associated)"}");
                }
                Console.WriteLine();

                foreach (var win in group)
                {
                    Console.WriteLine($"  HWND:     0x{win.Hwnd.ToInt64():X8} ({win.Hwnd})");
                    Console.WriteLine($"  PID:      {win.ProcessId}");
                    Console.WriteLine($"  Title:    {win.Title}");
                    Console.WriteLine($"  State:    {win.WindowState}");
                    Console.WriteLine($"  Focused:  {(win.IsFocused ? "YES" : "NO")}");
                    Console.WriteLine($"  Visible:  {(win.IsVisible ? "YES" : "NO")}");
                    Console.WriteLine($"  Source:   {win.MappingSource}");
                    Console.WriteLine();
                }

                if (g < grouped.Count - 1)
                {
                    Console.WriteLine("----------------------------------------");
                }
            }
        }

        Console.WriteLine("========================================");
        Console.WriteLine(" Commands & Testing");
        Console.WriteLine("========================================");

        // Command handling
        if (args.Length >= 2 && args[0].Equals("--switch-slot", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(args[1], out int slotNum))
            {
                var tabs = new List<TabItemDto>();
                for (int i = 2; i < args.Length; i++)
                {
                    if (args[i].StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                        args[i].StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        tabs.Add(new TabItemDto { Url = args[i], Title = args[i], Active = (tabs.Count == 0) });
                    }
                }

                Console.WriteLine($"Testing Slot {slotNum} Switch (Tabs to copy: {tabs.Count})...");
                var res = NativeMessageHost.HandleRequest(new NativeMessageRequest
                {
                    Action = "switch-profile",
                    Slot = slotNum,
                    CopyTabs = tabs.Count > 0,
                    Tabs = tabs
                }, detector, slotManager);

                Console.WriteLine($"Result: {(res.Success ? "SUCCESS" : "FAILED")}");
                if (res.Success)
                {
                    Console.WriteLine($"Focused Profile '{res.Profile}' (HWND: 0x{(res.WindowHandle ?? 0):X8}).");
                    Console.WriteLine($"Source: '{res.SourceProfile}', Target: '{res.TargetProfile}', Copied: {res.TabsCopied}, Skipped: {res.TabsSkipped}");
                }
                else
                {
                    Console.WriteLine($"Error: {res.Error}");
                }
            }
        }
        else if (args.Length >= 2 && args[0].Equals("--focus-hwnd", StringComparison.OrdinalIgnoreCase))
        {
            if (long.TryParse(args[1], out long rawHwnd) || (args[1].StartsWith("0x", StringComparison.OrdinalIgnoreCase) && long.TryParse(args[1][2..], System.Globalization.NumberStyles.HexNumber, null, out rawHwnd)))
            {
                IntPtr targetHwnd = new IntPtr(rawHwnd);
                Console.WriteLine($"Bringing window HWND 0x{targetHwnd.ToInt64():X8} to foreground...");
                bool success = WindowManager.FocusWindow(targetHwnd);
                Console.WriteLine($"Focus result: {(success ? "SUCCESS" : "FAILED")}");
            }
        }
        else
        {
            Console.WriteLine("Usage options:");
            Console.WriteLine("  --switch-slot <1-5> [url1 url2 ...]   Test switching to a configured slot (optionally copying URLs)");
            Console.WriteLine("  --focus-hwnd <HWND>                  Focus a specific window handle");
            Console.WriteLine("  --native-messaging                   Start in Chrome Native Messaging host mode");
        }
        Console.WriteLine();
    }
}
