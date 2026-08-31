using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using ChromeAccountSwitcher.Helper.Chrome;
using ChromeAccountSwitcher.Helper.Windows;

namespace ChromeAccountSwitcher.Helper.NativeMessaging;

public static class NativeMessageHost
{
    public static void Run(ChromeWindowDetector detector, SlotConfigManager slotManager)
    {
        using Stream inStream = Console.OpenStandardInput();
        using Stream outStream = Console.OpenStandardOutput();

        while (true)
        {
            byte[] lenBytes = new byte[4];
            int read = inStream.Read(lenBytes, 0, 4);
            if (read < 4)
            {
                // Chrome closed the stdio pipe
                break;
            }

            int messageLength = BitConverter.ToInt32(lenBytes, 0);
            if (messageLength <= 0 || messageLength > 1024 * 1024)
            {
                continue;
            }

            byte[] msgBytes = new byte[messageLength];
            int totalRead = 0;
            while (totalRead < messageLength)
            {
                int chunk = inStream.Read(msgBytes, totalRead, messageLength - totalRead);
                if (chunk <= 0) break;
                totalRead += chunk;
            }

            NativeMessageResponse response;
            try
            {
                string json = Encoding.UTF8.GetString(msgBytes, 0, totalRead);
                var request = JsonSerializer.Deserialize<NativeMessageRequest>(json);
                response = HandleRequest(request, detector, slotManager);
            }
            catch (Exception ex)
            {
                response = new NativeMessageResponse
                {
                    Success = false,
                    Error = $"Failed to parse or process native message: {ex.Message}"
                };
            }

            byte[] respJsonBytes = JsonSerializer.SerializeToUtf8Bytes(response);
            byte[] respLenBytes = BitConverter.GetBytes(respJsonBytes.Length);

            outStream.Write(respLenBytes, 0, 4);
            outStream.Write(respJsonBytes, 0, respJsonBytes.Length);
            outStream.Flush();
        }
    }

    public static NativeMessageResponse HandleRequest(
        NativeMessageRequest? request,
        ChromeWindowDetector detector,
        SlotConfigManager slotManager)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Action))
        {
            return new NativeMessageResponse
            {
                Success = false,
                Error = "Invalid native message: missing action."
            };
        }

        if (request.Action.Equals("ping", StringComparison.OrdinalIgnoreCase))
        {
            return new NativeMessageResponse
            {
                Success = true,
                Message = "Chrome Account Switcher Native Host is online."
            };
        }

        if (request.Action.Equals("switch-profile", StringComparison.OrdinalIgnoreCase))
        {
            string? targetDirectory = null;
            string? targetDisplayName = null;

            if (request.Slot.HasValue)
            {
                var slotEntry = slotManager.GetSlot(request.Slot.Value);
                if (slotEntry == null || string.IsNullOrWhiteSpace(slotEntry.ProfileDirectory))
                {
                    return new NativeMessageResponse
                    {
                        Success = false,
                        Error = $"Slot {request.Slot.Value} is not configured."
                    };
                }
                targetDirectory = slotEntry.ProfileDirectory;
                targetDisplayName = slotEntry.DisplayName;
            }
            else if (!string.IsNullOrWhiteSpace(request.ProfileDirectory))
            {
                targetDirectory = request.ProfileDirectory;
            }
            else
            {
                return new NativeMessageResponse
                {
                    Success = false,
                    Error = "Missing slot or profileDirectory in switch request."
                };
            }

            // Scan currently running Chrome windows
            var detectedWindows = detector.DetectChromeWindows(out _);
            var browserWindows = detectedWindows.Where(w => w.IsNormalBrowserWindow).ToList();

            // Find windows belonging to target directory
            var matchingWindows = browserWindows
                .Where(w => w.ProfileDirectory != null &&
                            w.ProfileDirectory.Equals(targetDirectory, StringComparison.OrdinalIgnoreCase))
                .ToList();

            // Fallback: If no direct directory match, check if title matches target directory or display name
            if (matchingWindows.Count == 0 && !string.IsNullOrWhiteSpace(targetDisplayName))
            {
                matchingWindows = browserWindows
                    .Where(w => w.Title.IndexOf(targetDisplayName, StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();
            }

            if (matchingWindows.Count == 0)
            {
                return new NativeMessageResponse
                {
                    Success = false,
                    Error = $"Target profile '{targetDisplayName ?? targetDirectory}' is not currently running."
                };
            }

            // Deterministic Window Selection (Rule 5):
            // 1. Prefer focused window if already active
            // 2. Prefer visible, non-minimized normal browser window
            // 3. Fallback to any matching window
            var targetWindow = matchingWindows.FirstOrDefault(w => w.IsFocused)
                               ?? matchingWindows.FirstOrDefault(w => w.IsVisible && !w.IsMinimized)
                               ?? matchingWindows.First();

            bool focused = WindowManager.FocusWindow(targetWindow.Hwnd);
            if (!focused)
            {
                return new NativeMessageResponse
                {
                    Success = false,
                    Profile = targetDirectory,
                    DisplayName = targetDisplayName ?? targetWindow.ProfileDisplayName,
                    WindowHandle = targetWindow.Hwnd.ToInt64(),
                    Error = $"Failed to bring window (HWND: 0x{targetWindow.Hwnd.ToInt64():X8}) to foreground."
                };
            }

            return new NativeMessageResponse
            {
                Success = true,
                Profile = targetDirectory,
                DisplayName = targetDisplayName ?? targetWindow.ProfileDisplayName ?? targetDirectory,
                WindowHandle = targetWindow.Hwnd.ToInt64()
            };
        }

        return new NativeMessageResponse
        {
            Success = false,
            Error = $"Unknown action: '{request.Action}'"
        };
    }
}
