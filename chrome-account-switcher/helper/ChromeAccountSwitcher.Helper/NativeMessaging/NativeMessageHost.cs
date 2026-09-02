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
            var discovered = detector.RefreshProfiles(request.SourceProfile);
            var currentProfile = discovered.FirstOrDefault(p => p.IsCurrent)?.DirectoryName;
            var profileDtos = discovered.Select(p => new ChromeProfileDto
            {
                Directory = p.DirectoryName,
                DisplayName = p.DisplayName,
                GaiaName = p.GaiaName,
                Email = p.Email,
                AvatarIcon = p.AvatarIcon,
                OrderIndex = p.OrderIndex,
                IsCurrent = p.IsCurrent
            }).ToList();

            return new NativeMessageResponse
            {
                Success = true,
                Message = "Chrome Account Switcher Native Host is online.",
                Profiles = profileDtos,
                CurrentProfile = currentProfile,
                Slots = slotManager.GetAllSlots()
            };
        }

        if (request.Action.Equals("getProfiles", StringComparison.OrdinalIgnoreCase) ||
            request.Action.Equals("get-profiles", StringComparison.OrdinalIgnoreCase))
        {
            var discovered = detector.RefreshProfiles(request.SourceProfile);
            var currentProfile = discovered.FirstOrDefault(p => p.IsCurrent)?.DirectoryName;
            var profileDtos = discovered.Select(p => new ChromeProfileDto
            {
                Directory = p.DirectoryName,
                DisplayName = p.DisplayName,
                GaiaName = p.GaiaName,
                Email = p.Email,
                AvatarIcon = p.AvatarIcon,
                OrderIndex = p.OrderIndex,
                IsCurrent = p.IsCurrent
            }).ToList();

            return new NativeMessageResponse
            {
                Success = true,
                Profiles = profileDtos,
                CurrentProfile = currentProfile,
                Slots = slotManager.GetAllSlots()
            };
        }

        if (request.Action.Equals("get-slots", StringComparison.OrdinalIgnoreCase))
        {
            return new NativeMessageResponse
            {
                Success = true,
                Slots = slotManager.GetAllSlots()
            };
        }

        if (request.Action.Equals("set-shortcut", StringComparison.OrdinalIgnoreCase))
        {
            if (request.Slot.HasValue && request.Slot.Value >= 1 && request.Slot.Value <= 50)
            {
                slotManager.SetSlotShortcut(request.Slot.Value, request.Shortcut);
                return new NativeMessageResponse
                {
                    Success = true,
                    Message = $"Shortcut for Slot {request.Slot.Value} updated.",
                    Slots = slotManager.GetAllSlots()
                };
            }

            return new NativeMessageResponse
            {
                Success = false,
                Error = "Invalid slot number provided for set-shortcut."
            };
        }

        if (request.Action.Equals("sync-slots", StringComparison.OrdinalIgnoreCase))
        {
            if (request.Slots != null && request.Slots.Count > 0)
            {
                slotManager.SyncSlots(request.Slots);
            }

            return new NativeMessageResponse
            {
                Success = true,
                Message = "Slots synchronized successfully.",
                Slots = slotManager.GetAllSlots()
            };
        }

        if (request.Action.Equals("switch-profile", StringComparison.OrdinalIgnoreCase))
        {
            string? targetDirectory = null;
            string? targetDisplayName = null;

            if (!string.IsNullOrWhiteSpace(request.ProfileDirectory))
            {
                targetDirectory = request.ProfileDirectory;
                var known = detector.GetProfileByDirectory(targetDirectory);
                targetDisplayName = known?.DisplayName;
            }
            else if (request.Slot.HasValue)
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
            else
            {
                return new NativeMessageResponse
                {
                    Success = false,
                    Error = "Missing slot or profileDirectory in switch request."
                };
            }

            // Validate targetDirectory against directory traversal / invalid characters
            if (targetDirectory.Contains('/') || targetDirectory.Contains('\\') || targetDirectory.Contains("..") ||
                Path.GetInvalidFileNameChars().Any(c => targetDirectory.Contains(c)))
            {
                return new NativeMessageResponse
                {
                    Success = false,
                    Error = "Invalid profile directory name."
                };
            }

            // 1. Identify source profile
            string? sourceProfile = request.SourceProfile;
            var detectedWindows = detector.DetectChromeWindows(out _);
            var browserWindows = detectedWindows.Where(w => w.IsNormalBrowserWindow).ToList();

            if (string.IsNullOrEmpty(sourceProfile))
            {
                var currentFocused = browserWindows.FirstOrDefault(w => w.IsFocused);
                sourceProfile = currentFocused?.ProfileDisplayName ?? currentFocused?.ProfileDirectory ?? "Unknown";
            }

            // 2. Filter and sanitize tabs to copy
            var validUrls = new List<string>();
            int tabsSkipped = 0;

            if (request.CopyTabs == true && request.Tabs != null)
            {
                foreach (var tab in request.Tabs)
                {
                    if (ChromeLauncher.IsValidHttpUrl(tab.Url, out var safeUrl))
                    {
                        validUrls.Add(safeUrl);
                    }
                    else
                    {
                        tabsSkipped++;
                    }
                }
            }

            // 3. Find windows belonging to target directory
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

            bool isTargetRunning = matchingWindows.Count > 0;
            IntPtr targetHwnd = IntPtr.Zero;
            string? resolvedDisplayName = targetDisplayName;

            if (isTargetRunning)
            {
                // Target profile is ALREADY RUNNING:
                // If tabs to copy, open them in the running profile
                if (validUrls.Count > 0)
                {
                    ChromeLauncher.OpenUrlsInProfile(targetDirectory, validUrls);
                    System.Threading.Thread.Sleep(150); // Allow Chrome singleton IPC to receive tabs
                }

                // Deterministic Window Selection:
                // 1. Prefer focused window if already active
                // 2. Prefer visible, non-minimized normal browser window
                // 3. Fallback to any matching window
                var targetWindow = matchingWindows.FirstOrDefault(w => w.IsFocused)
                                   ?? matchingWindows.FirstOrDefault(w => w.IsVisible && !w.IsMinimized)
                                   ?? matchingWindows.First();

                targetHwnd = targetWindow.Hwnd;
                resolvedDisplayName = resolvedDisplayName ?? targetWindow.ProfileDisplayName ?? targetDirectory;
                WindowManager.FocusWindow(targetHwnd);
            }
            else
            {
                // Target profile is NOT RUNNING:
                // Launch Chrome for target profile with the copied URLs (or empty if none)
                bool launched = ChromeLauncher.OpenUrlsInProfile(targetDirectory, validUrls);
                if (!launched)
                {
                    return new NativeMessageResponse
                    {
                        Success = false,
                        Profile = targetDirectory,
                        DisplayName = targetDisplayName ?? targetDirectory,
                        SourceProfile = sourceProfile,
                        TargetProfile = targetDisplayName ?? targetDirectory,
                        Error = $"Failed to launch Chrome for profile '{targetDisplayName ?? targetDirectory}'."
                    };
                }

                // Wait and poll for target window to initialize (up to 3 seconds)
                for (int attempt = 0; attempt < 12; attempt++)
                {
                    System.Threading.Thread.Sleep(250);
                    var reDetected = detector.DetectChromeWindows(out _);
                    var targetWin = reDetected.FirstOrDefault(w =>
                        w.IsNormalBrowserWindow &&
                        ((w.ProfileDirectory != null && w.ProfileDirectory.Equals(targetDirectory, StringComparison.OrdinalIgnoreCase)) ||
                         (!string.IsNullOrEmpty(targetDisplayName) && w.Title.IndexOf(targetDisplayName, StringComparison.OrdinalIgnoreCase) >= 0)));

                    if (targetWin != null)
                    {
                        targetHwnd = targetWin.Hwnd;
                        resolvedDisplayName = resolvedDisplayName ?? targetWin.ProfileDisplayName ?? targetDirectory;
                        WindowManager.FocusWindow(targetHwnd);
                        break;
                    }
                }
            }

            return new NativeMessageResponse
            {
                Success = true,
                Profile = targetDirectory,
                DisplayName = resolvedDisplayName ?? targetDirectory,
                SourceProfile = sourceProfile,
                TargetProfile = resolvedDisplayName ?? targetDirectory,
                TabsCopied = validUrls.Count,
                TabsSkipped = tabsSkipped,
                WindowHandle = targetHwnd != IntPtr.Zero ? targetHwnd.ToInt64() : null
            };
        }

        return new NativeMessageResponse
        {
            Success = false,
            Error = $"Unknown action: '{request.Action}'"
        };
    }
}
