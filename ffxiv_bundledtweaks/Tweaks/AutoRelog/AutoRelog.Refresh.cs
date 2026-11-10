using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace ComplexTweaks.Tweaks;

public partial class AutoRelog {
    private CancellationTokenSource? _lifetime;
    private bool _refreshRunning;
    private bool _autoRefreshBlocked;
    private string _refreshStatus = string.Empty;

    private static string LauncherDirectory => Path.GetFullPath(Path.Combine(Svc.Interface.GetPluginConfigDirectory(), "..", ".."));

    private void OnEnableRefresh() => _lifetime = new();

    private void OnDisableRefresh() {
        _lifetime?.Cancel();
        _lifetime?.Dispose();
        _lifetime = null;
        _refreshStatus = string.Empty;
    }

    private async Task<string?> RefreshToken(bool automatic, CancellationToken token) {
        if (_refreshRunning)
            return "A refresh is already running.";
        if (automatic && _autoRefreshBlocked)
            return "Automatic refresh is off because the last login was rejected. Check the password saved in XIVLauncher.";

        _refreshRunning = true;
        try {
            var session = await Authenticate(token);
            await IFramework.Get().Run(() => SetGameSession(session), token);

            _authDismissAllowed = true;
            _autoRefreshBlocked = false;
            Information("Login token refreshed");
            return null;
        }
        catch (LoginException ex) {
            if (ex.Failure == LoginFailure.Rejected)
                _autoRefreshBlocked = true;
            Warning($"Login token refresh failed: {ex.Message}");
            return ex.Message;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !token.IsCancellationRequested) {
            // just the name to not log the request
            Warning($"Login token refresh failed: {ex.GetType().Name}");
            return ex is HttpRequestException or TaskCanceledException ? "Couldn't reach the login servers." : $"Something went wrong ({ex.GetType().Name}).";
        }
        finally {
            _refreshRunning = false;
        }
    }

    private static unsafe void SetGameSession(string session) => AgentLobby.Instance()->GameSession.SetString(session);

    private async Task<string> Authenticate(CancellationToken token) {
        var isSteam = await IFramework.Get().Run(() => SteamTicket.IsAvailable, token);
        var account = LauncherAccounts.Current(LauncherDirectory);
        if (account == null && !isSteam)
            throw new LoginException(LoginFailure.Other, "Couldn't tell which XIVLauncher account launched the game.");

        if (account?.UseOtp == true)
            throw new LoginException(LoginFailure.Other, "This account uses a one-time password, which can't be entered automatically.");

        using var client = new LoginClient(GamePath());
        SteamTicket? ticket = null;
        OAuthSession session;
        try {
            if (isSteam)
                ticket = await SteamTicket.Request(token);
            var encoded = ticket?.Encoded;
            session = await Task.Run(async () => {
                var page = await client.OpenLoginPage(_isFreeTrial, encoded, token);
                // steam names the linked account, so its password is right even if XIVLauncher has another account selected.
                var id = page.LinkedId ?? account!.UserName;
                var password = LauncherAccounts.ReadPassword(id) ?? throw new LoginException(LoginFailure.Other, "XIVLauncher has no saved password for this account. Turn on its option to save the password.");
                return await client.SubmitLogin(page, id, password, token);
            }, token);
        }
        finally {
            if (ticket != null)
                await ticket.Cancel();
        }
        return await Task.Run(() => client.RegisterSession(session, token), token);
    }

    private static string GamePath() {
        using var process = Process.GetCurrentProcess();
        return Directory.GetParent(Path.GetDirectoryName(process.MainModule!.FileName)!)!.FullName;
    }

    protected override void DrawCustomConfig() {
        if (!_autoRefreshBlocked && _refreshStatus.Length is 0) return;

        ImGui.DrawSection("Login Token");
        if (_autoRefreshBlocked)
            ImGui.TextColoredWrapped(ImGuiColors.DalamudRed, "Automatic refresh is off because the last login was rejected. Check the password saved in XIVLauncher.");
        if (_refreshStatus.Length > 0)
            ImGui.TextWrapped(_refreshStatus);
    }

    protected override unsafe void DrawDebugConfig() {
        ImGui.DrawSection("Login Token Debug");

        if (ImGui.Button("Invalidate"))
            AgentLobby.Instance()->GameSession.SetString("invalid");

        ImGui.SameLine();

        using (ImRaii.Disabled(_refreshRunning || _lifetime == null)) {
            if (ImGui.Button("Refresh"))
                StartManualRefresh(_lifetime!.Token);
        }
    }

    private void StartManualRefresh(CancellationToken token) {
        _refreshStatus = "Refreshing...";
        _ = IFramework.Get().Run(async () => {
            var failure = await RefreshToken(automatic: false, token);
            _refreshStatus = failure ?? "Login token refreshed.";
            if (failure != null)
                Notify($"Couldn't refresh the login token. {failure}", NotificationType.Error);
        }, token);
    }
}
