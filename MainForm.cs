using System.Diagnostics;
using System.Text.Json;

namespace L2InterludeUpdater;

internal sealed class MainForm : Form
{
    private static readonly Color Ink = Color.FromArgb(45, 37, 29);
    private static readonly Color Paper = Color.FromArgb(244, 239, 226);
    private static readonly Color Gold = Color.FromArgb(183, 132, 48);
    private static readonly Color Moss = Color.FromArgb(53, 111, 83);
    private static readonly Color Muted = Color.FromArgb(111, 101, 87);

    private readonly TextBox _clientPath = new();
    private readonly Label _installedVersion = new();
    private readonly Label _availableVersion = new();
    private readonly Label _status = new();
    private readonly RichTextBox _releaseNotes = new();
    private readonly ProgressBar _progress = new();
    private readonly Button _browseButton = new();
    private readonly Button _checkButton = new();
    private readonly Button _updateButton = new();
    private readonly Button _repairButton = new();
    private readonly Button _localManifestButton = new();
    private readonly Button _playButton = new();
    private readonly GitHubReleaseClient _releaseClient = new();
    private readonly ContentInstaller _contentInstaller = new();
    private ContentReleaseInfo? _latestRelease;
    private CancellationTokenSource? _operation;

    public MainForm()
    {
        Text = "Interlude Launcher";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(800, 600);
        Size = new Size(920, 690);
        BackColor = Paper;
        ForeColor = Ink;
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
        };
    }

    private void BuildLayout()
    {
        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 112,
            BackColor = Ink,
            Padding = new Padding(28, 20, 28, 16)
        };
        header.Controls.Add(new Label
        {
            Text = "INTERLUDE LAUNCHER",
            ForeColor = Color.FromArgb(232, 195, 117),
            Font = new Font("Bahnschrift SemiBold", 22f),
            AutoSize = true,
            Location = new Point(26, 18)
        });
        header.Controls.Add(new Label
        {
            Text = "Install, update, repair and play",
            ForeColor = Color.FromArgb(211, 203, 188),
            Font = new Font("Georgia", 10.5f, FontStyle.Italic),
            AutoSize = true,
            Location = new Point(29, 65)
        });
        Controls.Add(header);

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(28, 22, 28, 24),
            ColumnCount = 1,
            RowCount = 6
        };
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(content);

        content.Controls.Add(new Label
        {
            Text = "CARPETA DE INSTALACION",
            Font = new Font("Bahnschrift SemiBold", 9.5f),
            ForeColor = Muted,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 6)
        }, 0, 0);

        var folderRow = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            Margin = new Padding(0, 0, 0, 16)
        };
        folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _clientPath.Dock = DockStyle.Fill;
        _clientPath.Font = new Font("Segoe UI", 10f);
        _clientPath.Margin = new Padding(0, 0, 8, 0);
        _clientPath.TextChanged += (_, _) => RefreshInstalledVersion();
        StyleSecondaryButton(_browseButton, "Elegir carpeta");
        _browseButton.Click += (_, _) => BrowseClientDirectory();
        folderRow.Controls.Add(_clientPath, 0, 0);
        folderRow.Controls.Add(_browseButton, 1, 0);
        content.Controls.Add(folderRow, 0, 1);

        var card = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(251, 248, 239),
            Padding = new Padding(22),
            Margin = new Padding(0, 0, 0, 16)
        };
        content.Controls.Add(card, 0, 2);
        var cardLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3
        };
        cardLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        cardLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        cardLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        cardLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        cardLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        card.Controls.Add(cardLayout);

        cardLayout.Controls.Add(VersionLabel("INSTALADA"), 0, 0);
        cardLayout.Controls.Add(VersionLabel("DISPONIBLE"), 1, 0);
        ConfigureVersionValue(_installedVersion, "Sin instalar");
        ConfigureVersionValue(_availableVersion, "Buscando...");
        cardLayout.Controls.Add(_installedVersion, 0, 1);
        cardLayout.Controls.Add(_availableVersion, 1, 1);

        _releaseNotes.Dock = DockStyle.Fill;
        _releaseNotes.ReadOnly = true;
        _releaseNotes.BorderStyle = BorderStyle.None;
        _releaseNotes.BackColor = card.BackColor;
        _releaseNotes.ForeColor = Ink;
        _releaseNotes.Font = new Font("Georgia", 10f);
        _releaseNotes.Text = "Las novedades de la proxima version apareceran aca.";
        _releaseNotes.Margin = new Padding(0, 18, 0, 0);
        cardLayout.SetColumnSpan(_releaseNotes, 2);
        cardLayout.Controls.Add(_releaseNotes, 0, 2);

        _status.Text = "Listo.";
        _status.AutoSize = true;
        _status.ForeColor = Muted;
        _status.Margin = new Padding(0, 0, 0, 6);
        content.Controls.Add(_status, 0, 3);

        _progress.Dock = DockStyle.Top;
        _progress.Height = 12;
        _progress.Style = ProgressBarStyle.Continuous;
        _progress.Margin = new Padding(0, 0, 0, 18);
        content.Controls.Add(_progress, 0, 4);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = true,
            Margin = new Padding(0)
        };
        StylePrimaryButton(_updateButton, "Instalar cliente");
        _updateButton.Enabled = false;
        _updateButton.Click += async (_, _) => await InstallLatestAsync(repair: false);
        StyleSecondaryButton(_playButton, "Jugar");
        _playButton.Click += (_, _) => LaunchGame();
        StyleSecondaryButton(_repairButton, "Reparar");
        _repairButton.Enabled = false;
        _repairButton.Click += async (_, _) => await InstallLatestAsync(repair: true);
        StyleSecondaryButton(_localManifestButton, "Manifiesto local");
        _localManifestButton.Click += async (_, _) => await InstallLocalManifestAsync();
        StyleSecondaryButton(_checkButton, "Buscar actualizacion");
        _checkButton.Click += async (_, _) => await CheckForUpdatesAsync(showErrors: true);
        actions.Controls.Add(_updateButton);
        actions.Controls.Add(_playButton);
        actions.Controls.Add(_repairButton);
        actions.Controls.Add(_localManifestButton);
        actions.Controls.Add(_checkButton);
        content.Controls.Add(actions, 0, 5);
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
        label.ForeColor = Ink;
        label.Font = new Font("Bahnschrift SemiBold", 16f);
        label.Margin = new Padding(0);
    }

    private static void StylePrimaryButton(Button button, string text)
    {
        button.Text = text;
        button.AutoSize = true;
        button.Height = 38;
        button.Padding = new Padding(14, 5, 14, 5);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = Moss;
        button.ForeColor = Color.White;
        button.Font = new Font("Bahnschrift SemiBold", 9.5f);
        button.Margin = new Padding(8, 0, 0, 0);
    }

    private static void StyleSecondaryButton(Button button, string text)
    {
        button.Text = text;
        button.AutoSize = true;
        button.Height = 34;
        button.Padding = new Padding(10, 3, 10, 3);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = Gold;
        button.FlatAppearance.BorderSize = 1;
        button.BackColor = Paper;
        button.ForeColor = Ink;
        button.Margin = new Padding(8, 0, 0, 0);
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
            : @"C:\Games\Interlude";
    }

    private void BrowseClientDirectory()
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
        }
    }

    private async Task CheckForUpdatesAsync(bool showErrors)
    {
        if (_operation is not null)
        {
            return;
        }

        try
        {
            SetBusy(true, "Buscando la ultima version del cliente...");
            _operation = new CancellationTokenSource();
            _latestRelease = await _releaseClient.GetLatestContentAsync(_operation.Token);
            _availableVersion.Text = _latestRelease.Version;
            _releaseNotes.Text = string.IsNullOrWhiteSpace(_latestRelease.Notes)
                ? _latestRelease.Name
                : _latestRelease.Notes;
            _status.Text = "Cliente disponible. Podes instalar, actualizar o reparar.";
        }
        catch (Exception error)
        {
            _latestRelease = null;
            _availableVersion.Text = "No publicada";
            _releaseNotes.Text = "Todavia podes probar el launcher con un manifiesto local.";
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
            SetBusy(true, repair ? "Preparando reparacion..." : "Preparando cliente...");
            _operation = new CancellationTokenSource();
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
            SetBusy(true, "Verificando manifiesto local...");
            _operation = new CancellationTokenSource();
            await InstallContentManifestAsync(dialog.FileName, null, false, _operation.Token);
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

        var backupMessage = result.BackupDirectory is null
            ? "No habia archivos anteriores que respaldar."
            : $"Backup: {result.BackupDirectory}";
        MessageBox.Show(
            this,
            $"Cliente {result.ClientVersion} listo.\n\n" +
            $"Archivos instalados: {result.InstalledFiles}\n" +
            $"Paquetes descargados: {result.DownloadedPackages}\n" +
            $"Borrados: {result.DeletedPaths}\n{backupMessage}",
            "Cliente actualizado",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void RefreshInstalledVersion()
    {
        var state = Directory.Exists(_clientPath.Text)
            ? StateFiles.ReadContentManifest(_clientPath.Text)
            : null;
        _installedVersion.Text = state?.ClientVersion ?? "Sin instalar";
        _updateButton.Text = state is null ? "Instalar cliente" : "Actualizar";
        _playButton.Enabled = IsClientInstalled();
        _repairButton.Enabled = state is not null && _latestRelease is not null && _operation is null;
        _updateButton.Enabled = _latestRelease is not null && HasInstallPath() && _operation is null;
    }

    private bool IsClientInstalled() =>
        File.Exists(Path.Combine(_clientPath.Text, "system-e", "l2.exe"));

    private bool HasInstallPath() => !string.IsNullOrWhiteSpace(_clientPath.Text);

    private bool EnsureInstallDirectory()
    {
        if (!HasInstallPath())
        {
            MessageBox.Show(
                this,
                "Elegi una carpeta de instalacion.",
                "Falta la carpeta",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return false;
        }

        Directory.CreateDirectory(_clientPath.Text);
        var hasState = StateFiles.ReadContentManifest(_clientPath.Text) is not null;
        if (!hasState && Directory.EnumerateFileSystemEntries(_clientPath.Text).Any())
        {
            var answer = MessageBox.Show(
                this,
                "La carpeta no esta vacia. El launcher respaldara los archivos que deba reemplazar. ¿Continuar?",
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
        var executable = Path.Combine(_clientPath.Text, "system-e", "l2.exe");
        Process.Start(new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = true
        });
    }

    private void SetBusy(bool busy, string? message = null)
    {
        _browseButton.Enabled = !busy;
        _checkButton.Enabled = !busy;
        _localManifestButton.Enabled = !busy;
        _playButton.Enabled = !busy && IsClientInstalled();
        _repairButton.Enabled = !busy && _latestRelease is not null &&
                                StateFiles.ReadContentManifest(_clientPath.Text) is not null;
        _updateButton.Enabled = !busy && _latestRelease is not null && HasInstallPath();
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
