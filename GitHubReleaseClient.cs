using System.Net.Http.Headers;
using System.Text.Json;

namespace L2TribeLauncher;

internal sealed class GitHubReleaseClient : IDisposable
{
    public const string LatestReleaseApi =
        "https://api.github.com/repos/joaquinzapata1/l2-tribe-launcher/releases/latest";

    private readonly HttpClient _httpClient = new();
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5)
    ];

    public GitHubReleaseClient()
    {
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("L2TribeLauncher", "0.1"));
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public async Task<ReleaseInfo> GetLatestAsync(CancellationToken cancellationToken)
    {
        using var response = await GetWithRetryAsync(LatestReleaseApi, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException("No client patch release has been published yet.");
        }
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var assets = root.GetProperty("assets").EnumerateArray().ToList();
        var package = assets.FirstOrDefault(asset =>
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            return name.StartsWith("patch-", StringComparison.OrdinalIgnoreCase) &&
                   name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
        });
        if (package.ValueKind == JsonValueKind.Undefined)
        {
            throw new InvalidDataException("Latest release does not contain a patch-*.zip asset.");
        }

        var packageName = package.GetProperty("name").GetString()!;
        var checksumName = packageName + ".sha256";
        var checksum = assets.FirstOrDefault(asset =>
            string.Equals(asset.GetProperty("name").GetString(), checksumName, StringComparison.OrdinalIgnoreCase));
        if (checksum.ValueKind == JsonValueKind.Undefined)
        {
            throw new InvalidDataException($"Latest release does not contain {checksumName}.");
        }

        var tag = root.GetProperty("tag_name").GetString() ?? "";
        return new ReleaseInfo(
            tag.TrimStart('v'),
            root.GetProperty("name").GetString() ?? tag,
            root.GetProperty("body").GetString() ?? "",
            packageName,
            package.GetProperty("browser_download_url").GetString()!,
            checksum.GetProperty("browser_download_url").GetString()!);
    }

    public async Task<ContentReleaseInfo> GetLatestContentAsync(CancellationToken cancellationToken)
    {
        const string releasesUrl =
            "https://api.github.com/repos/joaquinzapata1/l2-tribe-launcher/releases?per_page=30";
        using var response = await GetWithRetryAsync(releasesUrl, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (release.GetProperty("draft").GetBoolean())
            {
                continue;
            }

            var assets = release.GetProperty("assets").EnumerateArray().ToList();
            var manifest = assets.FirstOrDefault(asset => string.Equals(
                asset.GetProperty("name").GetString(),
                "client-manifest.json",
                StringComparison.OrdinalIgnoreCase));
            if (manifest.ValueKind == JsonValueKind.Undefined)
            {
                continue;
            }
            var checksum = assets.FirstOrDefault(asset => string.Equals(
                asset.GetProperty("name").GetString(),
                "client-manifest.json.sha256",
                StringComparison.OrdinalIgnoreCase));
            if (checksum.ValueKind == JsonValueKind.Undefined)
            {
                throw new InvalidDataException(
                    "Client release is missing client-manifest.json.sha256.");
            }

            var tag = release.GetProperty("tag_name").GetString() ?? "";
            return new ContentReleaseInfo(
                tag.Replace("client-v", "", StringComparison.OrdinalIgnoreCase),
                release.GetProperty("name").GetString() ?? tag,
                release.GetProperty("body").GetString() ?? "",
                manifest.GetProperty("browser_download_url").GetString()!,
                checksum.GetProperty("browser_download_url").GetString()!);
        }

        throw new InvalidOperationException("No full-client release has been published yet.");
    }

    public async Task<string> DownloadChecksumAsync(string url, CancellationToken cancellationToken)
    {
        var content = await GetStringWithRetryAsync(url, cancellationToken);
        var hash = content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        if (hash.Length != 64 || !hash.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException("Release checksum asset is invalid.");
        }
        return hash.ToLowerInvariant();
    }

    public async Task DownloadAsync(
        string url,
        string destination,
        Action<int>? reportPercent,
        CancellationToken cancellationToken)
    {
        await WithRetryAsync(async () =>
        {
            TryDeleteFile(destination);
            using var response = await _httpClient.GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;

            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var target = new FileStream(
                destination,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                1024 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var buffer = new byte[1024 * 1024];
            long downloaded = 0;
            int read;
            while ((read = await source.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                downloaded += read;
                if (total is > 0)
                {
                    reportPercent?.Invoke((int)Math.Min(100, downloaded * 100 / total.Value));
                }
            }
        }, cancellationToken);
    }

    public void Dispose() => _httpClient.Dispose();

    private async Task<HttpResponseMessage> GetWithRetryAsync(string url, CancellationToken cancellationToken)
    {
        return await WithRetryAsync(async () =>
        {
            var response = await _httpClient.GetAsync(url, cancellationToken);
            if (IsTransient(response.StatusCode))
            {
                var statusCode = (int)response.StatusCode;
                response.Dispose();
                throw new HttpRequestException(
                    $"Transient HTTP error {statusCode} while requesting {url}.");
            }
            return response;
        }, cancellationToken);
    }

    private async Task<string> GetStringWithRetryAsync(string url, CancellationToken cancellationToken)
    {
        return await WithRetryAsync(async () =>
        {
            using var response = await GetWithRetryAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }, cancellationToken);
    }

    private static async Task<T> WithRetryAsync<T>(
        Func<Task<T>> action,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await action();
            }
            catch (Exception error) when (ShouldRetry(error, attempt, cancellationToken))
            {
                await Task.Delay(RetryDelays[attempt], cancellationToken);
            }
        }
    }

    private static async Task WithRetryAsync(
        Func<Task> action,
        CancellationToken cancellationToken)
    {
        await WithRetryAsync(async () =>
        {
            await action();
            return true;
        }, cancellationToken);
    }

    private static bool ShouldRetry(Exception error, int attempt, CancellationToken cancellationToken)
    {
        return !cancellationToken.IsCancellationRequested &&
               attempt < RetryDelays.Length &&
               error is HttpRequestException or IOException or TaskCanceledException;
    }

    private static bool IsTransient(System.Net.HttpStatusCode statusCode)
    {
        var code = (int)statusCode;
        return statusCode is System.Net.HttpStatusCode.RequestTimeout or
               System.Net.HttpStatusCode.TooManyRequests ||
               code >= 500;
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
        }
    }
}
