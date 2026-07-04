using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace L2TribeLauncher;

internal sealed class PackageInstaller
{
    public async Task<InstallResult> InstallAsync(
        string packagePath,
        string clientDirectory,
        string? expectedPackageSha256,
        Action<InstallProgress>? report,
        CancellationToken cancellationToken)
    {
        packagePath = Path.GetFullPath(packagePath);
        clientDirectory = Path.GetFullPath(clientDirectory);
        if (!File.Exists(packagePath))
        {
            throw new FileNotFoundException("Patch package not found.", packagePath);
        }
        if (!Directory.Exists(clientDirectory))
        {
            throw new DirectoryNotFoundException($"Client directory not found: {clientDirectory}");
        }
        EnsureClientDirectoryIsWritable(clientDirectory);

        report?.Invoke(new InstallProgress(1, "Verifying package..."));
        var packageSha256 = await HashFileAsync(packagePath, cancellationToken);
        if (!string.IsNullOrWhiteSpace(expectedPackageSha256) &&
            !packageSha256.Equals(NormalizeSha256(expectedPackageSha256), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The downloaded package SHA-256 does not match its release checksum.");
        }

        using var archive = ZipFile.OpenRead(packagePath);
        var manifest = await ReadManifestAsync(archive, cancellationToken);
        ValidateManifest(manifest, archive, clientDirectory);

        var stagingDirectory = Path.Combine(clientDirectory, $".l2-updater-staging-{Guid.NewGuid():N}");
        var backupDirectory = Path.Combine(clientDirectory, $"_patch_backup_{DateTime.Now:yyyyMMdd-HHmmss-fff}");
        var originals = new List<OriginalItem>();
        var affectedPaths = manifest.Files.Select(file => file.Path)
            .Concat(manifest.Delete)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        EnsureSufficientDiskSpace(clientDirectory, manifest, affectedPaths);
        var payloadParentDirectories = GetParentDirectories(manifest.Files.Select(file => file.Path));
        var existingParentDirectories = payloadParentDirectories
            .Where(path => Directory.Exists(ResolveClientPath(clientDirectory, path)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var installedStatePath = Path.Combine(clientDirectory, StateFiles.InstalledStateName);
        byte[]? previousInstalledState = File.Exists(installedStatePath)
            ? await File.ReadAllBytesAsync(installedStatePath, cancellationToken)
            : null;
        var applyStarted = false;

        try
        {
            Directory.CreateDirectory(stagingDirectory);
            await ExtractAndVerifyAsync(
                archive,
                manifest,
                stagingDirectory,
                report,
                cancellationToken);

            report?.Invoke(new InstallProgress(72, "Backing up the current client files..."));
            foreach (var relativePath in affectedPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var target = ResolveClientPath(clientDirectory, relativePath);
                if (File.Exists(target))
                {
                    BackupFile(target, backupDirectory, relativePath);
                    originals.Add(new OriginalItem(relativePath, false));
                }
                else if (Directory.Exists(target))
                {
                    BackupDirectory(target, backupDirectory, relativePath);
                    originals.Add(new OriginalItem(relativePath, true));
                }
            }

            applyStarted = true;
            var copied = 0;
            foreach (var file in manifest.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var source = ResolveClientPath(stagingDirectory, file.Path);
                var target = ResolveClientPath(clientDirectory, file.Path);
                if (Directory.Exists(target))
                {
                    throw new IOException($"A directory blocks the patch file: {file.Path}");
                }

                var targetDirectory = Path.GetDirectoryName(target)
                    ?? throw new InvalidDataException($"Invalid target path: {file.Path}");
                Directory.CreateDirectory(targetDirectory);
                var temporaryTarget = target + ".l2updater-new";
                File.Copy(source, temporaryTarget, true);
                File.Move(temporaryTarget, target, true);
                copied++;

                var percent = 72 + (int)Math.Min(20, copied * 20L / manifest.Files.Count);
                report?.Invoke(new InstallProgress(percent, $"Installing {file.Path}"));
            }

            var deleted = 0;
            foreach (var relativePath in manifest.Delete)
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

            var state = new InstalledState
            {
                PatchVersion = manifest.PatchVersion,
                InstalledUtc = DateTimeOffset.UtcNow,
                PackageSha256 = packageSha256
            };
            var stateJson = JsonSerializer.Serialize(state, JsonDefaults.Options);
            await File.WriteAllTextAsync(installedStatePath, stateJson, cancellationToken);

            if (originals.Count == 0 && Directory.Exists(backupDirectory))
            {
                Directory.Delete(backupDirectory, true);
            }

            report?.Invoke(new InstallProgress(100, $"Patch {manifest.PatchVersion} installed."));
            return new InstallResult(
                manifest.PatchVersion,
                packageSha256,
                originals.Count > 0 ? backupDirectory : null,
                copied,
                deleted);
        }
        catch (Exception installError) when (applyStarted)
        {
            try
            {
                RollBack(
                    clientDirectory,
                    backupDirectory,
                    affectedPaths,
                    originals,
                    payloadParentDirectories,
                    existingParentDirectories);
                if (previousInstalledState is null)
                {
                    File.Delete(installedStatePath);
                }
                else
                {
                    await File.WriteAllBytesAsync(installedStatePath, previousInstalledState, CancellationToken.None);
                }
            }
            catch (Exception rollbackError)
            {
                throw new AggregateException(
                    "Installation failed and rollback could not fully restore the client.",
                    installError,
                    rollbackError);
            }

            throw new InvalidOperationException(
                "Installation failed. The previous client state was restored.",
                installError);
        }
        finally
        {
            foreach (var file in manifest.Files)
            {
                var temporaryTarget = ResolveClientPath(clientDirectory, file.Path) + ".l2updater-new";
                TryDeleteFile(temporaryTarget);
            }
            TryDeleteDirectory(stagingDirectory);
        }
    }

    private static async Task<PatchManifest> ReadManifestAsync(
        ZipArchive archive,
        CancellationToken cancellationToken)
    {
        var entry = archive.GetEntry("manifest.json")
            ?? throw new InvalidDataException("Package does not contain manifest.json.");
        await using var stream = entry.Open();
        return await JsonSerializer.DeserializeAsync<PatchManifest>(
                   stream,
                   JsonDefaults.Options,
                   cancellationToken)
               ?? throw new InvalidDataException("manifest.json is empty or invalid.");
    }

    private static void ValidateManifest(
        PatchManifest manifest,
        ZipArchive archive,
        string clientDirectory)
    {
        if (manifest.FormatVersion != 1)
        {
            throw new InvalidDataException($"Unsupported manifest format: {manifest.FormatVersion}");
        }
        if (string.IsNullOrWhiteSpace(manifest.PatchVersion))
        {
            throw new InvalidDataException("Patch version is missing.");
        }
        if (!manifest.PayloadRoot.Equals("files", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Unsupported payload root.");
        }
        if (manifest.Files.Count == 0)
        {
            throw new InvalidDataException("Patch contains no files.");
        }

        var filePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in manifest.Files)
        {
            ValidateRelativePath(file.Path);
            if (!filePaths.Add(file.Path))
            {
                throw new InvalidDataException($"Duplicate patch path: {file.Path}");
            }
            if (file.Size < 0 || file.Sha256.Length != 64 || !file.Sha256.All(Uri.IsHexDigit))
            {
                throw new InvalidDataException($"Invalid size or SHA-256 for: {file.Path}");
            }
        }

        var deletePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var relativePath in manifest.Delete)
        {
            ValidateRelativePath(relativePath);
            if (!deletePaths.Add(relativePath))
            {
                throw new InvalidDataException($"Duplicate delete path: {relativePath}");
            }
            var prefix = relativePath.TrimEnd('/') + "/";
            if (filePaths.Contains(relativePath) || filePaths.Any(path => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidDataException($"Delete path overlaps the patch payload: {relativePath}");
            }
        }

        var archiveEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries.Where(item => !string.IsNullOrEmpty(item.Name)))
        {
            if (!archiveEntries.Add(entry.FullName))
            {
                throw new InvalidDataException($"Duplicate ZIP entry: {entry.FullName}");
            }
        }
        foreach (var file in manifest.Files)
        {
            if (!archiveEntries.Contains($"files/{file.Path}"))
            {
                throw new InvalidDataException($"Payload file is missing from ZIP: {file.Path}");
            }
        }

        foreach (var requiredPath in manifest.BaseClient.RequiredPaths)
        {
            var target = ResolveClientPath(clientDirectory, requiredPath);
            if (!File.Exists(target) && !Directory.Exists(target))
            {
                throw new InvalidDataException(
                    $"The selected folder is not a compatible Grand Crusade client. Missing: {requiredPath}");
            }
        }

        ResolveClientPath(clientDirectory, manifest.LaunchExecutable);
    }

    private static async Task ExtractAndVerifyAsync(
        ZipArchive archive,
        PatchManifest manifest,
        string stagingDirectory,
        Action<InstallProgress>? report,
        CancellationToken cancellationToken)
    {
        var totalBytes = Math.Max(1, manifest.Files.Sum(file => file.Size));
        long completedBytes = 0;
        var buffer = new byte[1024 * 1024];

        foreach (var file in manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = archive.GetEntry($"files/{file.Path}")
                ?? throw new InvalidDataException($"Payload file is missing: {file.Path}");
            if (entry.Length != file.Size)
            {
                throw new InvalidDataException($"Payload size mismatch: {file.Path}");
            }

            var destination = ResolveClientPath(stagingDirectory, file.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using var source = entry.Open();
            await using var output = new FileStream(
                destination,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                buffer.Length,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            int read;
            while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                hash.AppendData(buffer, 0, read);
                completedBytes += read;
                var percent = 3 + (int)Math.Min(67, completedBytes * 67 / totalBytes);
                report?.Invoke(new InstallProgress(percent, $"Verifying {file.Path}"));
            }

            var actualHash = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            if (!actualHash.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Payload SHA-256 mismatch: {file.Path}");
            }
        }
    }

    private static string ResolveClientPath(string root, string relativePath)
    {
        ValidateRelativePath(relativePath);
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var relativeSystemPath = relativePath.Replace('/', Path.DirectorySeparatorChar);
        var resolved = Path.GetFullPath(Path.Combine(normalizedRoot, relativeSystemPath));
        if (!resolved.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Path escapes the client directory: {relativePath}");
        }
        EnsurePathContainsNoReparsePoints(normalizedRoot, relativeSystemPath, relativePath);
        return resolved;
    }

    private static void EnsurePathContainsNoReparsePoints(
        string normalizedRoot,
        string relativeSystemPath,
        string displayPath)
    {
        var current = normalizedRoot.TrimEnd(Path.DirectorySeparatorChar);
        foreach (var segment in relativeSystemPath.Split(Path.DirectorySeparatorChar))
        {
            current = Path.Combine(current, segment);
            if (!File.Exists(current) && !Directory.Exists(current))
            {
                continue;
            }

            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException($"Client path contains a link or junction: {displayPath}");
            }
        }
    }

    private static void ValidateRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) ||
            Path.IsPathRooted(relativePath) ||
            relativePath.Contains(':') ||
            relativePath.Contains('\\'))
        {
            throw new InvalidDataException($"Invalid client-relative path: {relativePath}");
        }

