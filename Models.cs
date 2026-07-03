using System.Text.Json;
using System.Text.Json.Serialization;

namespace L2InterludeUpdater;

internal static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
}

internal sealed class PatchManifest
{
    public int FormatVersion { get; set; }
    public string PatchVersion { get; set; } = "";
    public DateTimeOffset GeneratedUtc { get; set; }
    public BaseClientManifest BaseClient { get; set; } = new();
    public string PayloadRoot { get; set; } = "";
    public string LaunchExecutable { get; set; } = "";
    public List<string> Delete { get; set; } = [];
    public List<PatchFileManifest> Files { get; set; } = [];
}

internal sealed class BaseClientManifest
{
    public string Name { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public List<string> RequiredPaths { get; set; } = [];
}

internal sealed class PatchFileManifest
{
    public string Path { get; set; } = "";
    public long Size { get; set; }
    public string Sha256 { get; set; } = "";
}

internal sealed class InstalledState
{
    public string PatchVersion { get; set; } = "";
    public DateTimeOffset InstalledUtc { get; set; }
    public string PackageSha256 { get; set; } = "";
}

internal sealed class UpdaterSettings
{
    public string ClientDirectory { get; set; } = "";
    public string Language { get; set; } = "ES";
}

internal sealed record InstallProgress(int Percent, string Message);

internal sealed record InstallResult(
    string PatchVersion,
    string PackageSha256,
    string? BackupDirectory,
    int CopiedFiles,
    int DeletedPaths);

internal sealed record ReleaseInfo(
    string Version,
    string Name,
    string Notes,
    string PackageName,
    string PackageUrl,
    string ChecksumUrl);

internal sealed record ContentReleaseInfo(
    string Version,
    string Name,
    string Notes,
    string ManifestUrl,
    string ManifestChecksumUrl);

internal sealed class ContentManifest
{
    public int FormatVersion { get; set; }
    public string ClientVersion { get; set; } = "";
    public DateTimeOffset GeneratedUtc { get; set; }
    public string LaunchExecutable { get; set; } = "";
    public long TotalSize { get; set; }
    public List<ContentPackage> Packages { get; set; } = [];
    public List<ContentFile> Files { get; set; } = [];
    public List<string> Deleted { get; set; } = [];
}

internal sealed class ContentPackage
{
    public string Id { get; set; } = "";
    public string FileName { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public long Size { get; set; }
    public string Sha256 { get; set; } = "";
    public long UncompressedSize { get; set; }
    public int FileCount { get; set; }
}

internal sealed class ContentFile
{
    public string Path { get; set; } = "";
    public long Size { get; set; }
    public string Sha256 { get; set; } = "";
    public string PackageId { get; set; } = "";
    public string AddedInVersion { get; set; } = "";
    public bool Mutable { get; set; }
}

internal sealed record ContentInstallResult(
    string ClientVersion,
    int InstalledFiles,
    int DeletedPaths,
    int DownloadedPackages,
    string? BackupDirectory);

internal static class StateFiles
{
    public const string InstalledStateName = ".l2-interlude-patch.json";
    public const string ContentManifestName = ".l2-client-manifest.json";

    public static InstalledState? ReadInstalledState(string clientDirectory)
    {
        try
        {
            var path = Path.Combine(clientDirectory, InstalledStateName);
            return File.Exists(path)
                ? JsonSerializer.Deserialize<InstalledState>(File.ReadAllText(path), JsonDefaults.Options)
                : null;
        }
        catch
        {
            return null;
        }
    }

    public static ContentManifest? ReadContentManifest(string clientDirectory)
    {
        try
        {
            var path = Path.Combine(clientDirectory, ContentManifestName);
            return File.Exists(path)
                ? JsonSerializer.Deserialize<ContentManifest>(File.ReadAllText(path), JsonDefaults.Options)
                : null;
        }
        catch
        {
            return null;
        }
    }
}
