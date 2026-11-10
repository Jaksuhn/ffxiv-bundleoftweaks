using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ComplexTweaks.Tweaks;

internal enum LoginFailure {
    Rejected, // don't try relogging
    Other, // any other error happened
}

internal sealed class LoginException(LoginFailure failure, string message) : Exception(message) {
    public LoginFailure Failure { get; } = failure;
}

internal readonly record struct OAuthSession(string SessionId, int MaxExpansion);

internal readonly record struct LoginPage(string Url, string Stored, string? LinkedId); // SqExId linked to steam

internal sealed partial class LoginClient(string gamePath) : IDisposable {
    // https://github.com/goatcorp/FFXIVQuickLauncher/blob/c57b1fb86443d1f4b784b85c07f73b1225da3d3c/src/XIVLauncher.Common/Game/Launcher.cs#L57-L66
    // https://github.com/goatcorp/FFXIVQuickLauncher/blob/c57b1fb86443d1f4b784b85c07f73b1225da3d3c/src/XIVLauncher.Common/Game/Launcher.cs#L698-L704
    // https://github.com/goatcorp/FFXIVQuickLauncher/blob/c57b1fb86443d1f4b784b85c07f73b1225da3d3c/src/XIVLauncher.Common/Constants.cs#L8-L22
    private const string LoginHost = "https://ffxiv-login.square-enix.com/oauth/ffxivarr/login";
    private const string PatchHost = "https://patch-gamever.ffxiv.com/http/win32/ffxivneo_release_game";
    private const string FrontierTemplate = "https://launcher.finalfantasyxiv.com/v650/index.html?rc_lang=en_us&time={0}";
    private const string PatcherUserAgent = "FFXIV PATCH CLIENT";
    private const string LoginAccept = "image/gif, image/jpeg, image/pjpeg, application/x-ms-application, application/xaml+xml, application/x-ms-xbap, */*";
    private const string AcceptLanguage = "en-us,en;q=0.9";
    private const string EmptyLoginCookie = "_rsid=\"\"";
    private const string RestartMarker = "window.external.user(\"restartup\");";
    private const string BaseGameVersion = "2012.01.01.0000.0000";
    private const int LoginRegion = 3;
    private static readonly string[] BootFiles = ["ffxivboot.exe", "ffxivboot64.exe", "ffxivlauncher64.exe", "ffxivupdater64.exe"];
    private readonly string _userAgent = $"SQEXAuthor/2.0.0(Windows 6.2; ja-jp; {MakeComputerId()})";
    private readonly HttpClient _http = new(new SocketsHttpHandler {
        UseCookies = false,
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
    }) { Timeout = TimeSpan.FromSeconds(30) };

    public void Dispose() => _http.Dispose();

    // https://github.com/goatcorp/FFXIVQuickLauncher/blob/c57b1fb86443d1f4b784b85c07f73b1225da3d3c/src/XIVLauncher.Common/Game/Launcher.cs#L465-L546
    public async Task<LoginPage> OpenLoginPage(bool freeTrial, EncodedTicket? steamTicket, CancellationToken token) {
        var topUrl = $"{LoginHost}/top?lng=en&rgn={LoginRegion}&isft={(freeTrial ? 1 : 0)}&cssmode=1&isnew=1&launchver=3";
        if (steamTicket is { } ticket)
            topUrl += $"&issteam=1&session_ticket={ticket.Text}&ticket_size={ticket.Length}";

        using var topRequest = new HttpRequestMessage(HttpMethod.Get, topUrl);
        AddLoginHeaders(topRequest, string.Format(FrontierTemplate, DateTime.UtcNow.ToString("yyyy-MM-dd-HH-mm")));
        using var topResponse = await _http.SendAsync(topRequest, token);
        var (stored, linkedId) = ParseLoginPage(await topResponse.Content.ReadAsStringAsync(token), steamTicket != null);
        return new LoginPage(topUrl, stored, linkedId);
    }

    // https://github.com/goatcorp/FFXIVQuickLauncher/blob/c57b1fb86443d1f4b784b85c07f73b1225da3d3c/src/XIVLauncher.Common/Game/Launcher.cs#L548-L611
    public async Task<OAuthSession> SubmitLogin(LoginPage page, string squareEnixId, string password, CancellationToken token) {
        using var loginRequest = new HttpRequestMessage(HttpMethod.Post, $"{LoginHost}/login.send");
        AddLoginHeaders(loginRequest, page.Url);
        loginRequest.Headers.TryAddWithoutValidation("Cache-Control", "no-cache");
        loginRequest.Content = new FormUrlEncodedContent(new Dictionary<string, string> {
            ["_STORED_"] = page.Stored,
            ["sqexid"] = squareEnixId,
            ["password"] = password,
            ["otppw"] = string.Empty, // the form always has this field; accounts with OTP are refused before this point
        });
        using var loginResponse = await _http.SendAsync(loginRequest, token);
        return ParseLoginResponse(await loginResponse.Content.ReadAsStringAsync(token));
    }

