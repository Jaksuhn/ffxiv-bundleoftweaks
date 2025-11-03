using Dalamud.Hooking;
using ECommons.EzHookManager;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.Shell;
using Lumina.Excel.Sheets;
using System.Runtime.InteropServices;

namespace ComplexTweaks.Features;

[Tweak]
public class InstantLogout : Tweak
{
    public override string Name => "Instant Logout";
    public override string Description => "Skips the 20 second countdown when logging out outside of a sanctuary";

    internal unsafe EzHook<ShellCommandModule.Delegates.ExecuteCommandInner>? SentChatHook;
    Hook<Memory.Delegates.SystemMenuExecutionDelegate>? _systemMenuHook;

    public override unsafe void Enable()
    {
        SentChatHook ??= new((nint)ShellCommandModule.MemberFunctionPointers.ExecuteCommandInner, SentChatDetour, false);
        SentChatHook.Enable();
        Svc.Hook.InitializeFromAttributes(this);
        _systemMenuHook = Svc.Hook.HookFromSignature<Memory.Delegates.SystemMenuExecutionDelegate>("E8 ?? ?? ?? ?? 40 B5 ?? 41 B9", SystemMenuExecuteDetour);
        _systemMenuHook.Enable();
    }

    public override void Disable()
    {
        SentChatHook?.Pause();
        _systemMenuHook?.Dispose();
        _systemMenuHook = null;
    }

    private unsafe void SentChatDetour(ShellCommandModule* commandModule, Utf8String* rawMessage, UIModule* uiModule)
    {
        var msg = (*rawMessage).ToString();
        if (msg is null or { Length: 0 } || !msg.StartsWith('/'))
        {
            SentChatHook!.Original(commandModule, rawMessage, uiModule);
            return;
        }

        // ToString is needed so that the implicit equals doesn't try to turn msg into a ROSSSS (which will crash with payloads like translate)
        if (GetRow<TextCommand>(172) is { Command: var cmd, Alias: var alias } && (cmd.ToString() == msg || alias.ToString() == msg) && ShouldLogout())
            Logout();

        SentChatHook!.Original(commandModule, rawMessage, uiModule);
    }

    private unsafe bool SystemMenuExecuteDetour(AgentHUD* agentHud, int a2, int a3, int a4, byte* a5)
    {
        if (a2 is 1 && a4 is -1)
        {
            switch (a3)
            {
                case 23 when ShouldLogout():
                    Logout();
                    return false;
                    // 24 is for shutdown but I don't want to override that one
            }
        }

        return _systemMenuHook!.Original(agentHud, a2, a3, a4, a5);
    }

    // only trigger instant when the 20s would trigger since this causes "the selected character was not logged out properly" and I'd like to do that as infrequently as possible
    private unsafe bool ShouldLogout() => !Player.IsInDuty && !TerritoryInfo.Instance()->InSanctuary;
    private unsafe void Logout()
    {
        for (var i = 0; i < Svc.Condition.MaxEntries; i++)
            Marshal.WriteByte(Svc.Condition.Address + i, 0);

        foreach (var addon in RaptureAtkUnitManager.Instance()->AtkUnitManager.AllLoadedUnitsList.Entries)
        {
            if (addon.Value == null || !addon.Value->IsVisible) continue;
            addon.Value->Close(true);
        }

        Service.Memory.SendLogout?.Invoke();
    }
}
