using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace L2InterludeUpdater;

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

    private readonly TextBox _clientPath = new();
    private readonly Label _versionCaption = new();
    private readonly Label _availableVersion = new();
    private readonly Label _chronicleLabel = new();
    private readonly Label _featuresLabel = new();
    private readonly Label _status = new();
    private readonly ProgressBar _progress = new();
    private readonly RoundedButton _updateButton = new();
    private readonly RoundedButton _cancelButton = new();
    private readonly RoundedButton _settingsButton = new();
    private readonly Button _languageButton = new();
    private readonly ContextMenuStrip _languageMenu = new();
    private readonly ContextMenuStrip _settingsMenu = new();
    private readonly ToolStripMenuItem _chooseFolderMenuItem = new();
    private readonly ToolStripMenuItem _repairMenuItem = new();
    private readonly ToolStripMenuItem _manualInstallMenuItem = new();
    private readonly ToolTip _toolTips = new()
    {
        InitialDelay = 250,
        ReshowDelay = 100,
        AutoPopDelay = 4000,
        ShowAlways = true
    };
    private readonly GitHubReleaseClient _releaseClient = new();
    private readonly ContentInstaller _contentInstaller = new();
    private ContentReleaseInfo? _latestRelease;
    private CancellationTokenSource? _operation;
    private LauncherLanguage _language = LauncherLanguage.Spanish;
    private bool _loadingSettings;
    private LauncherStrings Strings => LauncherLocalization.For(_language);

    public MainForm()
    {
        Text = LauncherBranding.WindowTitle;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(900, 420);
        Size = new Size(1000, 440);
        FormBorderStyle = FormBorderStyle.None;
        MaximizeBox = false;
        BackColor = Canvas;
        ForeColor = Cream;
        Font = new Font("Segoe UI", 9.5f);
        AutoScaleMode = AutoScaleMode.Dpi;

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
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
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
        var brand = new Panel { Dock = DockStyle.Fill };
        brand.Controls.Add(new PictureBox
        {
            Image = LoadEmbeddedImage(LauncherBranding.LogoResource),
            SizeMode = PictureBoxSizeMode.StretchImage,
            BackColor = Color.Transparent,
            Dock = DockStyle.Left,
            Width = 198
        });
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
        windowTools.Controls.Add(SocialButton("Instagram", LauncherBranding.InstagramIconResource, LauncherBranding.InstagramUrl));
        windowTools.Controls.Add(SocialButton("Facebook", LauncherBranding.FacebookIconResource, LauncherBranding.FacebookUrl));
        windowTools.Controls.Add(SocialButton("Twitch", LauncherBranding.TwitchIconResource, LauncherBranding.TwitchUrl));

        StyleLanguageButton(_languageButton);
        _languageButton.Click += (_, _) => _languageMenu.Show(
            _languageButton,
            new Point(0, _languageButton.Height));
        windowTools.Controls.Add(_languageButton);
        ConfigureLanguageMenu();

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
            Padding = new Padding(36, 8, 36, 10),
            ColumnCount = 2,
            RowCount = 1
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 126));
        var progressLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 0, 12, 0)
        };
        progressLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        progressLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 8));
        _status.AutoEllipsis = true;
        _status.Dock = DockStyle.Fill;
        _status.ForeColor = Muted;
        _status.Font = new Font("Segoe UI", 9f);
        _status.TextAlign = ContentAlignment.MiddleLeft;
        progressLayout.Controls.Add(_status, 0, 0);
        _progress.Dock = DockStyle.Fill;
        _progress.Style = ProgressBarStyle.Continuous;
        _progress.ForeColor = Gold;
        _progress.Margin = new Padding(0);
        progressLayout.Controls.Add(_progress, 0, 1);
        footer.Controls.Add(progressLayout, 0, 0);

        StyleSecondaryButton(_settingsButton, "");
        _settingsButton.AutoSize = false;
        _settingsButton.CornerRadius = 10;
        _settingsButton.Font = new Font("Bahnschrift SemiBold", 8.5f);
        _settingsButton.TextAlign = ContentAlignment.MiddleCenter;
        _settingsButton.Dock = DockStyle.Fill;
        _settingsButton.Margin = new Padding(6, 3, 0, 3);
        _settingsButton.Click += (_, _) => _settingsMenu.Show(
            _settingsButton,
            new Point(0, _settingsButton.Height));
        footer.Controls.Add(_settingsButton, 1, 0);
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
        _manualInstallMenuItem.Click += async (_, _) => await InstallLocalManifestAsync();
        _settingsMenu.Items.AddRange([
            _chooseFolderMenuItem,
            _repairMenuItem,
            new ToolStripSeparator(),
            _manualInstallMenuItem
        ]);
    }

    private void ConfigureLanguageMenu()
    {
        _languageMenu.BackColor = Surface;
        _languageMenu.ForeColor = Cream;
        _languageMenu.ShowImageMargin = false;
        foreach (var language in Enum.GetValues<LauncherLanguage>())
        {
            var item = new ToolStripMenuItem(LauncherLocalization.Code(language));
            item.Click += (_, _) => ChangeLanguage(language);
            _languageMenu.Items.Add(item);
        }
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
        _languageButton.Text = LauncherLocalization.Code(_language);
        _cancelButton.Text = Strings.Cancel;
        _chronicleLabel.Text = string.Format(
            Strings.Chronicle,
            LauncherBranding.Chronicle,
            LauncherBranding.Rate);
        _featuresLabel.Text = Strings.Features;
        _settingsButton.Text = Strings.Options;
        _chooseFolderMenuItem.Text = Strings.ChooseFolder;
        _repairMenuItem.Text = Strings.Repair;
        _manualInstallMenuItem.Text = Strings.ManualInstall;
        _toolTips.SetToolTip(_settingsButton, Strings.ChooseFolder);
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
        button.Width = 48;
        button.Height = 30;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = SurfaceRaised;
        button.BackColor = Surface;
        button.ForeColor = Cream;
        button.Font = new Font("Bahnschrift SemiBold", 8.5f);
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
            AccessibleName = $"Abrir {label} de L2 Hamburgo"
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
                "L2 Hamburgo",
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
            _status.Text = Strings.ClientAvailable;
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

        var tempDirectory = Path.Combine(Path.GetTempPath(), "L2InterludeUpdater");
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

    private async Task InstallLocalManifestAsync()
    {
        if (!EnsureInstallDirectory())
        {
            return;
        }
        using var dialog = new OpenFileDialog
        {
            Title = Strings.SelectManifest,
            Filter = "Client manifest (client-manifest.json)|client-manifest.json|JSON (*.json)|*.json",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            _operation = new CancellationTokenSource();
            SetBusy(true, Strings.VerifyingManifest);
            await InstallContentManifestAsync(dialog.FileName, null, false, _operation.Token);
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
                    _status.Text = progress.Message;
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
        var companion = Path.Combine(systemDirectory, "L2HamburgoPresence.exe");
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
        _manualInstallMenuItem.Enabled = !busy;
        _cancelButton.Enabled = busy;
        _cancelButton.Visible = busy;
        _repairMenuItem.Enabled = !busy && _latestRelease is not null &&
                                  StateFiles.ReadContentManifest(_clientPath.Text) is not null;
        _updateButton.Visible = !busy;
        _updateButton.Enabled = !busy && (IsClientInstalled() || _latestRelease is not null);
        RefreshPrimaryButtonColors();
        if (busy)
        {
            _progress.Value = 0;
        }
        if (message is not null)
        {
            _status.Text = message;
        }
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
            "L2InterludeUpdater");
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
