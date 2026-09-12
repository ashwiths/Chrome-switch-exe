using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using ChromeAccountSwitcher.Installer.Configuration;
using ChromeAccountSwitcher.Installer.Core;

namespace ChromeAccountSwitcher.Installer.UI;

public class MainForm : Form
{
    private Label _lblHeaderTitle = null!;
    private Label _lblHeaderSubtitle = null!;
    private Panel _headerPanel = null!;
    private Panel _cardPanel = null!;
    private Label _lblCardDetails = null!;
    private Label _lblStatus = null!;
    private Label _lblNextStep = null!;
    private ProgressBar _progressBar = null!;
    private Button _btnInstall = null!;
    private Button _btnCancel = null!;
    private Button _btnOpenGuide = null!;
    private FlowLayoutPanel _buttonPanel = null!;

    private readonly string? _overrideExtensionId;
    private bool _isInstalled = false;

    public MainForm(string? overrideExtensionId = null)
    {
        _overrideExtensionId = overrideExtensionId;
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        Text = InstallerConfig.InstallerTitle;
        Size = new Size(540, 420);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = true;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(248, 249, 250);
        Font = new Font("Segoe UI", 9.5f, FontStyle.Regular, GraphicsUnit.Point);

        // Header Panel
        _headerPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 90,
            BackColor = Color.White,
            Padding = new Padding(24, 16, 24, 12)
        };
        _headerPanel.Paint += (s, e) =>
        {
            using var pen = new Pen(Color.FromArgb(220, 224, 230), 1);
            e.Graphics.DrawLine(pen, 0, _headerPanel.Height - 1, _headerPanel.Width, _headerPanel.Height - 1);
        };

        _lblHeaderTitle = new Label
        {
            Text = InstallerConfig.AppName,
            Font = new Font("Segoe UI", 15f, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = Color.FromArgb(32, 33, 36),
            AutoSize = true,
            Location = new Point(24, 16)
        };

        _lblHeaderSubtitle = new Label
        {
            Text = InstallerConfig.InstallerSubtitle,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = Color.FromArgb(95, 99, 104),
            AutoSize = true,
            Location = new Point(24, 48)
        };

        _headerPanel.Controls.Add(_lblHeaderTitle);
        _headerPanel.Controls.Add(_lblHeaderSubtitle);

        // Card Panel (Details)
        _cardPanel = new Panel
        {
            Location = new Point(24, 105),
            Size = new Size(476, 120),
            BackColor = Color.White,
            Padding = new Padding(16)
        };
        _cardPanel.Paint += (s, e) =>
        {
            using var pen = new Pen(Color.FromArgb(225, 228, 232), 1);
            e.Graphics.DrawRectangle(pen, 0, 0, _cardPanel.Width - 1, _cardPanel.Height - 1);
        };

        string installFolder = InstallEngine.GetInstallDirectory();
        _lblCardDetails = new Label
        {
            Text = $"• Location: {installFolder}\r\n" +
                   "• Native Messaging: Registers com.chrome_account_switcher.helper\r\n" +
                   "• Hotkey Daemon: Background service for global profile shortcuts\r\n" +
                   "• Chrome Data: Completely untouched (zero profile/cookie changes)",
            Font = new Font("Segoe UI", 9.25f, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = Color.FromArgb(60, 64, 67),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };
        _cardPanel.Controls.Add(_lblCardDetails);

        // Status Label
        _lblStatus = new Label
        {
            Text = "Click Install to configure Chrome Account Switcher on this PC.",
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = Color.FromArgb(32, 33, 36),
            Location = new Point(24, 236),
            Size = new Size(476, 22),
            AutoEllipsis = true
        };

        // Next Step Label (Shown after success)
        _lblNextStep = new Label
        {
            Text = "Next: Add the Chrome extension and start switching between your profiles.",
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = Color.FromArgb(26, 115, 232),
            Location = new Point(24, 260),
            Size = new Size(476, 22),
            Visible = false
        };

        // Progress Bar
        _progressBar = new ProgressBar
        {
            Location = new Point(24, 288),
            Size = new Size(476, 12),
            Style = ProgressBarStyle.Continuous,
            Value = 0,
            Visible = false
        };

        // Button Flow Panel
        _buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 60,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(16, 12, 24, 12),
            BackColor = Color.FromArgb(241, 243, 244)
        };
        _buttonPanel.Paint += (s, e) =>
        {
            using var pen = new Pen(Color.FromArgb(220, 224, 230), 1);
            e.Graphics.DrawLine(pen, 0, 0, _buttonPanel.Width, 0);
        };

        _btnCancel = new Button
        {
            Text = "Cancel",
            Size = new Size(90, 34),
            FlatStyle = FlatStyle.System,
            Margin = new Padding(8, 0, 0, 0)
        };
        _btnCancel.Click += (s, e) => Close();

        _btnInstall = new Button
        {
            Text = "Install",
            Size = new Size(100, 34),
            BackColor = Color.FromArgb(26, 115, 232),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold, GraphicsUnit.Point),
            Margin = new Padding(8, 0, 0, 0)
        };
        _btnInstall.FlatAppearance.BorderSize = 0;
        _btnInstall.Click += async (s, e) => await HandleInstallClick();

