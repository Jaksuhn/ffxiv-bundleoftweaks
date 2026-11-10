using Dalamud.Interface.ImGuiNotification;
using FFXIVClientStructs.FFXIV.Application.Network.LobbyClient;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System.Threading.Tasks;
using Task = System.Threading.Tasks.Task;

namespace ComplexTweaks.Tweaks;

public partial class AutoRelog : Tweak {
    public override string Name => "Auto Relog";
    public override string Description => "Logs back in to your last character after a disconnect.";

    private const int MaxAttempts = 3;
    private const int RetryDelayMs = 3000;
    private const int PollIntervalFrames = 10;
    private const int DismissAttempts = 3;
    private const int ScreenSettleMs = 1000;
    private static readonly TimeSpan ScreenTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan CharaListTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ConfirmTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan LoginTimeout = TimeSpan.FromHours(2); // covers the login queue, which the game re-requests by itself

    private enum WaitResult { Reached, ErrorDialog, CongestionPrompt, TimedOut }
    private enum AttemptResult { LoggedIn, Retry, Stop }

    private ushort? _openErrorRow;
    private bool _openErrorIsAuth;
    private bool _authDismissAllowed;
    private bool _congestionPrompt;
    private bool _loggedIn;
    private bool _tokenRefreshUsed;
    private bool _isFreeTrial;

    public override void OnEnable() {
        IClientState.Get().Login += OnLogin;
        IClientState.Get().Logout += OnLogout;
        OnEnableRefresh();
        if (IClientState.Get().IsLoggedIn)
            ReadFreeTrial();
    }

    public override void OnDisable() {
        IClientState.Get().Login -= OnLogin;
        IClientState.Get().Logout -= OnLogout;
        OnDisableRefresh();
    }

    private void OnLogin() {
        _loggedIn = true;
        _authDismissAllowed = false;
        _tokenRefreshUsed = false;
        ReadFreeTrial();
    }

    private void ReadFreeTrial() => _isFreeTrial = ICondition.Get()[ConditionFlag.OnFreeTrial];

    private void OnLogout(int type, int code) {
        Information($"Logged out: type={type}, code={code}");
        if (type == 0)
            return;

        var target = ReadLastCharacter();
        if (target == 0) {
            Notify("Disconnected, but the game has no last character recorded.", NotificationType.Warning);
            return;
        }

        _loggedIn = false;
        _congestionPrompt = false;
        _tokenRefreshUsed = false;
        Automation.Start(AutoTask.From(t => Relog(t, target), nameof(AutoRelog)));
    }

    private static unsafe ulong ReadLastCharacter() {
        ref var config = ref Framework.Instance()->SystemConfig;
        return config.GetConfigOption(ConfigOption.LastLogin0)->Value.UInt | (ulong)config.GetConfigOption(ConfigOption.LastLogin1)->Value.UInt << 32;
    }

    [VTableHook<AgentLobby>(nameof(AgentLobby.AtkMessageBoxEvent))]
    internal unsafe byte OnDismissed(AtkMessageBoxManager.AtkMessageBoxEvent* thisPtr, void* a2, AtkValue* value) {
        var isAuth = _openErrorIsAuth;
        _openErrorRow = null;
        _openErrorIsAuth = false;
        if (value != null && value->Type == AtkValueType.UInt) {
            // ShowLobbyStatusDialog packs (CodeType << 16) | ErrorSheetRow; >= 4 on a row with no specific handling exits the game
            var row = (ushort)value->UInt;
            var type = value->UInt >> 16;
            if (type >= 4 && (!isAuth || _authDismissAllowed)) {
                Information($"Keeping the game open: row={row}, type={type}");
                value->UInt = row;
            }
            if (isAuth)
                _authDismissAllowed = false;
        }
        return OnDismissedHook.Original(thisPtr, a2, value);
    }