    // https://github.com/goatcorp/FFXIVQuickLauncher/blob/c57b1fb86443d1f4b784b85c07f73b1225da3d3c/src/XIVLauncher.Common/Game/Launcher.cs#L399-L446
    public async Task<string> RegisterSession(OAuthSession session, CancellationToken token) {
        var gameVersion = ReadVersion(Path.Combine(gamePath, "game", "ffxivgame.ver"));
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{PatchHost}/{gameVersion}/{session.SessionId}");
        request.Headers.TryAddWithoutValidation("User-Agent", PatcherUserAgent);
        request.Headers.TryAddWithoutValidation("X-Hash-Check", "enabled");
        request.Content = new ByteArrayContent(Encoding.UTF8.GetBytes(BuildVersionReport(session.MaxExpansion)));

        using var response = await _http.SendAsync(request, token);
        var body = await response.Content.ReadAsStringAsync(token);

        if (response.StatusCode == HttpStatusCode.Conflict)
            throw new LoginException(LoginFailure.Other, "The patch server wants the launcher updated first.");
        if (response.StatusCode == HttpStatusCode.Gone)
            throw new LoginException(LoginFailure.Other, "The patch server says this game version is no longer served.");
        if (!response.Headers.TryGetValues("X-Patch-Unique-Id", out var ids))
            throw new LoginException(LoginFailure.Other, $"The patch server didn't return a session ({(int)response.StatusCode}).");
        if (!string.IsNullOrEmpty(body))
            throw new LoginException(LoginFailure.Other, "A game patch is pending. Restart through the launcher to install it.");

        return ids.First();
    }

    // https://github.com/goatcorp/FFXIVQuickLauncher/blob/c57b1fb86443d1f4b784b85c07f73b1225da3d3c/src/XIVLauncher.Common/Game/Launcher.cs#L465-L515
    internal static (string Stored, string? LinkedId) ParseLoginPage(string page, bool isSteam) {
        if (page.Contains(RestartMarker))
            throw new LoginException(LoginFailure.Other, isSteam
                ? "Steam isn't linked to a Square Enix account, or is linked to a different one."
                : "The login page asked to restart. The account may need attention on the Square Enix website.");

        var stored = StoredRegex().Match(page);
        if (!stored.Success)
            throw new LoginException(LoginFailure.Other, "Couldn't read the login page.");
        if (!isSteam)
            return (stored.Groups["stored"].Value, null);

        var linked = LinkedIdRegex().Match(page);
        if (!linked.Success || string.IsNullOrEmpty(linked.Groups["id"].Value))
            throw new LoginException(LoginFailure.Other, "The login page didn't return the Square Enix ID linked to Steam.");
        return (stored.Groups["stored"].Value, linked.Groups["id"].Value);
    }

    // https://github.com/goatcorp/FFXIVQuickLauncher/blob/c57b1fb86443d1f4b784b85c07f73b1225da3d3c/src/XIVLauncher.Common/Game/Launcher.cs#L595-L610
    // https://github.com/goatcorp/FFXIVQuickLauncher/blob/c57b1fb86443d1f4b784b85c07f73b1225da3d3c/src/XIVLauncher.Common/Game/Exceptions/OauthLoginException.cs#L10-L11
    internal static OAuthSession ParseLoginResponse(string page) {
        var success = LoginSuccessRegex().Match(page);
        if (!success.Success) {
            var error = LoginErrorRegex().Match(page);
            throw new LoginException(LoginFailure.Rejected, error.Success
                ? $"Login rejected: {WebUtility.HtmlDecode(error.Groups["message"].Value)}"
                : "Login was rejected, or the page returned something unexpected.");
        }

        var fields = success.Groups["fields"].Value.Split(',');
        if (fields.Length <= 13 || !int.TryParse(fields[13], out var maxExpansion))
            throw new LoginException(LoginFailure.Other, "Couldn't read the login response.");
        if (fields[9] == "0")
            throw new LoginException(LoginFailure.Other, "This account has no active subscription for the game.");
        if (fields[3] == "0")
            throw new LoginException(LoginFailure.Other, "The latest terms haven't been accepted. Accept them through the launcher.");

        return new OAuthSession(fields[1], maxExpansion);
    }

