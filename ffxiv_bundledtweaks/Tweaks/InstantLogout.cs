using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.Shell;
using Lumina.Excel.Sheets;

namespace ComplexTweaks.Tweaks;

[Tweak]
public partial class InstantLogout : Tweak
{
    public override string Name => "Instant Logout";
    public override string Description => "Skips the 20 second countdown when logging out outside of a sanctuary";

    [AddressHook<ShellCommandModule>(nameof(ShellCommandModule.MemberFunctionPointers.ExecuteCommandInner))]
    private unsafe void ExecuteCommandInner(ShellCommandModule* commandModule, Utf8String* rawMessage, UIModule* uiModule)
    {
        var msg = (*rawMessage).ToString();
        if (msg is null or { Length: 0 } || !msg.StartsWith('/'))
        {
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

    [SigHook("E8 ?? ?? ?? ?? 40 B5 ?? 41 B9")]
    private unsafe bool SystemMenuExecute(AgentHUD* agentHud, int a2, int a3, int a4, byte* a5)
    {
        if (a2 is 1 && a4 is -1)
        {
            switch (a3)
            {
                case 23 when ShouldInstantLogout():
                    AgentLobby.Instance()->HandleLogout(false, 60);
                    return false;
                case 24 when ShouldInstantLogout():
                    AgentLobby.Instance()->HandleLogout(true, 60);
                    return false;
            }
        }

        return SystemMenuExecuteHook.Original(agentHud, a2, a3, a4, a5);
    }

    // only trigger instant when the 20s would trigger since this causes "the selected character was not logged out properly" and I'd like to do that as infrequently as possible
    // TODO: figure out what needs to be done before HandleLogout to not have the above happen
    private unsafe bool ShouldInstantLogout() => !Player.IsInDuty && !TerritoryInfo.Instance()->InSanctuary;
}