        _btnOpenGuide = new Button
        {
            Text = "Open Guide",
            Size = new Size(110, 34),
            FlatStyle = FlatStyle.System,
            Visible = false,
            Margin = new Padding(8, 0, 0, 0)
        };
        _btnOpenGuide.Click += (s, e) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = InstallerConfig.DocumentationUrl,
                    UseShellExecute = true
                });
            }
            catch { }
        };

        _buttonPanel.Controls.Add(_btnCancel);
        _buttonPanel.Controls.Add(_btnInstall);
        _buttonPanel.Controls.Add(_btnOpenGuide);

        Controls.Add(_progressBar);
        Controls.Add(_lblNextStep);
        Controls.Add(_lblStatus);
        Controls.Add(_cardPanel);
        Controls.Add(_headerPanel);
        Controls.Add(_buttonPanel);
    }

    private async Task HandleInstallClick()
    {
        if (_isInstalled)
        {
            Close();
            return;
        }

        _btnInstall.Enabled = false;
        _btnCancel.Enabled = false;
        _progressBar.Visible = true;
        _progressBar.Value = 10;

        var progress = new Action<string, int>((msg, val) =>
        {
            if (InvokeRequired)
            {
                Invoke(() => UpdateProgress(msg, val));
            }
            else
            {
                UpdateProgress(msg, val);
            }
        });

        InstallResult result = await Task.Run(() => InstallEngine.Install(_overrideExtensionId, progress));

        if (result.Success)
        {
            _isInstalled = true;
            _lblStatus.Text = "Chrome Account Switcher has been installed successfully.";
            _lblStatus.ForeColor = Color.FromArgb(19, 115, 51); // Green
            _lblStatus.Font = new Font("Segoe UI", 10f, FontStyle.Bold, GraphicsUnit.Point);
            _lblNextStep.Visible = true;

            _progressBar.Value = 100;
            _btnInstall.Text = "Finish";
            _btnInstall.BackColor = Color.FromArgb(19, 115, 51);
            _btnInstall.Enabled = true;
            _btnCancel.Visible = false;
            _btnOpenGuide.Visible = true;
        }
        else
        {
            _lblStatus.Text = $"Installation failed: {result.ErrorMessage}";
            _lblStatus.ForeColor = Color.FromArgb(217, 48, 37); // Red
            _btnInstall.Enabled = true;
            _btnCancel.Enabled = true;
        }
    }

    private void UpdateProgress(string msg, int val)
    {
        _lblStatus.Text = msg;
        _progressBar.Value = Math.Clamp(val, 0, 100);
    }
}
