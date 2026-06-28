namespace L2InterludeUpdater;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--install-content", StringComparer.OrdinalIgnoreCase))
        {
            return RunContentCommandLineAsync(args).GetAwaiter().GetResult();
        }
        if (args.Contains("--apply-package", StringComparer.OrdinalIgnoreCase))
        {
            return RunCommandLineAsync(args).GetAwaiter().GetResult();
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
    }

    private static async Task<int> RunCommandLineAsync(string[] args)
    {
        string? package = null;
        string? client = null;
        string? checksum = null;
        string? logPath = null;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index].ToLowerInvariant())
            {
                case "--apply-package":
                    package = ReadValue(args, ref index);
                    break;
                case "--client":
                    client = ReadValue(args, ref index);
                    break;
                case "--sha256":
                    checksum = ReadValue(args, ref index);
                    break;
                case "--log":
                    logPath = ReadValue(args, ref index);
                    break;
            }
        }

        void Log(string message)
        {
            Console.WriteLine(message);
            if (!string.IsNullOrWhiteSpace(logPath))
            {
                var fullLogPath = Path.GetFullPath(logPath);
                Directory.CreateDirectory(Path.GetDirectoryName(fullLogPath)!);
                File.AppendAllText(fullLogPath, $"{DateTimeOffset.Now:o} {message}{Environment.NewLine}");
            }
        }

        if (string.IsNullOrWhiteSpace(package) || string.IsNullOrWhiteSpace(client))
        {
            Log("Usage: InterludeLauncher.exe --apply-package <patch.zip> --client <client-dir> [--sha256 <hash>] [--log <file>]");
            return 2;
        }

        try
        {
            var installer = new PackageInstaller();
            var lastReportedPercent = -1;
            var result = await installer.InstallAsync(
                package,
                client,
                checksum,
                progress =>
                {
                    if (progress.Percent != lastReportedPercent)
                    {
                        lastReportedPercent = progress.Percent;
                        Log($"{progress.Percent,3}% {progress.Message}");
                    }
                },
                CancellationToken.None);
            Log($"Installed patch {result.PatchVersion}; copied={result.CopiedFiles}; deleted={result.DeletedPaths}");
            return 0;
        }
        catch (Exception error)
        {
            Log(error.ToString());
            return 1;
        }
    }

    private static async Task<int> RunContentCommandLineAsync(string[] args)
    {
        string? manifest = null;
        string? client = null;
        string? checksum = null;
        string? logPath = null;
        var repair = false;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index].ToLowerInvariant())
            {
                case "--install-content":
                    manifest = ReadValue(args, ref index);
                    break;
                case "--client":
                    client = ReadValue(args, ref index);
                    break;
                case "--manifest-sha256":
                    checksum = ReadValue(args, ref index);
                    break;
                case "--repair":
                    repair = true;
                    break;
                case "--log":
                    logPath = ReadValue(args, ref index);
                    break;
            }
        }

        void Log(string message)
        {
            Console.WriteLine(message);
            if (!string.IsNullOrWhiteSpace(logPath))
            {
                var fullLogPath = Path.GetFullPath(logPath);
                Directory.CreateDirectory(Path.GetDirectoryName(fullLogPath)!);
                File.AppendAllText(fullLogPath, $"{DateTimeOffset.Now:o} {message}{Environment.NewLine}");
            }
        }

        if (string.IsNullOrWhiteSpace(manifest) || string.IsNullOrWhiteSpace(client))
        {
            Log("Usage: InterludeLauncher.exe --install-content <client-manifest.json> --client <dir> [--manifest-sha256 <hash>] [--repair] [--log <file>]");
            return 2;
        }

        try
        {
            using var installer = new ContentInstaller();
            var lastReportedPercent = -1;
            var result = await installer.InstallAsync(
                manifest,
                client,
                checksum,
                repair,
                progress =>
                {
                    if (progress.Percent != lastReportedPercent)
                    {
                        lastReportedPercent = progress.Percent;
                        Log($"{progress.Percent,3}% {progress.Message}");
                    }
                },
                CancellationToken.None);
            Log(
                $"Client {result.ClientVersion} ready; files={result.InstalledFiles}; " +
                $"packages={result.DownloadedPackages}; deleted={result.DeletedPaths}");
            return 0;
        }
        catch (Exception error)
        {
            Log(error.ToString());
            return 1;
        }
    }

    private static string ReadValue(string[] args, ref int index)
    {
        if (++index >= args.Length)
        {
            throw new ArgumentException($"Missing value after {args[index - 1]}");
        }
        return args[index];
    }
}