    // https://github.com/goatcorp/FFXIVQuickLauncher/blob/c57b1fb86443d1f4b784b85c07f73b1225da3d3c/src/XIVLauncher.Common/Game/Launcher.cs#L266-L286
    // https://github.com/goatcorp/FFXIVQuickLauncher/blob/c57b1fb86443d1f4b784b85c07f73b1225da3d3c/src/XIVLauncher.Common/Game/Launcher.cs#L354-L368
    internal string BuildVersionReport(int maxExpansion) {
        var boot = Path.Combine(gamePath, "boot");
        var hashes = BootFiles.Select(file => $"{file}/{DescribeFile(Path.Combine(boot, file))}");
        var report = new StringBuilder($"{ReadVersion(Path.Combine(boot, "ffxivboot.ver"))}={string.Join(',', hashes)}\n");

        for (var ex = 1; ex <= maxExpansion; ex++)
            report.Append($"ex{ex}\t{ReadVersion(Path.Combine(gamePath, "game", "sqpack", $"ex{ex}", $"ex{ex}.ver"))}\n");
        return report.ToString();
    }

    // https://github.com/goatcorp/FFXIVQuickLauncher/blob/c57b1fb86443d1f4b784b85c07f73b1225da3d3c/src/XIVLauncher.Common/Game/Launcher.cs#L468-L475
    // https://github.com/goatcorp/FFXIVQuickLauncher/blob/c57b1fb86443d1f4b784b85c07f73b1225da3d3c/src/XIVLauncher.Common/Game/Launcher.cs#L559-L568
    private void AddLoginHeaders(HttpRequestMessage request, string referer) {
        request.Headers.TryAddWithoutValidation("Accept", LoginAccept);
        request.Headers.TryAddWithoutValidation("Referer", referer);
        request.Headers.TryAddWithoutValidation("Accept-Language", AcceptLanguage);
        request.Headers.TryAddWithoutValidation("User-Agent", _userAgent);
        request.Headers.TryAddWithoutValidation("Cookie", EmptyLoginCookie);
    }

    // https://github.com/goatcorp/FFXIVQuickLauncher/blob/c57b1fb86443d1f4b784b85c07f73b1225da3d3c/src/XIVLauncher.Common/Game/Launcher.cs#L613-L623
    private static string DescribeFile(string path) {
        using var stream = File.OpenRead(path);
        return $"{stream.Length}/{Convert.ToHexStringLower(SHA1.HashData(stream))}";
    }

    // https://github.com/goatcorp/FFXIVQuickLauncher/blob/c57b1fb86443d1f4b784b85c07f73b1225da3d3c/src/XIVLauncher.Common/Repository.cs#L67-L76
    private static string ReadVersion(string path) {
        var version = File.Exists(path) ? File.ReadAllText(path).Trim() : string.Empty;
        return version.Length > 0 ? version : BaseGameVersion;
    }

    // https://github.com/goatcorp/FFXIVQuickLauncher/blob/c57b1fb86443d1f4b784b85c07f73b1225da3d3c/src/XIVLauncher.Common/Game/Launcher.cs#L657-L673
    private static string MakeComputerId() {
        var identity = $"{Environment.MachineName}{Environment.UserName}{Environment.OSVersion}{Environment.ProcessorCount}";
        var hash = SHA1.HashData(Encoding.Unicode.GetBytes(identity));
        var id = new byte[5];
        hash.AsSpan(0, 4).CopyTo(id.AsSpan(1));
        id[0] = (byte)-(id[1] + id[2] + id[3] + id[4]);
        return Convert.ToHexStringLower(id);
    }

    [GeneratedRegex("""\t<\s*input .* name="_STORED_" value="(?<stored>.*)">""")]
    private static partial Regex StoredRegex();

    [GeneratedRegex("""<input name="sqexid" type="hidden" value="(?<id>.*)"\/>""")]
    private static partial Regex LinkedIdRegex();

    [GeneratedRegex("""window\.external\.user\("login=auth,ok,(?<fields>.*)\);""")]
    private static partial Regex LoginSuccessRegex();

    [GeneratedRegex("""window\.external\.user\("login=auth,ng,err,(?<message>.*)"\);""")]
    private static partial Regex LoginErrorRegex();
}
