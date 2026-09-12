using System;
using System.Linq;
using System.Windows.Forms;
using ChromeAccountSwitcher.Installer.Configuration;
using ChromeAccountSwitcher.Installer.Core;
using ChromeAccountSwitcher.Installer.UI;

namespace ChromeAccountSwitcher.Installer;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        bool isUninstall = args.Any(a => a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase) ||
                                         a.Equals("-u", StringComparison.OrdinalIgnoreCase));
        bool isSilent = args.Any(a => a.Equals("--silent", StringComparison.OrdinalIgnoreCase) ||
                                      a.Equals("-s", StringComparison.OrdinalIgnoreCase) ||
                                      a.Equals("/silent", StringComparison.OrdinalIgnoreCase) ||
                                      a.Equals("/quiet", StringComparison.OrdinalIgnoreCase));

        string? overrideExtensionId = null;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals("--extension-id", StringComparison.OrdinalIgnoreCase) ||
                args[i].Equals("-e", StringComparison.OrdinalIgnoreCase))
            {
                overrideExtensionId = args[i + 1];
            }
        }

        // Handle Uninstallation mode
        if (isUninstall)
        {
            if (!isSilent)
            {
                var confirm = MessageBox.Show(
                    "Are you sure you want to uninstall Chrome Account Switcher?\r\n\r\n" +
                    "This will remove the Windows helper and Native Messaging configuration.\r\n" +
                    "Your Chrome profiles, cookies, and browsing data will remain completely untouched.",
                    "Uninstall Chrome Account Switcher",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (confirm != DialogResult.Yes)
                {
                    return 0;
                }
            }

            var uninstResult = UninstallEngine.Uninstall();
            if (!isSilent)
            {
                if (uninstResult.Success)
                {
                    MessageBox.Show(
                        "Chrome Account Switcher has been successfully uninstalled.",
                        "Uninstall Complete",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show(
                        $"Uninstallation encountered an issue:\r\n{uninstResult.ErrorMessage}",
                        "Uninstall Issue",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            }

            return uninstResult.Success ? 0 : 1;
        }

        // Handle Silent Installation mode
        if (isSilent)
        {
            var instResult = InstallEngine.Install(overrideExtensionId);
            if (!instResult.Success)
            {
                Console.Error.WriteLine(instResult.ErrorMessage);
            }
            return instResult.Success ? 0 : 1;
        }

        // Standard GUI Installation
        Application.Run(new MainForm(overrideExtensionId));
        return 0;
    }
}
