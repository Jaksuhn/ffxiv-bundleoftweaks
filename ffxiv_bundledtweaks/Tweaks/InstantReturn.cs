using ECommons.Automation;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace ComplexTweaks.Tweaks;

[Tweak(debug: true)]
public unsafe partial class InstantReturn : Tweak {
    public override string Name => "Quick Return";
    public override string Description => "Calls the return function directly";

    public override void Enable() => Svc.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "SelectYesno", HandleReturn);
    public override void Disable() => Svc.AddonLifecycle.UnregisterListener(HandleReturn);

    [AddressHook<AgentReturn>(nameof(AgentReturn.MemberFunctionPointers.Return))]
    private byte AgentReturn_Return(AgentInterface* agent) {
        if (ActionManager.Instance()->GetActionStatus(ActionType.GeneralAction, 6) != 0 || Player.IsInPvP)
            return AgentReturn_ReturnHook.Original(agent);

        if (Svc.Party.Length > 1) {
            if (Svc.Party[0]?.Name == Svc.ClientState.LocalPlayer?.Name)
                Chat.SendMessage("/partycmd breakup");
            else
                Chat.SendMessage("/leave");
        }

        GameMain.ExecuteCommand((int)ExecuteCommandFlag.InstantReturn);
        return 1;
    }

    private void HandleReturn(AddonEvent type, AddonArgs args) {
        var agent = AgentModule.Instance()->GetAgentByInternalId(AgentId.Return);
        if (agent is null || agent->AddonId != args.Addon.Id) return;

        args.ReceiveEvent(AtkEventType.ButtonClick, 0, args.GenerateEvent(), args.GenerateEventData());
    }
}
