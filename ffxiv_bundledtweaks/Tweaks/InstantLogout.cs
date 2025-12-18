using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.Shell;
using Lumina.Excel.Sheets;

namespace ComplexTweaks.Tweaks;

[Tweak]
public unsafe partial class InstantLogout : Tweak {
    public override string Name => "Instant Logout";
    public override string Description => "Skips the 20 second countdown when logging out outside of a sanctuary";

    [AddressHook<ShellCommandModule>(nameof(ShellCommandModule.MemberFunctionPointers.ExecuteCommandInner))]
    private unsafe void ExecuteCommandInner(ShellCommandModule* commandModule, Utf8String* rawMessage, UIModule* uiModule) {
        var msg = (*rawMessage).ToString();
        if (msg is null or { Length: 0 } || !msg.StartsWith('/')) {
            ExecuteCommandInnerHook.Original(commandModule, rawMessage, uiModule);
            return;
        }

        // ToString is needed so that the implicit equals doesn't try to turn msg into a ROSSSS (which will crash with payloads like translate)
        if (GetRow<TextCommand>(172) is { Command: var cmd, Alias: var alias } && (cmd.ToString() == msg || alias.ToString() == msg) && ShouldInstantLogout())
            AgentLobby.Instance()->HandleLogout(false, 60);
        if (GetRow<TextCommand>(173) is { Command: var cmd2, Alias: var alias2 } && (cmd2.ToString() == msg || alias2.ToString() == msg) && ShouldInstantLogout())
            AgentLobby.Instance()->HandleLogout(true, 60);

        ExecuteCommandInnerHook.Original(commandModule, rawMessage, uiModule);
    }

    [VTableHook<UIModule>(203)]
    private unsafe void ExecuteMainCommand(UIModule* self, uint command) {
        switch (command) {
            case 23 when ShouldInstantLogout():
                AgentLobby.Instance()->HandleLogout(false, 60);
                break;
            case 24 when ShouldInstantLogout():
                AgentLobby.Instance()->HandleLogout(true, 60);
                break;
            default:
                ExecuteMainCommandHook.Original(self, command);
                break;
        }
    }

    //[AddressHook<AgentHUD>(nameof(AgentHUD.MemberFunctionPointers.HandleMainCommandOperation))]
    //private unsafe bool HandleMainCommandOperation(AgentHUD* self, MainCommandOperation operation, uint param1, int param2 = -1, byte* param3 = null)
    //{
    //    // this doesn't handle hotbar actions
    //    if (operation is MainCommandOperation.ExecuteMainCommand && param2 is -1)
    //    {
    //        switch (param1)
    //        {
    //            case 23 when ShouldInstantLogout():
    //                AgentLobby.Instance()->HandleLogout(false, 60);
    //                return false;
    //            case 24 when ShouldInstantLogout():
    //                AgentLobby.Instance()->HandleLogout(true, 60);
    //                return false;
    //        }
    //    }

    //    return HandleMainCommandOperationHook.Original(self, operation, param1, param2, param3);
    //}

    // only trigger instant when the 20s would trigger since this causes "the selected character was not logged out properly" and I'd like to do that as infrequently as possible
    // TODO: figure out what needs to be done before HandleLogout to not have the above happen
    private unsafe bool ShouldInstantLogout() => !Player.IsInDuty && !TerritoryInfo.Instance()->InSanctuary;
}
