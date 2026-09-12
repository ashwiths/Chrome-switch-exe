using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using ChromeAccountSwitcher.Helper.Chrome;
using ChromeAccountSwitcher.Helper.Hotkeys;
using ChromeAccountSwitcher.Helper.Windows;

namespace ChromeAccountSwitcher.Helper.NativeMessaging;

public static class NativeMessageHost
{
    private static readonly string LogFile = Path.Combine(Path.GetTempPath(), "chrome_switcher_helper.log");

    private static void Log(string msg)
    {
        try { File.AppendAllText(LogFile, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\r\n"); } catch { }
    }

    public static IntPtr CallingParentHwnd { get; set; } = IntPtr.Zero;

    public static void Run(ChromeWindowDetector detector, SlotConfigManager slotManager)
    {
        int myPid = Environment.ProcessId;
        int pPid = ProcessHelper.GetParentProcessId(myPid);
        string? pCmd = pPid > 0 ? ProcessHelper.GetProcessCommandLine(pPid) : null;
        Log($"NativeMessageHost.Run started: PID={myPid}, ParentPID={pPid}, ParentCmd='{pCmd}'");

        // Parse --parent-window from command-line arguments or parent cmd
        foreach (var arg in Environment.GetCommandLineArgs())
        {
            if (arg.StartsWith("--parent-window=", StringComparison.OrdinalIgnoreCase))
            {
                if (long.TryParse(arg.Substring("--parent-window=".Length), out long raw))
                {
                    CallingParentHwnd = new IntPtr(raw);
                    break;
                }
            }
        }
        if (CallingParentHwnd == IntPtr.Zero && !string.IsNullOrEmpty(pCmd))
        {
            var m = System.Text.RegularExpressions.Regex.Match(pCmd, @"--parent-window=(\d+)");
            if (m.Success && long.TryParse(m.Groups[1].Value, out long raw))
            {
                CallingParentHwnd = new IntPtr(raw);
            }
        }
        if (CallingParentHwnd != IntPtr.Zero)
        {
            Log($"Captured CallingParentHwnd: 0x{CallingParentHwnd.ToInt64():X8} ({CallingParentHwnd})");
        }

        EnsureDaemonRunning();

        using (Stream inStream = Console.OpenStandardInput())
        using (Stream outStream = Console.OpenStandardOutput())

        while (true)
        {
            byte[] lenBytes = new byte[4];
            Log("Waiting for 4-byte message length...");
            if (!ReadExact(inStream, lenBytes, 0, 4))
            {
                Log("inStream closed or EOF received. Exiting loop.");
                break;
            }

            // Detect and skip optional UTF-8 BOM preamble (0xEF, 0xBB, 0xBF)
            if (lenBytes[0] == 0xEF && lenBytes[1] == 0xBB && lenBytes[2] == 0xBF)
            {
                Log("Detected UTF-8 BOM preamble. Synchronizing stream...");
                lenBytes[0] = lenBytes[3];
                if (!ReadExact(inStream, lenBytes, 1, 3))
                {
                    Log("Failed to read remaining length bytes after BOM.");
                    break;
                }
            }

            int messageLength = BitConverter.ToInt32(lenBytes, 0);
            Log($"Received message length: {messageLength} bytes");

            if (messageLength <= 0 || messageLength > 1024 * 1024)
            {
                Log($"Invalid message length: {messageLength}. Continuing...");
                continue;
            }

            byte[] msgBytes = new byte[messageLength];
            if (!ReadExact(inStream, msgBytes, 0, messageLength))
            {
                Log("Failed to read complete message body. Exiting loop.");
                break;
            }

            string json = Encoding.UTF8.GetString(msgBytes, 0, messageLength);
            Log($"Request JSON: {json}");

            NativeMessageResponse response;
            try
            {
                var request = JsonSerializer.Deserialize<NativeMessageRequest>(json);
                response = HandleRequest(request, detector, slotManager);
                Log($"HandleRequest finished: Success={response.Success}");
            }
            catch (Exception ex)
            {
                Log($"Exception in HandleRequest: {ex}");
                response = new NativeMessageResponse
                {
                    Success = false,
                    Error = $"Failed to parse or process native message: {ex.Message}"
                };
            }

            byte[] respJsonBytes = JsonSerializer.SerializeToUtf8Bytes(response);
            byte[] respLenBytes = BitConverter.GetBytes(respJsonBytes.Length);

            Log($"Writing response: {respJsonBytes.Length} bytes");
            outStream.Write(respLenBytes, 0, 4);
            outStream.Write(respJsonBytes, 0, respJsonBytes.Length);
            outStream.Flush();
            Log("Response flushed to outStream successfully.");
        }
        Log("NativeMessageHost.Run finished.");
    }

    private static bool ReadExact(Stream stream, byte[] buffer, int offset, int count)
    {
        int readTotal = 0;
        while (readTotal < count)
        {
            int read = stream.Read(buffer, offset + readTotal, count - readTotal);
            if (read <= 0) return false;
            readTotal += read;
        }
        return true;
    }

    public static void EnsureDaemonRunning()
    {
        try
        {
            bool isDaemonRunning = false;
            try
            {
                using var testMutex = Mutex.OpenExisting(GlobalKeyboardHook.DaemonMutexName);
                isDaemonRunning = true;
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                isDaemonRunning = false;
            }
            catch (Exception ex)
            {
                Log($"Mutex probe exception: {ex.Message}");
            }

            if (!isDaemonRunning)
            {
                string exePath = Environment.ProcessPath ??
                    Path.Combine(AppContext.BaseDirectory, "ChromeAccountSwitcher.Helper.exe");

                if (File.Exists(exePath))
                {
                    string startupDir = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                    string vbsPath = Path.Combine(startupDir, "ChromeAccountSwitcherDaemon.vbs");
                    if (File.Exists(vbsPath))
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = "wscript.exe",
                            Arguments = $"\"{vbsPath}\"",
                            UseShellExecute = true,
                            WindowStyle = ProcessWindowStyle.Hidden
                        };
                        Process.Start(psi);
                        Log("Started detached background hotkey daemon via wscript VBS.");
                    }
                    else
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = exePath,
                            Arguments = "--listen-hotkeys",
                            UseShellExecute = true,
                            WindowStyle = ProcessWindowStyle.Hidden,
                            CreateNoWindow = true
                        };
                        Process.Start(psi);
                        Log("Started detached background hotkey daemon directly.");
                    }
                }
            }
            else
            {
                Log("Master Daemon is already active (Mutex validated).");
            }
        }
        catch (Exception ex)
        {
            Log($"EnsureDaemonRunning failed: {ex.Message}");
        }
    }

    public static void NotifyDaemonToReloadHotkeys()
    {
        try
        {
            if (EventWaitHandle.TryOpenExisting(GlobalKeyboardHook.ReloadEventName, out var handle))
            {
                using (handle)
                {
                    handle.Set();
                }
            }
        }
        catch { }
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
            var discovered = detector.RefreshProfiles(request.SourceProfile, request.SourceEmail, request.ProbeToken, request.Tabs);
            var currentProfile = discovered.FirstOrDefault(p => p.IsCurrent)?.DirectoryName;
            if (CallingParentHwnd != IntPtr.Zero && !string.IsNullOrWhiteSpace(currentProfile))
            {
                ProfileWindowCache.RecordWindow(currentProfile, CallingParentHwnd);
            }
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
            var discovered = detector.RefreshProfiles(request.SourceProfile, request.SourceEmail, request.ProbeToken, request.Tabs);
            var currentProfile = discovered.FirstOrDefault(p => p.IsCurrent)?.DirectoryName;
            if (CallingParentHwnd != IntPtr.Zero && !string.IsNullOrWhiteSpace(currentProfile))
            {
                ProfileWindowCache.RecordWindow(currentProfile, CallingParentHwnd);
            }
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

        if (request.Action.Equals("validateShortcut", StringComparison.OrdinalIgnoreCase))
        {
            string sc = request.Shortcut ?? string.Empty;
            if (!HotKeyHelper.TryParseShortcut(sc, out uint mods, out uint vk))
            {
                return new NativeMessageResponse
                {
                    Success = false,
                    Error = "Invalid shortcut format or missing modifier.",
                    Shortcut = sc
                };
            }

            int testId = 9999;
            bool ok = HotKeyHelper.RegisterHotKey(IntPtr.Zero, testId, mods, vk);
            if (!ok)
            {
                int err = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                string errMsg = err == 1409
                    ? "Shortcut is already in use by Windows or another application."
                    : $"Windows rejected shortcut (Win32 Error: {err}).";
                return new NativeMessageResponse
                {
                    Success = false,
                    Error = errMsg,
                    Shortcut = sc
                };
            }
            HotKeyHelper.UnregisterHotKey(IntPtr.Zero, testId);
            return new NativeMessageResponse { Success = true, Shortcut = sc };
        }

        if (request.Action.Equals("getShortcuts", StringComparison.OrdinalIgnoreCase))
        {
            return new NativeMessageResponse
            {
                Success = true,
                Slots = slotManager.GetAllSlots()
            };
        }

        if (request.Action.Equals("clearShortcut", StringComparison.OrdinalIgnoreCase) ||
            request.Action.Equals("clear-shortcut", StringComparison.OrdinalIgnoreCase))
        {
            if (request.Slot.HasValue)
            {
                slotManager.SetSlotShortcut(request.Slot.Value, null);
                NotifyDaemonToReloadHotkeys();
                return new NativeMessageResponse
                {
                    Success = true,
                    Message = $"Shortcut for Slot {request.Slot.Value} cleared.",
                    Slot = request.Slot.Value,
                    Shortcut = null,
                    Slots = slotManager.GetAllSlots()
                };
            }
            return new NativeMessageResponse
            {
                Success = false,
                Error = "Slot is required to clear shortcut."
            };
        }

        if (request.Action.Equals("setShortcut", StringComparison.OrdinalIgnoreCase) ||
            request.Action.Equals("set-shortcut", StringComparison.OrdinalIgnoreCase))
        {
            if (request.Slot.HasValue && request.Slot.Value >= 1 && request.Slot.Value <= 100)
            {
                slotManager.SetSlotShortcut(request.Slot.Value, request.Shortcut);
                NotifyDaemonToReloadHotkeys();
                return new NativeMessageResponse
                {
                    Success = true,
                    Message = $"Shortcut for Slot {request.Slot.Value} updated to '{request.Shortcut}'.",
                    Slot = request.Slot.Value,
                    Shortcut = request.Shortcut,
                    Slots = slotManager.GetAllSlots()
                };
            }

            return new NativeMessageResponse
            {
                Success = false,
                Error = "Invalid slot number provided for setShortcut."
            };
        }

        if (request.Action.Equals("getHelperStatus", StringComparison.OrdinalIgnoreCase))
        {
            return new NativeMessageResponse
            {
                Success = true,
                Message = "Chrome Account Switcher Helper is running with Win32 Global Hotkeys.",
                Slots = slotManager.GetAllSlots()
            };
        }

        if (request.Action.Equals("sync-slots", StringComparison.OrdinalIgnoreCase))
        {
            if (request.Slots != null && request.Slots.Count > 0)
            {
                slotManager.SyncSlots(request.Slots);
                NotifyDaemonToReloadHotkeys();
            }

            if (CallingParentHwnd != IntPtr.Zero && !string.IsNullOrWhiteSpace(request.SourceProfile))
            {
                ProfileWindowCache.RecordWindow(request.SourceProfile, CallingParentHwnd);
            }

            return new NativeMessageResponse
            {
                Success = true,
                Message = "Slots synchronized successfully.",
                Slots = slotManager.GetAllSlots()
            };
        }

        if (request.Action.Equals("switchSlot", StringComparison.OrdinalIgnoreCase))
        {
            request.Action = "switch-profile";
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
            string? sourceProfile = request.SourceProfile ?? "Unknown";

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

            // 3. Fast Profile Switch
            IntPtr? cachedHwnd = ProfileWindowCache.GetWindow(targetDirectory);
            IntPtr targetHwnd = IntPtr.Zero;
            string resolvedDisplayName = targetDisplayName ?? targetDirectory;

            if (cachedHwnd.HasValue && WindowManager.IsWindow(cachedHwnd.Value) && WindowManager.IsWindowVisible(cachedHwnd.Value))
            {
                // Fast Path 1: Instant HWND activation (< 2ms)
                targetHwnd = cachedHwnd.Value;
                if (validUrls.Count > 0)
                {
                    ChromeLauncher.OpenUrlsInProfile(targetDirectory, validUrls);
                }
                WindowManager.FocusWindow(targetHwnd);
            }
            else
            {
                // Fast Path 2: Instruct Chrome to open/switch profile via singleton IPC (~60-90ms)
                WindowManager.AllowSetForegroundWindow(WindowManager.ASFW_ANY);
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

                // Quick capture of newly activated Chrome window
                for (int i = 0; i < 4; i++)
                {
                    System.Threading.Thread.Sleep(20);
                    IntPtr fg = WindowManager.GetForegroundWindow();
                    if (fg != IntPtr.Zero)
                    {
                        string cls = WindowManager.GetWindowClass(fg);
                        if (cls.StartsWith("Chrome_WidgetWin", StringComparison.OrdinalIgnoreCase))
                        {
                            targetHwnd = fg;
                            ProfileWindowCache.RecordWindow(targetDirectory, fg);
                            break;
                        }
                    }
                }
            }

            return new NativeMessageResponse
            {
                Success = true,
                Profile = targetDirectory,
                DisplayName = resolvedDisplayName,
                SourceProfile = sourceProfile,
                TargetProfile = resolvedDisplayName,
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
