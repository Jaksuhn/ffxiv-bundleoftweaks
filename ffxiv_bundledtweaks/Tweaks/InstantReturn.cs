using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System.Threading.Tasks;

namespace ComplexTweaks.Tweaks;

[Tweak(debug: true)]
public partial class InstantReturn : Tweak {
    public override string Name => "Quick Return";
    public override string Description => "Calls the return function directly";

    public override void Enable() => Svc.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "SelectYesno", HandleReturn);
    public override void Disable() => Svc.AddonLifecycle.UnregisterListener(HandleReturn);

    [AddressHook<AgentReturn>(nameof(AgentReturn.MemberFunctionPointers.Return))]
    private unsafe void AgentReturn_Return(AgentReturn* agent) {
        if (ActionManager.Instance()->GetActionStatus(ActionType.GeneralAction, 6) != 0 || Player.IsInPvP)
            AgentReturn_ReturnHook.Original(agent);

        // there's some condition that party disbanding requires but can't find it so we're retrying
        Svc.Automation.Start(new Return());
    }

    private unsafe void HandleReturn(AddonEvent type, AddonArgs args) {
        var agent = AgentModule.Instance()->GetAgentByInternalId(AgentId.Return);
        if (agent is null || agent->AddonId != args.Addon.Id) return;

        args.ReceiveEvent(AtkEventType.ButtonClick, 0);
    }

    private sealed class Return : TaskBase {
        private static unsafe bool Disband() => InfoProxyPartyMember.Instance()->DisbandParty();
        private static unsafe bool Leave() => InfoProxyPartyMember.Instance()->LeaveParty();
        protected override async Task Execute() {
            if (InfoProxyCrossRealm.IsLocalPlayerInParty()) {
                if (InfoProxyCrossRealm.IsLocalPlayerPartyLeader())
                    await WaitUntil(Disband, "WaitForDisband");
                else
                    await WaitUntil(Leave, "WaitForLeave");
            }

            GameMain.ExecuteCommand(CommandFlag.InstantReturn.Value);
        }
    }
}
