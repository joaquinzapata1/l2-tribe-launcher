using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace L2TribeLauncher;

internal sealed class MainForm : Form
{
    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(
        IntPtr windowHandle,
        int message,
        IntPtr wParam,
        IntPtr lParam);

    private static readonly Color Canvas = LauncherBranding.Canvas;
    private static readonly Color Surface = LauncherBranding.Surface;
    private static readonly Color SurfaceRaised = LauncherBranding.SurfaceRaised;
    private static readonly Color Gold = LauncherBranding.Accent;
    private static readonly Color Cream = LauncherBranding.Text;
    private static readonly Color Muted = LauncherBranding.MutedText;
    private static readonly Icon AppIcon = new Icon(
        typeof(MainForm).Assembly.GetManifestResourceStream("L2TribeLauncher.Assets.favicon.ico")!);

    private readonly TextBox _clientPath = new();
    private readonly Label _versionCaption = new();
    private readonly Label _availableVersion = new();
    private readonly Label _chronicleLabel = new();
    private readonly Label _featuresLabel = new();
    private readonly Label _status = new();
    private readonly Label _clientPathLabel = new();
    private readonly SlimProgressBar _progress = new();
    private readonly RoundedButton _updateButton = new();
    private readonly RoundedButton _cancelButton = new();
    private readonly RoundedButton _settingsButton = new();
    private readonly Button _languageButton = new();
    private readonly Panel _languagePanel = new();
    private readonly ContextMenuStrip _settingsMenu = new();
    private readonly ToolStripMenuItem _chooseFolderMenuItem = new();
    private readonly ToolStripMenuItem _repairMenuItem = new();
    private readonly ToolTip _toolTips = new()
    {
        InitialDelay = 250,
        ReshowDelay = 100,
        AutoPopDelay = 4000,
        ShowAlways = true
    };
    private readonly GitHubReleaseClient _releaseClient = new();
    private readonly ContentInstaller _contentInstaller = new();
    private readonly Image _arFlag = CreateMenuFlag(LoadEmbeddedImage(LauncherBranding.ArFlagResource));
    private readonly Image _brFlag = CreateMenuFlag(LoadEmbeddedImage(LauncherBranding.BrFlagResource));
    private readonly Image _gbFlag = CreateMenuFlag(LoadEmbeddedImage(LauncherBranding.GbFlagResource));
    private ContentReleaseInfo? _latestRelease;
    private CancellationTokenSource? _operation;
    private LauncherLanguage _language = LauncherLanguage.Spanish;
    private bool _loadingSettings;
    private LauncherStrings Strings => LauncherLocalization.For(_language);

    public MainForm()
    {
        Text = LauncherBranding.WindowTitle;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(900, 500);
        ClientSize = new Size(1000, 520);
        FormBorderStyle = FormBorderStyle.None;
        MaximizeBox = false;
        BackColor = Canvas;
        ForeColor = Cream;
        Font = new Font("Segoe UI", 9.5f);
        AutoScaleMode = AutoScaleMode.Dpi;
        Icon = AppIcon;

        BuildLayout();
        LoadSettings();
        RefreshInstalledVersion();
        Shown += async (_, _) => await CheckForUpdatesAsync(showErrors: false);
        FormClosed += (_, _) =>
        {
            _operation?.Cancel();
            _operation?.Dispose();
            _releaseClient.Dispose();
            _contentInstaller.Dispose();
            _arFlag.Dispose();
            _brFlag.Dispose();
            _gbFlag.Dispose();
            _toolTips.Dispose();
        };
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Canvas,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(1)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        Controls.Add(root);

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Canvas,
            ColumnCount = 2,
            Padding = new Padding(36, 8, 36, 6)
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var brand = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent
        };

        var logo = new PictureBox
        {
            Size = new Size(140, 41),
            Location = new Point(0, 23),
            Image = LoadEmbeddedImage(LauncherBranding.LogoResource),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Transparent
        };
        logo.MouseDown += DragWindow;
        brand.Controls.Add(logo);
        brand.MouseDown += DragWindow;
        header.Controls.Add(brand, 0, 0);

