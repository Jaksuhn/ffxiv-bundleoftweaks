using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System.Runtime.InteropServices;

namespace ComplexTweaks.Tweaks;

[Tweak(debug: true)]
[RequiresClientStructs(7296)]
public unsafe partial class InstantReturn : Tweak {
    public override string Name => "Quick Return";
    public override string Description => "Calls the return function directly";

    public override void Enable() => Svc.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "SelectYesno", HandleReturn);
    public override void Disable() => Svc.AddonLifecycle.UnregisterListener(HandleReturn);

    private delegate void DisbandPartyDelegate(); // TODO: cs 7372
    private readonly DisbandPartyDelegate DisbandParty = Marshal.GetDelegateForFunctionPointer<DisbandPartyDelegate>(Svc.SigScanner.ScanText("E8 ?? ?? ?? ?? 40 88 B7 ?? ?? ?? ?? EB 0B C6 87 ?? ?? ?? ?? ??"));

    [AddressHook<AgentReturn>(nameof(AgentReturn.MemberFunctionPointers.Return))]
    private byte AgentReturn_Return(AgentInterface* agent) {
        if (ActionManager.Instance()->GetActionStatus(ActionType.GeneralAction, 6) != 0 || Player.IsInPvP)
            return AgentReturn_ReturnHook.Original(agent);

        if (InfoProxyCrossRealm.IsLocalPlayerInParty()) {
            if (InfoProxyCrossRealm.IsLocalPlayerPartyLeader())
                DisbandParty();
            else
                Svc.Chat.ExecuteCommand("/leave");
        }

        GameMain.ExecuteCommand(CommandFlag.InstantReturn.Value);
        return 1;
    }

    private void HandleReturn(AddonEvent type, AddonArgs args) {
        var agent = AgentModule.Instance()->GetAgentByInternalId(AgentId.Return);
        if (agent is null || agent->AddonId != args.Addon.Id) return;

        args.ReceiveEvent(AtkEventType.ButtonClick, 0);
    }
}
