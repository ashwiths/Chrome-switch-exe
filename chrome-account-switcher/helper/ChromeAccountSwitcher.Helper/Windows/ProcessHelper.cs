using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace ChromeAccountSwitcher.Helper.Windows;

public static class ProcessHelper
{
    private const int ProcessCommandLineInformation = 60;
    private const int ProcessBasicInformation = 0;

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const uint PROCESS_QUERY_INFORMATION = 0x0400;
    private const uint PROCESS_VM_READ = 0x0010;

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(
        IntPtr processHandle,
        int processInformationClass,
        IntPtr processInformation,
        int processInformationLength,
        out int returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(
        uint processAccess,
        bool bInheritHandle,
        int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential)]
    private struct UNICODE_STRING
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_BASIC_INFORMATION
    {
        public IntPtr ExitStatus;
        public IntPtr PebBaseAddress;
        public IntPtr AffinityMask;
        public IntPtr BasePriority;
        public UIntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }

    /// <summary>
    /// Gets the full command line of a running process by PID using native NT APIs.
    /// </summary>
    public static string? GetProcessCommandLine(int processId)
    {
        IntPtr hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_QUERY_INFORMATION | PROCESS_VM_READ, false, processId);
        if (hProcess == IntPtr.Zero)
        {
            hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
            if (hProcess == IntPtr.Zero)
            {
                return null;
            }
        }

        try
        {
            // First call to determine buffer size
            int status = NtQueryInformationProcess(
                hProcess,
                ProcessCommandLineInformation,
                IntPtr.Zero,
                0,
                out int returnLength);

            if (returnLength <= 0)
            {
                return null;
            }

            IntPtr buffer = Marshal.AllocHGlobal(returnLength);
            try
            {
                status = NtQueryInformationProcess(
                    hProcess,
                    ProcessCommandLineInformation,
                    buffer,
                    returnLength,
                    out _);

                if (status == 0) // STATUS_SUCCESS
                {
                    var unicodeString = Marshal.PtrToStructure<UNICODE_STRING>(buffer);
                    if (unicodeString.Buffer != IntPtr.Zero && unicodeString.Length > 0)
                    {
                        return Marshal.PtrToStringUni(unicodeString.Buffer, unicodeString.Length / 2);
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch
        {
            // Ignore failure
        }
        finally
        {
            CloseHandle(hProcess);
        }

        return null;
    }

    /// <summary>
    /// Gets the parent process ID of a given process.
    /// </summary>
    public static int GetParentProcessId(int processId)
    {
        IntPtr hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_QUERY_INFORMATION, false, processId);
        if (hProcess == IntPtr.Zero) return 0;

        try
        {
            int pbiSize = Marshal.SizeOf<PROCESS_BASIC_INFORMATION>();
            IntPtr pbiBuffer = Marshal.AllocHGlobal(pbiSize);
            try
            {
                int status = NtQueryInformationProcess(
                    hProcess,
                    ProcessBasicInformation,
                    pbiBuffer,
                    pbiSize,
                    out _);

                if (status == 0)
                {
                    var pbi = Marshal.PtrToStructure<PROCESS_BASIC_INFORMATION>(pbiBuffer);
                    return pbi.InheritedFromUniqueProcessId.ToInt32();
                }
            }
            finally
            {
                Marshal.FreeHGlobal(pbiBuffer);
            }
        }
        catch
        {
            // Ignore
        }
        finally
        {
            CloseHandle(hProcess);
        }

        return 0;
    }

    /// <summary>
    /// Extracts the Chrome profile directory from a process command line.
    /// </summary>
    public static string? ExtractProfileDirectoryFromCommandLine(string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return null;

        // Pattern 1: --profile-directory="Profile 1" or --profile-directory="Default"
        var match = Regex.Match(commandLine, @"--profile-directory=[""'](?<dir>[^""']+)[""']", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            return match.Groups["dir"].Value;
        }

        // Pattern 2: --profile-directory=Profile1 (unquoted)
        match = Regex.Match(commandLine, @"--profile-directory=(?<dir>[^\s]+)", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            return match.Groups["dir"].Value;
        }

        // If it is a Chrome main browser process and has no --type parameter, default profile is "Default"
        if (!commandLine.Contains("--type=", StringComparison.OrdinalIgnoreCase))
        {
            return "Default";
        }

        return null;
    }
}
