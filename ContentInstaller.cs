using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace L2TribeLauncher;

internal sealed class ContentInstaller : IDisposable
{
    private const int MaxConcurrentDownloads = 2;
    private const int ProgressReportMilliseconds = 250;
    private static readonly TimeSpan NetworkTimeout = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan ReadIdleTimeout = TimeSpan.FromMinutes(3);
    private readonly HttpClient _httpClient = new();

    public ContentInstaller()
    {
        _httpClient.Timeout = NetworkTimeout;
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("L2TribeLauncher", "0.1"));
    }

    public async Task<ContentInstallResult> InstallAsync(
        string manifestPath,
        string clientDirectory,
        string? expectedManifestSha256,
        bool repair,
        Action<InstallProgress>? report,
        CancellationToken cancellationToken)
    {
        manifestPath = Path.GetFullPath(manifestPath);
        clientDirectory = Path.GetFullPath(clientDirectory);
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("Client content manifest not found.", manifestPath);
        }
        Directory.CreateDirectory(clientDirectory);
        EnsureDirectoryIsWritable(clientDirectory);
        CleanupStaleInstallArtifacts(clientDirectory);

        report?.Invoke(new InstallProgress(
            1,
            "",
            Kind: InstallProgressKind.ValidatingClientManifest));
        var manifestHash = await HashFileAsync(manifestPath, cancellationToken);
        if (!string.IsNullOrWhiteSpace(expectedManifestSha256) &&
            !manifestHash.Equals(NormalizeSha256(expectedManifestSha256), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Client manifest SHA-256 does not match its release checksum.");
        }

        var manifestJson = await File.ReadAllTextAsync(manifestPath, cancellationToken);
        var manifest = JsonSerializer.Deserialize<ContentManifest>(manifestJson, JsonDefaults.Options)
            ?? throw new InvalidDataException("Client content manifest is invalid.");
        ValidateManifest(manifest, clientDirectory);

        var previousManifestPath = Path.Combine(clientDirectory, StateFiles.ContentManifestName);
        var previousManifestJson = File.Exists(previousManifestPath)
            ? await File.ReadAllTextAsync(previousManifestPath, cancellationToken)
            : null;
        var previousManifest = previousManifestJson is null
            ? null
            : JsonSerializer.Deserialize<ContentManifest>(previousManifestJson, JsonDefaults.Options);

        var requiredFiles = repair
            ? await FindFilesNeedingRepairAsync(manifest, clientDirectory, report, cancellationToken)
            : FindFilesNeedingUpdate(manifest, previousManifest, clientDirectory);
        var deletePaths = FindDeletePaths(manifest, previousManifest);
        var packageById = manifest.Packages.ToDictionary(package => package.Id, StringComparer.OrdinalIgnoreCase);
        var requiredPackages = requiredFiles
            .Select(file => packageById[file.PackageId])
            .DistinctBy(package => package.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (requiredFiles.Count == 0 && deletePaths.Count == 0)
        {
            await WriteInstalledManifestAsync(previousManifestPath, manifestJson, cancellationToken);
            report?.Invoke(new InstallProgress(
                100,
                manifest.ClientVersion,
                Kind: InstallProgressKind.ClientUpToDate));
            return new ContentInstallResult(manifest.ClientVersion, 0, 0, 0, null);
        }

        EnsureSufficientDiskSpace(clientDirectory, requiredFiles, requiredPackages, deletePaths);
        var cacheDirectory = Path.Combine(clientDirectory, ".l2-package-cache");
        var stagingDirectory = Path.Combine(clientDirectory, $".l2-content-staging-{Guid.NewGuid():N}");
        var backupDirectory = Path.Combine(clientDirectory, $"_client_backup_{DateTime.Now:yyyyMMdd-HHmmss-fff}");
        Directory.CreateDirectory(cacheDirectory);
        Directory.CreateDirectory(stagingDirectory);

        var affectedPaths = requiredFiles.Select(file => file.Path)
            .Concat(deletePaths)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var originalItems = new List<OriginalItem>();
        var originalDirectories = GetParentDirectories(requiredFiles.Select(file => file.Path))
            .Where(path => Directory.Exists(ResolveClientPath(clientDirectory, path)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var applyStarted = false;

        try
        {
            await DownloadPackagesAsync(
                requiredPackages,
                cacheDirectory,
                report,
                cancellationToken);

            report?.Invoke(new InstallProgress(
                56,
                "",
                Kind: InstallProgressKind.BackingUpFiles));
            foreach (var relativePath in affectedPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var target = ResolveClientPath(clientDirectory, relativePath);
                if (File.Exists(target))
                {
                    BackupFile(target, backupDirectory, relativePath);
                    originalItems.Add(new OriginalItem(relativePath, false));
                }
                else if (Directory.Exists(target))
                {
                    BackupDirectory(target, backupDirectory, relativePath);
                    originalItems.Add(new OriginalItem(relativePath, true));
                }
            }

            applyStarted = true;
            var filesByPackage = requiredFiles
                .GroupBy(file => file.PackageId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
            var installedFiles = 0;
            foreach (var package in requiredPackages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var packagePath = Path.Combine(cacheDirectory, package.FileName);
                var packageStage = Path.Combine(stagingDirectory, package.Id);
                Directory.CreateDirectory(packageStage);
                await ExtractPackageAsync(
                    packagePath,
                    filesByPackage[package.Id],
                    packageStage,
                    cancellationToken);

                foreach (var file in filesByPackage[package.Id])
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var source = ResolveClientPath(packageStage, file.Path);
                    var target = ResolveClientPath(clientDirectory, file.Path);
                    if (Directory.Exists(target))
                    {
                        throw new IOException($"A directory blocks the client file: {file.Path}");
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    var temporaryTarget = target + ".l2content-new";
                    File.Copy(source, temporaryTarget, true);
                    File.Move(temporaryTarget, target, true);
                    installedFiles++;
                    var percent = 58 + (int)Math.Min(37, installedFiles * 37L / requiredFiles.Count);
                    report?.Invoke(new InstallProgress(
                        percent,
                        file.Path,
                        Kind: InstallProgressKind.InstallingFile));
                }

                TryDeleteDirectory(packageStage);
            }

            var deleted = 0;
            foreach (var relativePath in deletePaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var target = ResolveClientPath(clientDirectory, relativePath);
                if (File.Exists(target))
                {
                    File.Delete(target);
                    deleted++;
                }
                else if (Directory.Exists(target))
                {
                    Directory.Delete(target, true);
                    deleted++;
                }
            }

            await WriteInstalledManifestAsync(previousManifestPath, manifestJson, cancellationToken);
            TryDeleteDirectory(backupDirectory);
            TryDeleteDirectory(cacheDirectory);
            report?.Invoke(new InstallProgress(
                100,
                manifest.ClientVersion,
                Kind: InstallProgressKind.ClientInstalled));
            return new ContentInstallResult(
                manifest.ClientVersion,
                installedFiles,
                deleted,
                requiredPackages.Count,
                null);
        }
        catch (Exception installError) when (applyStarted)
        {
            try
            {
                RollBack(
                    clientDirectory,
                    backupDirectory,
                    affectedPaths,
                    originalItems,
                    GetParentDirectories(requiredFiles.Select(file => file.Path)),
                    originalDirectories);
                if (previousManifestJson is null)
                {
                    TryDeleteFile(previousManifestPath);
                }
                else
                {
                    await File.WriteAllTextAsync(
                        previousManifestPath,
                        previousManifestJson,
                        CancellationToken.None);
                }
            }
            catch (Exception rollbackError)
            {
                throw new AggregateException(
                    "Client installation failed and rollback was incomplete.",
                    installError,
                    rollbackError);
            }

            if (installError is OperationCanceledException)
            {
                throw;
            }
            throw new InvalidOperationException(
                "Client installation failed. The previous state was restored.",
                installError);
        }
        finally
        {
            foreach (var file in requiredFiles)
            {
                TryDeleteFile(ResolveClientPath(clientDirectory, file.Path) + ".l2content-new");
            }
            TryDeleteDirectory(stagingDirectory);
        }
    }

    private static void ValidateManifest(ContentManifest manifest, string clientDirectory)
    {
        if (manifest.FormatVersion != 1 || string.IsNullOrWhiteSpace(manifest.ClientVersion))
        {
            throw new InvalidDataException("Unsupported or invalid client content manifest.");
        }
        if (manifest.Files.Count == 0 || manifest.Packages.Count == 0)
        {
            throw new InvalidDataException("Client manifest has no files or packages.");
        }

        ValidateRelativePath(manifest.LaunchExecutable);
        var packageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in manifest.Packages)
        {
            if (!packageIds.Add(package.Id) || package.Size <= 0 || package.FileCount <= 0)
            {
                throw new InvalidDataException($"Invalid or duplicate package: {package.Id}");
            }
            NormalizeSha256(package.Sha256);
            if (Path.GetFileName(package.FileName) != package.FileName || !package.FileName.EndsWith(".zip"))
            {
                throw new InvalidDataException($"Invalid package filename: {package.FileName}");
            }
            if (!Uri.TryCreate(package.DownloadUrl, UriKind.Absolute, out var uri) ||
                uri.Scheme is not ("https" or "http" or "file"))
            {
                throw new InvalidDataException($"Invalid package URL: {package.DownloadUrl}");
            }
        }

        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in manifest.Files)
        {
            ResolveClientPath(clientDirectory, file.Path);
            if (!paths.Add(file.Path) || !packageIds.Contains(file.PackageId) || file.Size < 0)
            {
                throw new InvalidDataException($"Invalid or duplicate client file: {file.Path}");
            }
            NormalizeSha256(file.Sha256);
        }
        foreach (var path in manifest.Deleted)
        {
            ResolveClientPath(clientDirectory, path);
        }
    }

    private static List<ContentFile> FindFilesNeedingUpdate(
        ContentManifest manifest,
        ContentManifest? previousManifest,
        string clientDirectory)
    {
        var previousFiles = previousManifest?.Files.ToDictionary(
            file => file.Path,
            StringComparer.OrdinalIgnoreCase) ?? new Dictionary<string, ContentFile>(StringComparer.OrdinalIgnoreCase);

        return manifest.Files.Where(file =>
        {
            var path = ResolveClientPath(clientDirectory, file.Path);
            if (!File.Exists(path))
            {
                return true;
            }
            if (file.Mutable)
            {
                return false;
            }
            if (new FileInfo(path).Length != file.Size)
            {
                return true;
            }
            return !previousFiles.TryGetValue(file.Path, out var previous) ||
                   !previous.Sha256.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase);
        }).ToList();
    }

    private static async Task<List<ContentFile>> FindFilesNeedingRepairAsync(
        ContentManifest manifest,
        string clientDirectory,
        Action<InstallProgress>? report,
        CancellationToken cancellationToken)
    {
        var required = new List<ContentFile>();
        for (var index = 0; index < manifest.Files.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = manifest.Files[index];
            var path = ResolveClientPath(clientDirectory, file.Path);
            var invalid = !File.Exists(path);
            if (file.Mutable && !invalid)
            {
                continue;
            }
            invalid = invalid || new FileInfo(path).Length != file.Size;
            if (!invalid)
            {
                invalid = !(await HashFileAsync(path, cancellationToken))
                    .Equals(file.Sha256, StringComparison.OrdinalIgnoreCase);
            }
            if (invalid)
            {
                required.Add(file);
            }
            var percent = 2 + (int)Math.Min(8, (index + 1) * 8L / manifest.Files.Count);
            report?.Invoke(new InstallProgress(
                percent,
                file.Path,
                Kind: InstallProgressKind.CheckingFile));
        }
        return required;
    }

    private static List<string> FindDeletePaths(
        ContentManifest manifest,
        ContentManifest? previousManifest)
    {
        var currentPaths = manifest.Files.Select(file => file.Path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return manifest.Deleted
            .Concat(previousManifest?.Files
                .Where(file => !file.Mutable && !currentPaths.Contains(file.Path))
                .Select(file => file.Path) ?? [])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task DownloadPackagesAsync(
        IReadOnlyCollection<ContentPackage> packages,
        string cacheDirectory,
        Action<InstallProgress>? report,
        CancellationToken cancellationToken)
    {
        if (packages.Count == 0)
        {
            return;
        }

        var totalBytes = Math.Max(1, packages.Sum(package => package.Size));
        long completedBytes = 0;
        using var semaphore = new SemaphoreSlim(MaxConcurrentDownloads);
        using var downloadCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var errors = new ConcurrentQueue<Exception>();
        var reportGate = new object();
        var lastReportedPercent = -1;
        var lastReportTicks = 0L;
        var tasks = packages.Select(async package =>
        {
            await semaphore.WaitAsync(downloadCancellation.Token);
            try
            {
                var destination = Path.Combine(cacheDirectory, package.FileName);
                await DownloadPackageAsync(
                    package,
                    destination,
                    delta =>
                    {
                        var completed = Interlocked.Add(ref completedBytes, delta);
                        var percent = 10 + (int)Math.Min(45, completed * 45 / totalBytes);
                        ReportDownloadProgress(percent, completed);
                    },
                    downloadCancellation.Token);
            }
            catch (Exception error)
            {
                errors.Enqueue(error);
                if (!cancellationToken.IsCancellationRequested)
                {
                    await downloadCancellation.CancelAsync();
                }
            }
            finally
            {
                semaphore.Release();
            }
        }).ToArray();

        await Task.WhenAll(tasks);
        if (errors.TryDequeue(out var firstError))
        {
            throw firstError;
        }

        ReportDownloadProgress(55, totalBytes, force: true);

        void ReportDownloadProgress(int percent, long completed, bool force = false)
        {
            if (report is null)
            {
                return;
            }

            var now = Stopwatch.GetTimestamp();
            var elapsedMilliseconds = lastReportTicks == 0
                ? ProgressReportMilliseconds
                : (now - lastReportTicks) * 1000 / Stopwatch.Frequency;
            lock (reportGate)
            {
                if (!force && percent == lastReportedPercent)
                {
                    return;
                }
                if (!force && elapsedMilliseconds < ProgressReportMilliseconds)
                {
                    return;
                }
                lastReportedPercent = percent;
                lastReportTicks = now;
            }

            var safeCompleted = Math.Min(completed, totalBytes);
            report(new InstallProgress(
                percent,
                "Downloading client...",
                force,
                InstallProgressKind.DownloadingClient,
                safeCompleted,
                totalBytes));
        }
    }

    private async Task DownloadPackageAsync(
        ContentPackage package,
        string destination,
        Action<int> reportBytes,
        CancellationToken cancellationToken)
    {
        if (File.Exists(destination) && new FileInfo(destination).Length == package.Size &&
            (await HashFileAsync(destination, cancellationToken)).Equals(package.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            reportBytes((int)Math.Min(int.MaxValue, package.Size));
            return;
        }
        TryDeleteFile(destination);

        var uri = new Uri(package.DownloadUrl);
        if (uri.IsFile)
        {
            await CopyWithProgressAsync(uri.LocalPath, destination, reportBytes, cancellationToken);
        }
        else
        {
            using var response = await _httpClient.GetAsync(
                uri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await CopyStreamWithProgressAsync(source, destination, reportBytes, cancellationToken);
        }

        if (new FileInfo(destination).Length != package.Size ||
            !(await HashFileAsync(destination, cancellationToken)).Equals(package.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            TryDeleteFile(destination);
            throw new InvalidDataException($"Package verification failed: {package.FileName}");
        }
    }

    private static async Task CopyWithProgressAsync(
        string sourcePath,
        string destination,
        Action<int> reportBytes,
        CancellationToken cancellationToken)
    {
        await using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        await CopyStreamWithProgressAsync(source, destination, reportBytes, cancellationToken);
    }

    private static async Task CopyStreamWithProgressAsync(
        Stream source,
        string destination,
        Action<int> reportBytes,
        CancellationToken cancellationToken)
    {
        await using var target = new FileStream(
            destination,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var buffer = new byte[1024 * 1024];
        int read;
        while ((read = await ReadWithIdleTimeoutAsync(source, buffer.AsMemory(), cancellationToken)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            reportBytes(read);
        }
    }

    private static async Task<int> ReadWithIdleTimeoutAsync(
        Stream source,
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        using var idleCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        idleCancellation.CancelAfter(ReadIdleTimeout);
        try
        {
            return await source.ReadAsync(buffer, idleCancellation.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new IOException(
                $"Download stalled for more than {ReadIdleTimeout.TotalMinutes:0} minutes.");
        }
    }

    private static async Task ExtractPackageAsync(
        string packagePath,
        IReadOnlyCollection<ContentFile> files,
        string stagingDirectory,
        CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var entries = archive.Entries.Where(entry => !string.IsNullOrEmpty(entry.Name))
            .ToDictionary(entry => entry.FullName, StringComparer.OrdinalIgnoreCase);
        var buffer = new byte[1024 * 1024];
        foreach (var file in files)
        {
            if (!entries.TryGetValue(file.Path, out var entry) || entry.Length != file.Size)
            {
                throw new InvalidDataException($"Package entry is missing or invalid: {file.Path}");
            }
            var destination = ResolveClientPath(stagingDirectory, file.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using var source = entry.Open();
            await using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            int read;
            while ((read = await source.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                hash.AppendData(buffer, 0, read);
            }
            var actualHash = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            if (!actualHash.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Package entry SHA-256 mismatch: {file.Path}");
            }
        }
    }

    private static void EnsureSufficientDiskSpace(
        string clientDirectory,
        IReadOnlyCollection<ContentFile> files,
        IReadOnlyCollection<ContentPackage> packages,
        IReadOnlyCollection<string> deletePaths)
    {
        var downloadBytes = packages.Sum(package => package.Size);
        var installBytes = files.Where(file => !File.Exists(ResolveClientPath(clientDirectory, file.Path)))
            .Sum(file => file.Size);
        var backupBytes = files.Select(file => file.Path).Concat(deletePaths)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Sum(path => GetExistingPathSize(ResolveClientPath(clientDirectory, path)));
        var largestPackage = packages.Count == 0 ? 0 : packages.Max(package => package.UncompressedSize);
        var required = downloadBytes + installBytes + backupBytes + largestPackage + 256L * 1024 * 1024;
        var drive = new DriveInfo(Path.GetPathRoot(clientDirectory)!);
        if (drive.AvailableFreeSpace < required)
        {
            throw new IOException(
                $"Not enough free space. Required: {FormatBytes(required)}; " +
                $"available: {FormatBytes(drive.AvailableFreeSpace)}.");
        }
    }

    private static void EnsureDirectoryIsWritable(string directory)
    {
        var probe = Path.Combine(directory, $".l2-write-test-{Guid.NewGuid():N}");
        try
        {
            File.WriteAllText(probe, "test");
        }
        catch (Exception error)
        {
            throw new UnauthorizedAccessException("The launcher cannot write to the installation folder.", error);
        }
        finally
        {
            TryDeleteFile(probe);
        }
    }

    private static void CleanupStaleInstallArtifacts(string clientDirectory)
    {
        foreach (var pattern in new[] { ".l2-content-staging-*", "_client_backup_*", ".l2-package-cache" })
        {
            foreach (var directory in Directory.EnumerateDirectories(clientDirectory, pattern, SearchOption.TopDirectoryOnly))
            {
                TryDeleteDirectory(directory);
            }
        }
    }

    private static async Task WriteInstalledManifestAsync(
        string destination,
        string manifestJson,
        CancellationToken cancellationToken)
    {
        var temporary = destination + ".new";
        await File.WriteAllTextAsync(temporary, manifestJson, cancellationToken);
        File.Move(temporary, destination, true);
    }

    private static string ResolveClientPath(string root, string relativePath)
    {
        ValidateRelativePath(relativePath);
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var systemPath = relativePath.Replace('/', Path.DirectorySeparatorChar);
        var resolved = Path.GetFullPath(Path.Combine(normalizedRoot, systemPath));
        if (!resolved.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Path escapes the client directory: {relativePath}");
        }

        var current = normalizedRoot.TrimEnd(Path.DirectorySeparatorChar);
        foreach (var segment in systemPath.Split(Path.DirectorySeparatorChar))
        {
            current = Path.Combine(current, segment);
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException($"Client path contains a link or junction: {relativePath}");
            }
        }
        return resolved;
    }

    private static void ValidateRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains(':') || path.Contains('\\'))
        {
            throw new InvalidDataException($"Invalid client-relative path: {path}");
        }
        if (path.Split('/').Any(segment => segment is "" or "." or ".."))
        {
            throw new InvalidDataException($"Unsafe client-relative path: {path}");
        }
    }

    private static string NormalizeSha256(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length != 64 || !normalized.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException("Invalid SHA-256 value in client manifest.");
        }
        return normalized;
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
    }

    private static long GetExistingPathSize(string path)
    {
        if (File.Exists(path))
        {
            return new FileInfo(path).Length;
        }
        return Directory.Exists(path)
            ? Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Sum(file => new FileInfo(file).Length)
            : 0;
    }

    private static string FormatBytes(long bytes) => $"{bytes / 1024d / 1024d / 1024d:0.00} GB";

    private static void BackupFile(string source, string backupRoot, string relativePath)
    {
        var destination = ResolveClientPath(backupRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, true);
    }

    private static void BackupDirectory(string source, string backupRoot, string relativePath) =>
        CopyDirectory(source, ResolveClientPath(backupRoot, relativePath));

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
        }
        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }

    private static void RollBack(
        string clientDirectory,
        string backupDirectory,
        IReadOnlyCollection<string> affectedPaths,
        IReadOnlyCollection<OriginalItem> originals,
        IReadOnlyCollection<string> parentDirectories,
        IReadOnlySet<string> originalDirectories)
    {
        foreach (var relativePath in affectedPaths.OrderByDescending(path => path.Length))
        {
            var target = ResolveClientPath(clientDirectory, relativePath);
            TryDeleteFile(target);
            TryDeleteDirectory(target);
        }
        foreach (var original in originals.OrderBy(item => item.RelativePath.Length))
        {
            var source = ResolveClientPath(backupDirectory, original.RelativePath);
            var target = ResolveClientPath(clientDirectory, original.RelativePath);
            if (original.IsDirectory)
            {
                CopyDirectory(source, target);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(source, target, true);
            }
        }
        foreach (var relativePath in parentDirectories.OrderByDescending(path => path.Length))
        {
            var directory = ResolveClientPath(clientDirectory, relativePath);
            if (!originalDirectories.Contains(relativePath) && Directory.Exists(directory) &&
                !Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory);
            }
        }
    }

    private static IReadOnlyCollection<string> GetParentDirectories(IEnumerable<string> paths)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            var current = path.LastIndexOf('/');
            while (current > 0)
            {
                var parent = path[..current];
                result.Add(parent);
                current = parent.LastIndexOf('/');
            }
        }
        return result;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch { }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
        catch { }
    }

    public void Dispose() => _httpClient.Dispose();

    private sealed record OriginalItem(string RelativePath, bool IsDirectory);
}