    [AddressHook<AgentLobby>(nameof(AgentLobby.MemberFunctionPointers.ShowLobbyStatusDialog))]
    internal unsafe void ShowLobbyStatusDialog(AgentLobby* agent, LobbyStatusCode* status, int previousState) {
        var row = status->ErrorSheetRow;
        Information($"Lobby status: code={status->Code}, row={row}, type={status->CodeType}");
        if (status->Code == 3089) // Lobby#1238, a Yes/No about visiting a congested world
            _congestionPrompt = true;
        else if (row is not (>= 13300 and <= 13307)) { // backup errors
            _openErrorRow = row;
            _openErrorIsAuth = row == 13100 || status->Code == 5006; // auth errors
            if (_openErrorIsAuth && CanRefreshAutomatically)
                _authDismissAllowed = true;
        }
        ShowLobbyStatusDialogHook.Original(agent, status, previousState);

        // All Worlds under maintenance, which usually comes with a client update, so exit rather than wait. Replaces any relog.
        if (row == 13200) // all worlds under maintenance
            Automation.Start(AutoTask.From(CloseForMaintenance, nameof(CloseForMaintenance))); // just exit the game since maint comes with a patch anyway
        else if (_openErrorIsAuth && !Automation.Running) // AR/LS just need a new token at this point
            Automation.Start(AutoTask.From(RecoverFromAuthError, nameof(RecoverFromAuthError)));
    }