        var segments = relativePath.Split('/');
        if (segments.Any(segment => segment is "" or "." or ".."))
        {
            throw new InvalidDataException($"Unsafe client-relative path: {relativePath}");
        }
    }

    private static string NormalizeSha256(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length != 64 || !normalized.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException("Release checksum is not a valid SHA-256 value.");
        }
        return normalized;
    }

    private static void EnsureClientDirectoryIsWritable(string clientDirectory)
    {
        var probe = Path.Combine(clientDirectory, $".l2-updater-write-test-{Guid.NewGuid():N}");
        try
        {
            File.WriteAllText(probe, "write-test");
        }
        catch (Exception error)
        {
            throw new UnauthorizedAccessException(
                "The updater cannot write to the selected client directory.",
                error);
        }
        finally
        {
            TryDeleteFile(probe);
        }
    }

    private static void EnsureSufficientDiskSpace(
        string clientDirectory,
        PatchManifest manifest,
        IReadOnlyCollection<string> affectedPaths)
    {
        var payloadBytes = manifest.Files.Sum(file => file.Size);
        var backupBytes = affectedPaths.Sum(path => GetExistingPathSize(ResolveClientPath(clientDirectory, path)));
        var largestFile = manifest.Files.Max(file => file.Size);
        const long safetyMargin = 128L * 1024 * 1024;
        var requiredBytes = payloadBytes + backupBytes + largestFile + safetyMargin;

        var root = Path.GetPathRoot(clientDirectory)
            ?? throw new IOException("Could not determine the client drive.");
        var drive = new DriveInfo(root);
        if (drive.AvailableFreeSpace < requiredBytes)
        {
            throw new IOException(
                $"Not enough free space. Required: {FormatBytes(requiredBytes)}; " +
                $"available: {FormatBytes(drive.AvailableFreeSpace)}.");
        }
    }

    private static long GetExistingPathSize(string path)
    {
        if (File.Exists(path))
        {
            return new FileInfo(path).Length;
        }
        if (!Directory.Exists(path))
        {
            return 0;
        }

        return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
            .Sum(file => new FileInfo(file).Length);
    }

    private static string FormatBytes(long bytes) => $"{bytes / 1024d / 1024d / 1024d:0.00} GB";

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void BackupFile(string source, string backupRoot, string relativePath)
    {
        var destination = ResolveClientPath(backupRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, true);
    }

    private static void BackupDirectory(string source, string backupRoot, string relativePath)
    {
        var destination = ResolveClientPath(backupRoot, relativePath);
        CopyDirectory(source, destination);
    }

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
        IReadOnlyCollection<string> payloadParentDirectories,
        IReadOnlySet<string> existingParentDirectories)
    {
        foreach (var relativePath in affectedPaths.OrderByDescending(path => path.Length))
        {
            var target = ResolveClientPath(clientDirectory, relativePath);
            if (File.Exists(target))
            {
                File.Delete(target);
            }
            else if (Directory.Exists(target))
            {
                Directory.Delete(target, true);
            }
        }

        foreach (var original in originals.OrderBy(item => item.RelativePath.Length))
        {
            var backup = ResolveClientPath(backupDirectory, original.RelativePath);
            var target = ResolveClientPath(clientDirectory, original.RelativePath);
            if (original.IsDirectory)
            {
                CopyDirectory(backup, target);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(backup, target, true);
            }
        }

        foreach (var relativePath in payloadParentDirectories.OrderByDescending(path => path.Length))
        {
            if (existingParentDirectories.Contains(relativePath))
            {
                continue;
            }

            var directory = ResolveClientPath(clientDirectory, relativePath);
            if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory);
            }
        }
    }

    private static IReadOnlyCollection<string> GetParentDirectories(IEnumerable<string> filePaths)
    {
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var filePath in filePaths)
        {
            var current = filePath.LastIndexOf('/');
            while (current > 0)
            {
                var parent = filePath[..current];
                directories.Add(parent);
                current = parent.LastIndexOf('/');
            }
        }
        return directories;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Cleanup must not hide the installation or rollback result.
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch
        {
            // A stale staging directory is safer than reporting a false install failure.
        }
    }

    private sealed record OriginalItem(string RelativePath, bool IsDirectory);
}