        var windowTools = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0)
        };
        windowTools.Controls.Add(SocialButton("Discord", LauncherBranding.DiscordIconResource, LauncherBranding.DiscordUrl));
        windowTools.Controls.Add(SocialButton("Facebook", LauncherBranding.FacebookIconResource, LauncherBranding.FacebookUrl));
        windowTools.Controls.Add(SocialButton("Instagram", LauncherBranding.InstagramIconResource, LauncherBranding.InstagramUrl));

        StyleLanguageButton(_languageButton);
        _languageButton.Click += (_, _) => ToggleLanguagePanel();
        windowTools.Controls.Add(_languageButton);
        BuildLanguagePanel();

        var minimizeButton = WindowButton("_", (_, _) => WindowState = FormWindowState.Minimized);
        var closeButton = WindowButton("X", (_, _) => CloseLauncher());
        closeButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(126, 50, 45);
        windowTools.Controls.Add(minimizeButton);
        windowTools.Controls.Add(closeButton);
        header.Controls.Add(windowTools, 1, 0);
        header.MouseDown += DragWindow;
        root.Controls.Add(header, 0, 0);

        var hero = new HeroPanel(LoadEmbeddedImage(LauncherBranding.HeroResource))
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(36, 0, 36, 12),
            Padding = new Padding(24)
        };
        var heroLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 2,
            RowCount = 1
        };
        heroLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
        heroLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        hero.Controls.Add(heroLayout);

        var heroIdentity = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(10, 0, 28, 0)
        };
        heroIdentity.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        heroIdentity.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        heroIdentity.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _chronicleLabel.AutoSize = true;
        _chronicleLabel.ForeColor = Cream;
        _chronicleLabel.BackColor = Color.Transparent;
        _chronicleLabel.Font = new Font("Bahnschrift SemiBold", 25f);
        _chronicleLabel.Margin = new Padding(0, 0, 0, 6);
        heroIdentity.Controls.Add(_chronicleLabel, 0, 1);
        _featuresLabel.AutoSize = true;
        _featuresLabel.ForeColor = Gold;
        _featuresLabel.BackColor = Color.Transparent;
        _featuresLabel.Font = new Font("Bahnschrift SemiBold", 8.5f);
        _featuresLabel.Margin = new Padding(1, 0, 0, 8);
        heroIdentity.Controls.Add(_featuresLabel, 0, 2);
        heroLayout.Controls.Add(heroIdentity, 0, 0);

        var actionCard = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(190, 19, 24, 28),
            Padding = new Padding(20),
            RowCount = 4,
            ColumnCount = 1
        };
        actionCard.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        actionCard.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        actionCard.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        actionCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        _versionCaption.AutoSize = true;
        _versionCaption.ForeColor = Muted;
        _versionCaption.Font = new Font("Bahnschrift SemiBold", 8.5f);
        _versionCaption.Margin = new Padding(0, 0, 0, 2);
        actionCard.Controls.Add(_versionCaption, 0, 0);
        _availableVersion.AutoSize = true;
        _availableVersion.ForeColor = Cream;
        _availableVersion.Font = new Font("Bahnschrift SemiBold", 18f);
        _availableVersion.Margin = new Padding(0);
        actionCard.Controls.Add(_availableVersion, 0, 1);
        StylePrimaryButton(_updateButton, "");
        _updateButton.Dock = DockStyle.Fill;
        _updateButton.Enabled = false;
        _updateButton.Click += async (_, _) => await HandlePrimaryActionAsync();
        actionCard.Controls.Add(_updateButton, 0, 3);
        StyleCancelButton(_cancelButton);
        _cancelButton.Dock = DockStyle.Fill;
        _cancelButton.Visible = false;
        _cancelButton.Click += (_, _) => CancelCurrentOperation();
        actionCard.Controls.Add(_cancelButton, 0, 3);
        heroLayout.Controls.Add(actionCard, 1, 0);
        root.Controls.Add(hero, 0, 1);

        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Surface,
            Padding = new Padding(36, 10, 36, 12),
            ColumnCount = 2,
            RowCount = 1
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 126));
        footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var progressLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(0, 0, 12, 0)
        };
        progressLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        progressLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        progressLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 6));
        _status.AutoEllipsis = true;
        _status.Dock = DockStyle.Fill;
        _status.ForeColor = Muted;
        _status.Font = new Font("Segoe UI", 9f);
        _status.TextAlign = ContentAlignment.MiddleLeft;
        progressLayout.Controls.Add(_status, 0, 0);
        _clientPathLabel.AutoEllipsis = true;
        _clientPathLabel.Dock = DockStyle.Fill;
        _clientPathLabel.ForeColor = Color.FromArgb(135, 142, 149);
        _clientPathLabel.Font = new Font("Segoe UI", 8.5f);
        _clientPathLabel.TextAlign = ContentAlignment.MiddleLeft;
        progressLayout.Controls.Add(_clientPathLabel, 0, 1);
        _progress.Dock = DockStyle.Fill;
        _progress.Margin = new Padding(0);
        _progress.Visible = false;
        progressLayout.Controls.Add(_progress, 0, 2);
        footer.Controls.Add(progressLayout, 0, 0);

        StyleSecondaryButton(_settingsButton, "");
        _settingsButton.AutoSize = false;
        _settingsButton.CornerRadius = 4;
        _settingsButton.Font = new Font("Bahnschrift SemiBold", 8.5f);
        _settingsButton.FlatAppearance.BorderSize = 0;
        _settingsButton.BackColor = SurfaceRaised;
        _settingsButton.ForeColor = Gold;
        _settingsButton.TextAlign = ContentAlignment.MiddleCenter;
        _settingsButton.Size = new Size(112, 36);
        _settingsButton.Margin = new Padding(0);
        _settingsButton.Click += (_, _) => _settingsMenu.Show(
            _settingsButton,
            new Point(0, _settingsButton.Height));

        var settingsPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(14, 0, 0, 0)
        };
        settingsPanel.Controls.Add(_settingsButton);
        settingsPanel.Layout += (_, _) => _settingsButton.Location = new Point(
            settingsPanel.ClientSize.Width - _settingsButton.Width,
            Math.Max(0, (settingsPanel.ClientSize.Height - _settingsButton.Height) / 2));
        footer.Controls.Add(settingsPanel, 1, 0);
        ConfigureSettingsMenu();
        root.Controls.Add(footer, 0, 2);
    }


    private void ConfigureSettingsMenu()
    {
        _settingsMenu.BackColor = Surface;
        _settingsMenu.ForeColor = Cream;
        _settingsMenu.ShowImageMargin = false;
        _chooseFolderMenuItem.Click += (_, _) => BrowseClientDirectory();
        _repairMenuItem.Click += async (_, _) => await InstallLatestAsync(repair: true);
        _settingsMenu.Items.AddRange([
            _chooseFolderMenuItem,
            _repairMenuItem
        ]);
    }

    private void BuildLanguagePanel()
    {
        _languagePanel.Size = new Size(36, 78);
        _languagePanel.BackColor = Surface;
        _languagePanel.Visible = false;
        _languagePanel.BorderStyle = BorderStyle.None;

        var y = 0;
        foreach (var language in Enum.GetValues<LauncherLanguage>())
        {
            var flagBox = new PictureBox
            {
                Size = new Size(36, 26),
                Location = new Point(0, y),
                Image = FlagFor(language),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand
            };
            flagBox.MouseDown += (_, _) =>
            {
                ChangeLanguage(language);
                _languagePanel.Hide();
            };
            _languagePanel.Controls.Add(flagBox);
            y += 26;
        }

        Controls.Add(_languagePanel);
    }

    private void ToggleLanguagePanel()
    {
        if (_languagePanel.Visible)
        {
            _languagePanel.Hide();
            return;
        }

        var screenLocation = _languageButton.PointToScreen(new Point(0, _languageButton.Height));
        _languagePanel.Location = PointToClient(screenLocation);
        _languagePanel.BringToFront();
        _languagePanel.Show();
    }

    private void ChangeLanguage(LauncherLanguage language)
    {
        _language = language;
        ApplyLanguage();
        if (!_loadingSettings)
        {
            SaveSettings();
        }
    }

    private void ApplyLanguage()
    {
        _versionCaption.Text = Strings.Version;
        _languageButton.Image = FlagFor(_language);
        _cancelButton.Text = Strings.Cancel;
        _chronicleLabel.Text = Strings.Chronicle;
        _featuresLabel.Text = Strings.Features;
        _settingsButton.Text = Strings.Options;
        _chooseFolderMenuItem.Text = Strings.ChooseFolder;
        _repairMenuItem.Text = Strings.Repair;
        _toolTips.SetToolTip(_settingsButton, Strings.Options);
        RefreshClientPathLabel();
        if (string.IsNullOrWhiteSpace(_availableVersion.Text))
        {
            _availableVersion.Text = Strings.Checking;
        }
        if (_latestRelease is null && _operation is null)
        {
            _status.Text = Strings.Ready;
        }
        RefreshInstalledVersion();
    }

    private static void StylePrimaryButton(Button button, string text)
    {
        button.Text = text;
        button.AutoSize = false;
        button.Height = 46;
        button.Padding = new Padding(14, 5, 14, 5);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = Gold;
        button.ForeColor = Canvas;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(247, 186, 75);
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(210, 144, 35);
        button.Font = new Font("Bahnschrift SemiBold", 10f);
        button.Margin = new Padding(0);
        if (button is RoundedButton rounded)
        {
            rounded.CornerRadius = 12;
        }
    }

    private static void StyleSecondaryButton(Button button, string text)
    {
        button.Text = text;
        button.AutoSize = true;
        button.Height = 34;
        button.Padding = new Padding(10, 3, 10, 3);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = Color.FromArgb(91, 97, 105);
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor = SurfaceRaised;
        button.BackColor = Surface;
        button.ForeColor = Cream;
        button.Font = new Font("Bahnschrift SemiBold", 8.5f);
        button.Margin = new Padding(8, 0, 0, 0);
    }

    private static void StyleLanguageButton(Button button)
    {
        button.Width = 36;
        button.Height = 26;
        button.Text = "";
        button.ImageAlign = ContentAlignment.MiddleCenter;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = SurfaceRaised;
        button.BackColor = Surface;
        button.Margin = new Padding(8, 5, 2, 0);
        button.Cursor = Cursors.Hand;
        button.TabStop = false;
    }

    private static void StyleCancelButton(Button button)
    {
        button.Text = "CANCELAR";
        button.AutoSize = false;
        button.Height = 46;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = Color.FromArgb(207, 104, 83);
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(112, 50, 45);
        button.BackColor = Color.FromArgb(86, 44, 42);
        button.ForeColor = Color.White;
        button.Font = new Font("Bahnschrift SemiBold", 10f);
        button.Margin = new Padding(0);
        if (button is RoundedButton rounded)
        {
            rounded.CornerRadius = 12;
        }
    }

    private static Button WindowButton(string text, EventHandler onClick)
    {
        var button = new Button
        {
            Text = text,
            Width = 34,
            Height = 30,
            FlatStyle = FlatStyle.Flat,
            BackColor = Canvas,
            ForeColor = Muted,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            Margin = new Padding(6, 5, 0, 0),
            TabStop = false
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = SurfaceRaised;
        button.Click += onClick;
        return button;
    }

    private Button SocialButton(string label, string iconResource, string url)
    {
        var icon = LoadEmbeddedImage(iconResource);
        var button = new Button
        {
            Text = "",
            Image = icon,
            ImageAlign = ContentAlignment.MiddleCenter,
            AutoSize = false,
            Width = 34,
            Height = 30,
            Padding = new Padding(0),
            FlatStyle = FlatStyle.Flat,
            BackColor = Canvas,
            Margin = new Padding(2, 5, 2, 0),
            Cursor = Cursors.Hand,
            TabStop = false,
            AccessibleName = $"Abrir {label} de L2 Tribe"
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = SurfaceRaised;
        button.FlatAppearance.MouseDownBackColor = Surface;
        button.Click += (_, _) => OpenExternalUrl(url);
        button.Disposed += (_, _) => icon.Dispose();
        _toolTips.SetToolTip(button, label);
        return button;
    }

    private static void OpenExternalUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception error)
        {
            MessageBox.Show(
                $"No se pudo abrir el link.\n\n{error.Message}",
                LauncherBranding.WindowTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void DragWindow(object? sender, MouseEventArgs eventArgs)
    {
        if (eventArgs.Button != MouseButtons.Left)
        {
            return;
        }
        ReleaseCapture();
        SendMessage(Handle, 0x00A1, (IntPtr)2, IntPtr.Zero);
    }

    private void CloseLauncher()
    {
        if (_operation is not null)
        {
            MessageBox.Show(
                this,
                Strings.OperationInProgress,
                Strings.OperationInProgressTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }
        Close();
    }

    private void LoadSettings()
    {
        var settings = SettingsStore.Load();
        _loadingSettings = true;
        try
        {
            _language = LauncherLocalization.Parse(settings.Language);
            if (!string.IsNullOrWhiteSpace(settings.ClientDirectory))
            {
                _clientPath.Text = settings.ClientDirectory;
            }
            else
            {
                var executableDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
                _clientPath.Text = File.Exists(Path.Combine(executableDirectory, "system-e", "l2.exe"))
                    ? executableDirectory
                    : string.Empty;
            }
            ApplyLanguage();
        }
        finally
        {
            _loadingSettings = false;
        }
    }

    private void SaveSettings() => SettingsStore.Save(new UpdaterSettings
    {
        ClientDirectory = _clientPath.Text,
        Language = LauncherLocalization.Code(_language)
    });

    private bool BrowseClientDirectory()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = Strings.FolderDialog,
            ShowNewFolderButton = true,
            SelectedPath = Directory.Exists(_clientPath.Text) ? _clientPath.Text : ""
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _clientPath.Text = dialog.SelectedPath;
            SaveSettings();
            RefreshInstalledVersion();
            return true;
        }
        return false;
    }

    private async Task CheckForUpdatesAsync(bool showErrors)
    {
        if (_operation is not null)
        {
            return;
        }

        try
        {
            _operation = new CancellationTokenSource();
            SetBusy(true, Strings.Checking);
            _latestRelease = await _releaseClient.GetLatestContentAsync(_operation.Token);
            _availableVersion.Text = _latestRelease.Version;
        }
        catch (OperationCanceledException)
        {
            _status.Text = Strings.SearchCanceled;
        }
        catch (Exception error)
        {
            _latestRelease = null;
            _availableVersion.Text = Strings.NoRelease;
            _status.Text = error.Message;
            if (showErrors)
            {
                ShowError(error);
            }
        }
        finally
        {
            _operation?.Dispose();
            _operation = null;
            SetBusy(false);
            RefreshInstalledVersion();
        }
    }

    private async Task HandlePrimaryActionAsync()
    {
        var state = Directory.Exists(_clientPath.Text)
            ? StateFiles.ReadContentManifest(_clientPath.Text)
            : null;
        var canPlay = IsClientInstalled() &&
                      (_latestRelease is null ||
                       state is not null && string.Equals(
                           state.ClientVersion,
                           _latestRelease.Version,
                           StringComparison.OrdinalIgnoreCase));
        if (canPlay)
        {
            LaunchGame();
            return;
        }
        await InstallLatestAsync(repair: false);
    }

    private async Task InstallLatestAsync(bool repair)
    {
        if (_latestRelease is null)
        {
            await CheckForUpdatesAsync(showErrors: true);
            if (_latestRelease is null)
            {
                return;
            }
        }
        if (!EnsureInstallDirectory())
        {
            return;
        }

        var tempDirectory = Path.Combine(Path.GetTempPath(), "L2TribeLauncher");
        Directory.CreateDirectory(tempDirectory);
        var manifestPath = Path.Combine(tempDirectory, $"client-manifest-{_latestRelease.Version}.json");
        try
        {
            _operation = new CancellationTokenSource();
            SetBusy(true, repair ? Strings.PreparingRepair : Strings.PreparingClient);
            var checksum = await _releaseClient.DownloadChecksumAsync(
                _latestRelease.ManifestChecksumUrl,
                _operation.Token);
            await _releaseClient.DownloadAsync(
                _latestRelease.ManifestUrl,
                manifestPath,
                percent => BeginInvoke(() =>
                {
                    _progress.Value = Math.Clamp(percent, 0, 100);
                    _status.Text = string.Format(Strings.DownloadingManifest, percent);
                }),
                _operation.Token);
            await InstallContentManifestAsync(manifestPath, checksum, repair, _operation.Token);
        }
        catch (OperationCanceledException)
        {
            _status.Text = Strings.OperationCanceled;
        }
        catch (Exception error)
        {
            ShowError(error);
        }
        finally
        {
            _operation?.Dispose();
            _operation = null;
            SetBusy(false);
            RefreshInstalledVersion();
        }
    }


    private async Task InstallContentManifestAsync(
        string manifestPath,
        string? checksum,
        bool repair,
        CancellationToken cancellationToken)
    {
        var clientDirectory = _clientPath.Text;
        var result = await Task.Run(
            () => _contentInstaller.InstallAsync(
                manifestPath,
                clientDirectory,
                checksum,
                repair,
                progress => BeginInvoke(() =>
                {
                    _progress.Value = Math.Clamp(progress.Percent, 0, 100);
                    _status.Text = FormatInstallProgress(progress);
                }),
                cancellationToken),
            cancellationToken);

        var launcherWarning = EnsureLauncherEntryPoint();

        var backupMessage = result.BackupDirectory is null
            ? Strings.NoBackup
            : string.Format(Strings.Backup, result.BackupDirectory);
        MessageBox.Show(
            this,
            string.Format(Strings.ClientReady, result.ClientVersion) + "\n\n" +
            string.Format(Strings.InstalledFiles, result.InstalledFiles) + "\n" +
            string.Format(Strings.DownloadedPackages, result.DownloadedPackages) + "\n" +
            string.Format(Strings.DeletedFiles, result.DeletedPaths) + $"\n{backupMessage}" +
            (launcherWarning is null ? "" : $"\n\n{launcherWarning}"),
            LauncherBranding.WindowTitle,
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void RefreshInstalledVersion()
    {
        var state = Directory.Exists(_clientPath.Text)
            ? StateFiles.ReadContentManifest(_clientPath.Text)
            : null;
        var clientInstalled = IsClientInstalled();
        var upToDate = state is not null && _latestRelease is not null &&
                       string.Equals(
                           state.ClientVersion,
                           _latestRelease.Version,
                           StringComparison.OrdinalIgnoreCase);
        _updateButton.Text = !clientInstalled
            ? Strings.Install
            : upToDate || _latestRelease is null
                ? Strings.Play
                : Strings.Update;
        _repairMenuItem.Enabled = state is not null && _latestRelease is not null && _operation is null;
        _updateButton.Enabled = _operation is null && (clientInstalled || _latestRelease is not null);
        RefreshClientPathLabel();
        if (_operation is null)
        {
            _status.Text = !clientInstalled
                ? Strings.ReadyToInstall
                : upToDate || _latestRelease is null
                    ? Strings.ReadyToPlay
                    : Strings.UpdateAvailable;
        }
        RefreshPrimaryButtonColors();
    }

    private bool IsClientInstalled() =>
        File.Exists(Path.Combine(_clientPath.Text, "system-e", "l2.exe"));

    private bool HasInstallPath() => !string.IsNullOrWhiteSpace(_clientPath.Text);

    private bool EnsureInstallDirectory()
    {
        if (!HasInstallPath())
        {
            if (!BrowseClientDirectory())
            {
                return false;
            }
        }

        Directory.CreateDirectory(_clientPath.Text);
        var hasState = StateFiles.ReadContentManifest(_clientPath.Text) is not null;
        if (!hasState && Directory.EnumerateFileSystemEntries(_clientPath.Text).Any())
        {
            var answer = MessageBox.Show(
                this,
                Strings.ExistingFolder,
                Strings.ExistingFolderTitle,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes)
            {
                return false;
            }
        }

        SaveSettings();
        return true;
    }

    private void CancelCurrentOperation()
    {
        if (_operation is null)
        {
            return;
        }
        _cancelButton.Enabled = false;
        _status.Text = Strings.Canceling;
        _operation.Cancel();
    }

    private void LaunchGame()
    {
        if (!IsClientInstalled())
        {
            MessageBox.Show(
                this,
                Strings.ClientNotInstalled,
                Strings.ClientUnavailable,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }
        var launcherWarning = EnsureLauncherEntryPoint();
        if (launcherWarning is not null)
        {
            _status.Text = launcherWarning;
        }

        var executable = Path.Combine(_clientPath.Text, "system-e", "l2.exe");
        var game = Process.Start(new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = true
        });
        StartDiscordPresence(game, Path.GetDirectoryName(executable)!);
    }

    private string? EnsureLauncherEntryPoint()
    {
        try
        {
            LauncherInstallation.EnsureInstalled(_clientPath.Text);
            return null;
        }
        catch (Exception error)
        {
            return string.Format(Strings.ShortcutError, error.Message);
        }
    }

    private static void StartDiscordPresence(Process? game, string systemDirectory)
    {
        var companion = Path.Combine(systemDirectory, "L2TribePresence.exe");
        if (game is null || !File.Exists(companion))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = companion,
                Arguments = $"--pid {game.Id}",
                WorkingDirectory = systemDirectory,
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch
        {
            // Discord presence is optional and must never block the game launch.
        }
    }

    private void SetBusy(bool busy, string? message = null)
    {
        _settingsButton.Enabled = !busy;
        _chooseFolderMenuItem.Enabled = !busy;
        _cancelButton.Enabled = busy;
        _cancelButton.Visible = busy;
        _repairMenuItem.Enabled = !busy && _latestRelease is not null &&
                                  StateFiles.ReadContentManifest(_clientPath.Text) is not null;
        _updateButton.Visible = !busy;
        _updateButton.Enabled = !busy && (IsClientInstalled() || _latestRelease is not null);
        RefreshPrimaryButtonColors();
        _progress.Visible = busy;
        if (busy)
        {
            _progress.Value = 0;
        }
        if (message is not null)
        {
            _status.Text = message;
        }
    }

    private void RefreshClientPathLabel()
    {
        var path = string.IsNullOrWhiteSpace(_clientPath.Text)
            ? Strings.NoFolderSelected
            : _clientPath.Text;
        _clientPathLabel.Text = string.Format(Strings.ClientFolder, path);
        _toolTips.SetToolTip(_clientPathLabel, path);
    }

    private void RefreshPrimaryButtonColors()
    {
        _updateButton.BackColor = _updateButton.Enabled ? Gold : SurfaceRaised;
        _updateButton.ForeColor = _updateButton.Enabled ? Canvas : Muted;
    }

    private void ShowError(Exception error)
    {
        _status.Text = error.Message;
        MessageBox.Show(
            this,
            error.Message,
            Strings.GenericErrorTitle,
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    private string FormatInstallProgress(InstallProgress progress)
    {
        if (progress.Kind == InstallProgressKind.ValidatingClientManifest)
        {
            return Strings.ValidatingClientManifest;
        }
        if (progress.Kind == InstallProgressKind.ClientUpToDate)
        {
            return string.Format(Strings.ClientUpToDate, progress.Message);
        }
        if (progress.Kind == InstallProgressKind.BackingUpFiles)
        {
            return Strings.BackingUpFiles;
        }
        if (progress.Kind == InstallProgressKind.InstallingFile)
        {
            return string.Format(Strings.InstallingFile, progress.Message);
        }
        if (progress.Kind == InstallProgressKind.CheckingFile)
        {
            return string.Format(Strings.CheckingFile, progress.Message);
        }
        if (progress.Kind == InstallProgressKind.ClientInstalled)
        {
            return string.Format(Strings.ClientInstalled, progress.Message);
        }
        if (progress.Kind != InstallProgressKind.DownloadingClient)
        {
            return progress.Message;
        }

        return string.Format(
            Strings.DownloadingClient,
            progress.Percent,
            FormatBytes(progress.CompletedBytes),
            FormatBytes(progress.TotalBytes));
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024)
        {
            return $"{bytes / 1024d / 1024d / 1024d:0.00} GB";
        }
        return $"{bytes / 1024d / 1024d:0.0} MB";
    }

    private static Image LoadEmbeddedImage(string resourceName)
    {
        using var stream = typeof(MainForm).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded launcher asset not found: {resourceName}");
        using var source = Image.FromStream(stream);
        var bitmap = new Bitmap(
            source.Width,
            source.Height,
            System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        bitmap.SetResolution(96, 96);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.DrawImage(source, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
        return bitmap;
    }

    private static Image CreateMenuFlag(Image flag)
    {
        const int itemWidth = 36;
        const int itemHeight = 26;
        const int maxFlagWidth = 26;
        const int maxFlagHeight = 18;
        var composite = new Bitmap(
            itemWidth,
            itemHeight,
            System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        composite.SetResolution(96, 96);
        using var graphics = Graphics.FromImage(composite);
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.Clear(Color.Transparent);
        var scale = Math.Min(
            (double)maxFlagWidth / flag.Width,
            (double)maxFlagHeight / flag.Height);
        var scaledWidth = (int)(flag.Width * scale);
        var scaledHeight = (int)(flag.Height * scale);
        var x = (itemWidth - scaledWidth) / 2;
        var y = (itemHeight - scaledHeight) / 2;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.DrawImage(flag, new Rectangle(x, y, scaledWidth, scaledHeight));
        return composite;
    }

    private Image FlagFor(LauncherLanguage language) => language switch
    {
        LauncherLanguage.English => _gbFlag,
        LauncherLanguage.Portuguese => _brFlag,
        _ => _arFlag
    };

    private sealed class SlimProgressBar : Control
    {
        private int _value;

        public SlimProgressBar()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw, true);
        }

        public int Value
        {
            get => _value;
            set
            {
                _value = Math.Clamp(value, 0, 100);
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs eventArgs)
        {
            eventArgs.Graphics.Clear(SurfaceRaised);
            if (_value <= 0)
            {
                return;
            }
            var width = (int)Math.Round(ClientSize.Width * (_value / 100d));
            using var brush = new SolidBrush(Gold);
            eventArgs.Graphics.FillRectangle(brush, 0, 0, width, ClientSize.Height);
        }
    }

    private sealed class RoundedButton : Button
    {
        private int _cornerRadius = 10;

        public int CornerRadius
        {
            get => _cornerRadius;
            set
            {
                _cornerRadius = Math.Max(0, value);
                UpdateRegion();
                Invalidate();
            }
        }

        protected override void OnResize(EventArgs eventArgs)
        {
            base.OnResize(eventArgs);
            UpdateRegion();
        }

        private void UpdateRegion()
        {
            if (Width <= 1 || Height <= 1)
            {
                return;
            }
            using var path = CreateRoundedRectangle(ClientRectangle, _cornerRadius);
            var previous = Region;
            Region = new Region(path);
            previous?.Dispose();
        }

        private static GraphicsPath CreateRoundedRectangle(Rectangle bounds, int radius)
        {
            var path = new GraphicsPath();
            bounds.Width--;
            bounds.Height--;
            var diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
            if (diameter <= 1)
            {
                path.AddRectangle(bounds);
                return path;
            }
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    private sealed class HeroPanel : Panel
    {
        private readonly Image _backgroundImage;

        public HeroPanel(Image backgroundImage)
        {
            _backgroundImage = backgroundImage;
            DoubleBuffered = true;
            ResizeRedraw = true;
        }

        protected override void OnResize(EventArgs eventArgs)
        {
            base.OnResize(eventArgs);
            if (Width <= 0 || Height <= 0)
            {
                return;
            }

            using var shape = CreateRoundedRectangle(ClientRectangle, 22);
            var previousRegion = Region;
            Region = new Region(shape);
            previousRegion?.Dispose();
        }

        protected override void OnPaintBackground(PaintEventArgs paintEventArgs)
        {
            var graphics = paintEventArgs.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var shape = CreateRoundedRectangle(ClientRectangle, 22);
            graphics.SetClip(shape);

            var scale = Math.Max(
                (float)Width / _backgroundImage.Width,
                (float)Height / _backgroundImage.Height);
            var drawWidth = (int)Math.Ceiling(_backgroundImage.Width * scale);
            var drawHeight = (int)Math.Ceiling(_backgroundImage.Height * scale);
            var destination = new Rectangle(
                (Width - drawWidth) / 2,
                (Height - drawHeight) / 2,
                drawWidth,
                drawHeight);
            graphics.DrawImage(_backgroundImage, destination);

            using var horizontalScrim = new LinearGradientBrush(
                ClientRectangle,
                Color.FromArgb(225, 12, 17, 18),
                Color.FromArgb(72, 12, 17, 18),
                LinearGradientMode.Horizontal);
            graphics.FillRectangle(horizontalScrim, ClientRectangle);
            using var verticalScrim = new LinearGradientBrush(
                ClientRectangle,
                Color.FromArgb(15, 0, 0, 0),
                Color.FromArgb(150, 0, 0, 0),
                LinearGradientMode.Vertical);
            graphics.FillRectangle(verticalScrim, ClientRectangle);
            graphics.ResetClip();

            using var border = new Pen(Color.FromArgb(45, 235, 170, 57), 1f);
            graphics.DrawPath(border, shape);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _backgroundImage.Dispose();
            }
            base.Dispose(disposing);
        }

        private static GraphicsPath CreateRoundedRectangle(Rectangle bounds, int radius)
        {
            var path = new GraphicsPath();
            if (bounds.Width <= 1 || bounds.Height <= 1)
            {
                path.AddRectangle(bounds);
                return path;
            }

            bounds.Width--;
            bounds.Height--;
            var diameter = radius * 2;
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    private static class SettingsStore
    {
        private static readonly string DirectoryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "L2TribeLauncher");
        private static readonly string FilePath = Path.Combine(DirectoryPath, "settings.json");

        public static UpdaterSettings Load()
        {
            try
            {
                return File.Exists(FilePath)
                    ? JsonSerializer.Deserialize<UpdaterSettings>(File.ReadAllText(FilePath), JsonDefaults.Options)
                      ?? new UpdaterSettings()
                    : new UpdaterSettings();
            }
            catch
            {
                return new UpdaterSettings();
            }
        }

        public static void Save(UpdaterSettings settings)
        {
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, JsonDefaults.Options));
            }
            catch
            {
                // Settings are optional; installation must not depend on them.
            }
        }
    }
}