    private async Task Relog(AutoTask t, ulong target) {
        try {
            for (var attempt = 1; attempt <= MaxAttempts; attempt++) {
                t.Status = $"Attempt {attempt}/{MaxAttempts}";
                switch (await Attempt(t, target)) {
                    case AttemptResult.LoggedIn:
                        Information("Logged back in");
                        return;
                    case AttemptResult.Stop:
                        return;
                }
                if (attempt < MaxAttempts)
                    await t.DelayMs(RetryDelayMs);
            }
            Notify($"Gave up logging back in after {MaxAttempts} attempts.", NotificationType.Error);
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch (Exception ex) {
            Error(ex, "Relog failed");
            Notify("Logging back in failed. See the log for details.", NotificationType.Error);
        }
    }

    private async Task<AttemptResult> Attempt(AutoTask t, ulong target) {
        var screen = await WaitForLobbyScreen(t);
        if (screen == WaitResult.ErrorDialog) {
            if (!await DismissError(t))
                return AttemptResult.Stop;
            screen = await WaitForLobbyScreen(t);
        }
        if (screen != WaitResult.Reached)
            return EndAttempt(screen);

        if (!IsReady("_CharaSelectListMenu")) // errors while logging in go back to chara select
            SendLobbyEvent(4);

        var list = await WaitFor(t, IsCharaListReady, CharaListTimeout);
        if (list != WaitResult.Reached)
            return EndAttempt(list);

        if (DescribeUnavailable(target) is { } problem) {
            Notify(problem, NotificationType.Warning);
            return AttemptResult.Stop;
        }

        SelectDisplayedCharacter();
        var confirm = await WaitFor(t, () => IsReady("SelectYesno"), ConfirmTimeout);
        if (confirm != WaitResult.Reached)
            return EndAttempt(confirm);
        AddonSelectYesno.Yes();

        var login = await WaitFor(t, () => _loggedIn, LoginTimeout);
        return login == WaitResult.Reached ? AttemptResult.LoggedIn : EndAttempt(login);
    }

    private AttemptResult EndAttempt(WaitResult result) {
        if (result != WaitResult.CongestionPrompt)
            return AttemptResult.Retry;
        Notify("The World is congested and the game is asking whether to continue. Answer it to finish logging in.", NotificationType.Warning);
        return AttemptResult.Stop;
    }

    private async Task<WaitResult> WaitForLobbyScreen(AutoTask t) {
        var screen = await WaitFor(t, IsOnLobbyScreen, ScreenTimeout);
        if (screen != WaitResult.Reached)
            return screen;
        await t.DelayMs(ScreenSettleMs);
        return await WaitFor(t, IsOnLobbyScreen, ScreenTimeout);
    }

    private async Task<WaitResult> WaitFor(AutoTask t, Func<bool> reached, TimeSpan timeout) {
        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (true) {
            if (_congestionPrompt)
                return WaitResult.CongestionPrompt;
            if (_openErrorRow != null && IsReady("Dialogue"))
                return WaitResult.ErrorDialog;
            if (reached())
                return WaitResult.Reached;
            if (Environment.TickCount64 >= deadline)
                return WaitResult.TimedOut;
            await t.NextFrame(PollIntervalFrames);
        }
    }

    private async Task CloseForMaintenance(AutoTask t) {
        await ClickErrorUntilClosed(t);
        if (_openErrorRow != null)
            return; // ClickErrorUntilClosed logged it

        if (await WaitFor(t, () => IsReady("_TitleMenu"), ScreenTimeout) != WaitResult.Reached) {
            Warning("All Worlds are under maintenance, but the title screen didn't open, so the game wasn't closed");
            return;
        }
        await t.DelayMs(ScreenSettleMs);

        Information("All Worlds are under maintenance, exiting the game");
        SendLobbyEvent(12); // exit game
    }

    private async Task RecoverFromAuthError(AutoTask t) {
        if (await RefreshForAuthError(t))
            await ClickErrorUntilClosed(t);
    }

    private async Task<bool> DismissError(AutoTask t) {
        if (_openErrorIsAuth && !await RefreshForAuthError(t))
            return false;
        await ClickErrorUntilClosed(t);
        return true;
    }

    private bool CanRefreshAutomatically => !_tokenRefreshUsed && !_autoRefreshBlocked; // don't send more than once between logins to not end up in a loop

    private async Task<bool> RefreshForAuthError(AutoTask t) {
        if (_tokenRefreshUsed) {
            _authDismissAllowed = false;
            Notify("Still getting an authentication error after refreshing the login token. Leaving the error open.", NotificationType.Error);
            return false;
        }
        _tokenRefreshUsed = true;

        if (await RefreshToken(automatic: true, t.CancelToken) is { } failure) {
            _authDismissAllowed = false;
            Notify($"Couldn't refresh the login token, leaving the authentication error open. {failure}", NotificationType.Error);
            return false;
        }
        return true;
    }

    private async Task ClickErrorUntilClosed(AutoTask t) {
        for (var i = 0; i < DismissAttempts && _openErrorRow != null; i++) {
            ClickErrorDialogOk();
            var deadline = Environment.TickCount64 + (long)(ConfirmTimeout / DismissAttempts).TotalMilliseconds;
            while (_openErrorRow != null && Environment.TickCount64 < deadline)
                await t.NextFrame(PollIntervalFrames);
        }
        if (_openErrorRow != null)
            Warning($"Error dialog for row {_openErrorRow} didn't close");
    }

    private bool IsOnLobbyScreen() => IsReady("_TitleMenu") || IsReady("_CharaSelectListMenu");
    private static unsafe bool IsCharaListReady() => IsReady("_CharaSelectListMenu") && AgentLobby.Instance()->HoveredCharacterContentId != 0;
    private static unsafe void SelectDisplayedCharacter() => SendLobbyEvent(29, 0, AgentLobby.Instance()->DisplayedCharacterIndex);

    private static unsafe string? DescribeUnavailable(ulong target) {
        var agent = AgentLobby.Instance();
        if (agent->HoveredCharacterContentId != target)
            return "Character select didn't open on your last character, so it wasn't logged in.";

        foreach (var entry in agent->LobbyData.CharaSelectEntries) {
            if (entry.Value == null || entry.Value->ContentId != target)
                continue;
            if (!entry.Value->IsNotLocked())
                return "Your last character is locked and can't be logged in.";
            if (entry.Value->LoginFlags.HasFlag(CharaSelectCharacterEntryLoginFlags.DCTraveling))
                return "Your last character is on another data center, so it wasn't logged in.";
            return null;
        }
        return "Your last character isn't on the character list, so it wasn't logged in.";
    }

    private static unsafe bool IsReady(string name) {
        var addon = RaptureAtkUnitManager.Instance()->GetAddonByName(name);
        return addon != null && addon->IsVisible && addon->IsReady && addon->IsFullyLoaded();
    }

    private static unsafe void ClickErrorDialogOk() {
        var addon = RaptureAtkUnitManager.Instance()->GetAddonByName("Dialogue");
        if (addon == null || !addon->IsReady)
            return;
        var evt = new AtkEvent { Listener = &addon->AtkEventListener, Target = &AtkStage.Instance()->AtkEventTarget };
        var data = new AtkEventData();
        addon->ReceiveEvent(AtkEventType.ButtonClick, 0, &evt, &data);
    }

    private static unsafe void SendLobbyEvent(params ReadOnlySpan<int> args) {
        var values = stackalloc AtkValue[args.Length];
        for (var i = 0; i < args.Length; i++)
            values[i] = new AtkValue { Type = AtkValueType.Int, Int = args[i] };
        var result = new AtkValue();
        AgentLobby.Instance()->ReceiveEvent(&result, values, (uint)args.Length, 0);
    }

    private void Notify(string message, NotificationType type) {
        switch (type) {
            case NotificationType.Error:
                Error(message);
                break;
            case NotificationType.Warning:
                Warning(message);
                break;
            default:
                Information(message);
                break;
        }
        INotificationManager.Get().AddNotification(new Notification { Title = $"{Svc.Interface.Manifest.Name} - {Name}", Content = message, Type = type });
    }
}
