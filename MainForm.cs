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
    private readonly Label _installedVersion = new();
    private readonly Label _availableVersion = new();
    private readonly Label _status = new();
    private readonly Label _releaseNotes = new();
    private readonly ProgressBar _progress = new();
    private readonly Button _browseButton = new();
    private readonly Button _checkButton = new();
    private readonly Button _updateButton = new();
    private readonly Button _cancelButton = new();
    private readonly Button _repairButton = new();
    private readonly Button _localManifestButton = new();
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

    public MainForm()
    {
        Text = LauncherBranding.WindowTitle;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(900, 640);
        Size = new Size(1000, 680);
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
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 178));
        Controls.Add(root);

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Canvas,
            ColumnCount = 2,
            Padding = new Padding(36, 10, 36, 8)
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var brand = new Panel { Dock = DockStyle.Fill };
        brand.Controls.Add(new PictureBox
        {
            Image = LoadEmbeddedImage(LauncherBranding.LogoResource),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Transparent,
            Dock = DockStyle.Left,
            Width = 220,
            Margin = new Padding(0)
        });
        brand.MouseDown += DragWindow;
        header.Controls.Add(brand, 0, 0);
        var buildBadge = new Label
        {
            Text = LauncherBranding.BuildBadge,
            AutoSize = true,
            ForeColor = Muted,
            BackColor = Surface,
            Font = new Font("Bahnschrift SemiBold", 8.5f),
            Padding = new Padding(12, 7, 12, 7),
            Margin = new Padding(0, 8, 0, 0),
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        var windowTools = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0)
        };
        windowTools.Controls.Add(SocialButton(
            "Discord",
            LauncherBranding.DiscordIconResource,
            LauncherBranding.DiscordUrl));
        windowTools.Controls.Add(SocialButton(
            "Instagram",
            LauncherBranding.InstagramIconResource,
            LauncherBranding.InstagramUrl));
        windowTools.Controls.Add(SocialButton(
            "Facebook",
            LauncherBranding.FacebookIconResource,
            LauncherBranding.FacebookUrl));
        windowTools.Controls.Add(SocialButton(
            "Twitch",
            LauncherBranding.TwitchIconResource,
            LauncherBranding.TwitchUrl));
        windowTools.Controls.Add(buildBadge);
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
            Margin = new Padding(36, 4, 36, 18),
            Padding = new Padding(30)
        };
        var heroLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 2,
            RowCount = 1
        };
        heroLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 66));
        heroLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        hero.Controls.Add(heroLayout);

        var heroCopy = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            RowCount = 4,
            ColumnCount = 1,
            Margin = new Padding(0, 0, 24, 0)
        };
        heroCopy.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        heroCopy.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        heroCopy.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        heroCopy.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        heroCopy.Controls.Add(new Label
        {
            Text = LauncherBranding.HeroEyebrow,
            ForeColor = Gold,
            Font = new Font("Bahnschrift SemiBold", 9f),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 10)
        }, 0, 0);
        heroCopy.Controls.Add(new Label
        {
            Text = LauncherBranding.HeroTitle,
            ForeColor = Color.White,
            Font = new Font("Georgia", 27f, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 8)
        }, 0, 1);
        heroCopy.Controls.Add(new Label
        {
            Text = LauncherBranding.HeroDescription,
            ForeColor = Color.FromArgb(218, 222, 220),
            Font = new Font("Segoe UI", 10.5f),
            AutoSize = true,
            Margin = new Padding(1, 0, 0, 16)
        }, 0, 2);
        _releaseNotes.Dock = DockStyle.Fill;
        _releaseNotes.BackColor = Color.Transparent;
        _releaseNotes.ForeColor = Color.FromArgb(197, 204, 201);
        _releaseNotes.Font = new Font("Segoe UI", 9.5f);
        _releaseNotes.Text = "Buscando la ultima version disponible...";
        _releaseNotes.AutoEllipsis = true;
        _releaseNotes.TextAlign = ContentAlignment.TopLeft;
        _releaseNotes.Margin = new Padding(0);
        heroCopy.Controls.Add(_releaseNotes, 0, 3);
        heroLayout.Controls.Add(heroCopy, 0, 0);

        var heroAction = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(176, 21, 27, 31),
            Padding = new Padding(20),
            RowCount = 4,
            ColumnCount = 2,
            Margin = new Padding(0)
        };
        heroAction.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        heroAction.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        heroAction.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        heroAction.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        heroAction.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        heroAction.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        heroAction.Controls.Add(VersionLabel("INSTALADA"), 0, 0);
        heroAction.Controls.Add(VersionLabel("DISPONIBLE"), 1, 0);
        ConfigureVersionValue(_installedVersion, "Sin instalar");
        ConfigureVersionValue(_availableVersion, "Buscando...");
        heroAction.Controls.Add(_installedVersion, 0, 1);
        heroAction.Controls.Add(_availableVersion, 1, 1);
        StylePrimaryButton(_updateButton, "INSTALAR CLIENTE");
        _updateButton.Dock = DockStyle.Fill;
        _updateButton.Enabled = false;
        _updateButton.Click += async (_, _) => await HandlePrimaryActionAsync();
        heroAction.SetColumnSpan(_updateButton, 2);
        heroAction.Controls.Add(_updateButton, 0, 3);
        StyleCancelButton(_cancelButton);
        _cancelButton.Dock = DockStyle.Fill;
        _cancelButton.Visible = false;
        _cancelButton.Click += (_, _) => CancelCurrentOperation();
        heroAction.SetColumnSpan(_cancelButton, 2);
        heroAction.Controls.Add(_cancelButton, 0, 3);
        heroLayout.Controls.Add(heroAction, 1, 0);
        root.Controls.Add(hero, 0, 1);

        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Surface,
            Padding = new Padding(36, 16, 36, 18),
            ColumnCount = 1,
            RowCount = 3
        };
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var folderRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            Margin = new Padding(0)
        };
        folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        folderRow.Controls.Add(new Label
        {
            Text = "CARPETA DE JUEGO",
            ForeColor = Muted,
            Font = new Font("Bahnschrift SemiBold", 9f),
            AutoSize = true,
            Anchor = AnchorStyles.Left
        }, 0, 0);
        _clientPath.Dock = DockStyle.Fill;
        _clientPath.Font = new Font("Segoe UI", 10f);
        _clientPath.BackColor = SurfaceRaised;
        _clientPath.ForeColor = Cream;
        _clientPath.BorderStyle = BorderStyle.FixedSingle;
        _clientPath.PlaceholderText = @"Elegi una carpeta, por ejemplo C:\Games\Interlude";
        _clientPath.Margin = new Padding(0, 3, 10, 4);
        _clientPath.TextChanged += (_, _) => RefreshInstalledVersion();
        StyleSecondaryButton(_browseButton, "ELEGIR CARPETA");
        _browseButton.Click += (_, _) => BrowseClientDirectory();
        folderRow.Controls.Add(_clientPath, 1, 0);
        folderRow.Controls.Add(_browseButton, 2, 0);
        footer.Controls.Add(folderRow, 0, 0);

        var progressLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0)
        };
        progressLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        progressLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 14));
        _status.Text = "Listo.";
        _status.AutoEllipsis = true;
        _status.Dock = DockStyle.Fill;
        _status.ForeColor = Muted;
        _status.Font = new Font("Segoe UI", 9f);
        _status.Margin = new Padding(0, 3, 0, 5);
        progressLayout.Controls.Add(_status, 0, 0);
        _progress.Dock = DockStyle.Fill;
        _progress.Style = ProgressBarStyle.Continuous;
        _progress.ForeColor = Gold;
        _progress.Margin = new Padding(0);
        progressLayout.Controls.Add(_progress, 0, 1);
        footer.Controls.Add(progressLayout, 0, 1);

        var tools = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = new Padding(0, 8, 0, 0)
        };
        StyleSecondaryButton(_repairButton, "REPARAR");
        _repairButton.Enabled = false;
        _repairButton.Click += async (_, _) => await InstallLatestAsync(repair: true);
        StyleSecondaryButton(_localManifestButton, "INSTALACION MANUAL");
        _localManifestButton.Click += async (_, _) => await InstallLocalManifestAsync();
        StyleSecondaryButton(_checkButton, "BUSCAR ACTUALIZACION");
        _checkButton.Click += async (_, _) => await CheckForUpdatesAsync(showErrors: true);
        tools.Controls.Add(_repairButton);
        tools.Controls.Add(_localManifestButton);
        tools.Controls.Add(_checkButton);
        footer.Controls.Add(tools, 0, 2);
        root.Controls.Add(footer, 0, 2);
    }

    private static Label VersionLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = Muted,
        Font = new Font("Bahnschrift SemiBold", 8.5f),
        Margin = new Padding(0, 0, 0, 2)
    };

    private static void ConfigureVersionValue(Label label, string text)
    {
        label.Text = text;
        label.AutoSize = true;
        label.ForeColor = Cream;
        label.Font = new Font("Bahnschrift SemiBold", 14f);
        label.Margin = new Padding(0);
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
                "Cancela la operacion actual antes de cerrar el launcher.",
                "Operacion en curso",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }
        Close();
    }

    private void LoadSettings()
    {
        var settings = SettingsStore.Load();
        if (!string.IsNullOrWhiteSpace(settings.ClientDirectory))
        {
            _clientPath.Text = settings.ClientDirectory;
            return;
        }

        var executableDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        _clientPath.Text = File.Exists(Path.Combine(executableDirectory, "system-e", "l2.exe"))
            ? executableDirectory
            : string.Empty;
    }

    private bool BrowseClientDirectory()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Elegi donde instalar el cliente completo",
            ShowNewFolderButton = true,
            SelectedPath = Directory.Exists(_clientPath.Text) ? _clientPath.Text : ""
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _clientPath.Text = dialog.SelectedPath;
            SettingsStore.Save(new UpdaterSettings { ClientDirectory = dialog.SelectedPath });
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
            SetBusy(true, "Buscando la ultima version del cliente...");
            _latestRelease = await _releaseClient.GetLatestContentAsync(_operation.Token);
            _availableVersion.Text = _latestRelease.Version;
            _releaseNotes.Text = string.IsNullOrWhiteSpace(_latestRelease.Notes)
                ? _latestRelease.Name
                : _latestRelease.Notes;
            _status.Text = "Cliente disponible. Podes instalar, actualizar o reparar.";
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Busqueda cancelada.";
        }
        catch (Exception error)
        {
            _latestRelease = null;
            _availableVersion.Text = "Sin release";
            _releaseNotes.Text = "No se pudo consultar la release publica.";
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
            SetBusy(true, repair ? "Preparando reparacion..." : "Preparando cliente...");
            var checksum = await _releaseClient.DownloadChecksumAsync(
                _latestRelease.ManifestChecksumUrl,
                _operation.Token);
            await _releaseClient.DownloadAsync(
                _latestRelease.ManifestUrl,
                manifestPath,
                percent => BeginInvoke(() =>
                {
                    _progress.Value = Math.Clamp(percent, 0, 100);
                    _status.Text = $"Descargando manifiesto... {percent}%";
                }),
                _operation.Token);
            await InstallContentManifestAsync(manifestPath, checksum, repair, _operation.Token);
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Operacion cancelada. No se dejaron cambios incompletos.";
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
            Title = "Elegi un manifiesto de cliente",
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
            SetBusy(true, "Verificando manifiesto local...");
            await InstallContentManifestAsync(dialog.FileName, null, false, _operation.Token);
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Operacion cancelada. No se dejaron cambios incompletos.";
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
            ? "No habia archivos anteriores que respaldar."
            : $"Backup: {result.BackupDirectory}";
        MessageBox.Show(
            this,
            $"Cliente {result.ClientVersion} listo.\n\n" +
            $"Archivos instalados: {result.InstalledFiles}\n" +
            $"Paquetes descargados: {result.DownloadedPackages}\n" +
            $"Borrados: {result.DeletedPaths}\n{backupMessage}" +
            (launcherWarning is null ? "" : $"\n\n{launcherWarning}"),
            "Cliente actualizado",
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
        _installedVersion.Text = state?.ClientVersion ?? (clientInstalled ? "Detectado" : "Sin instalar");
        _updateButton.Text = !clientInstalled
            ? "INSTALAR CLIENTE"
            : upToDate || _latestRelease is null
                ? "JUGAR"
                : "ACTUALIZAR";
        _repairButton.Enabled = state is not null && _latestRelease is not null && _operation is null;
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
                "La carpeta no esta vacia. El launcher respaldara los archivos que deba reemplazar. Continuar?",
                "Carpeta existente",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes)
            {
                return false;
            }
        }

        SettingsStore.Save(new UpdaterSettings { ClientDirectory = _clientPath.Text });
        return true;
    }

    private void CancelCurrentOperation()
    {
        if (_operation is null)
        {
            return;
        }
        _cancelButton.Enabled = false;
        _status.Text = "Cancelando...";
        _operation.Cancel();
    }

    private void LaunchGame()
    {
        if (!IsClientInstalled())
        {
            MessageBox.Show(
                this,
                "El cliente todavia no esta instalado.",
                "Cliente no disponible",
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
            return $"No se pudo crear el acceso directo de L2 Hamburgo: {error.Message}";
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
        _browseButton.Enabled = !busy;
        _checkButton.Enabled = !busy;
        _localManifestButton.Enabled = !busy;
        _cancelButton.Enabled = busy;
        _cancelButton.Visible = busy;
        _repairButton.Enabled = !busy && _latestRelease is not null &&
                                StateFiles.ReadContentManifest(_clientPath.Text) is not null;
        _updateButton.Visible = !busy;
        _updateButton.Enabled = !busy && (IsClientInstalled() || _latestRelease is not null);
        RefreshPrimaryButtonColors();
        _clientPath.ReadOnly = busy;
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
            "No se pudo completar la operacion",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    private static Image LoadEmbeddedImage(string resourceName)
    {
        using var stream = typeof(MainForm).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded launcher asset not found: {resourceName}");
        using var source = Image.FromStream(stream);
        return new Bitmap(source);
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
