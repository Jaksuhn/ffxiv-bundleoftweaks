using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ComplexTweaks.TweakSystem.Packs;

public sealed record GitHubRelease(string Tag, string DownloadUrl);

public static class GitHubPackClient {
    public const string AssetName = "tweaks.dll";

    private static readonly HttpClient Http = CreateClient();

    public static bool TryParse(string url, out string owner, out string name) {
        owner = "";
        name = "";
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
            return false;

        var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !IsSafe(parts[0]) || !IsSafe(parts[1]))
            return false;

        owner = parts[0];
        name = parts[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? parts[1][..^4] : parts[1];
        return IsSafe(name);
    }

    public static async Task<GitHubRelease> GetLatestAsync(string owner, string name, CancellationToken cancellationToken) {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{owner}/{name}/releases/latest");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await Http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var tag = document.RootElement.GetProperty("tag_name").GetString();
        if (string.IsNullOrWhiteSpace(tag))
            throw new InvalidOperationException("Latest release has no tag.");

        foreach (var asset in document.RootElement.GetProperty("assets").EnumerateArray()) {
            if (asset.GetProperty("name").GetString() != AssetName)
                continue;
            var download = asset.GetProperty("browser_download_url").GetString();
            if (string.IsNullOrWhiteSpace(download))
                break;
            return new GitHubRelease(tag, download);
        }

        throw new InvalidOperationException($"Latest release has no {AssetName} asset.");
    }

    public static async Task DownloadAsync(string url, string destination, CancellationToken cancellationToken) {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + ".tmp";
        using var response = await Http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using (var file = File.Create(temporary))
            await response.Content.CopyToAsync(file, cancellationToken);
        File.Move(temporary, destination, overwrite: true);
    }

    private static HttpClient CreateClient() {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("CBT");
        return client;
    }

    private static bool IsSafe(string value)
        => value.Length > 0 && value != "." && value != ".." && value.IndexOfAny(['/', '\\', ':']) < 0;
}
