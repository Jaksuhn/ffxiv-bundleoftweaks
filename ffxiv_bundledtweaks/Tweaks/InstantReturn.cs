using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace ComplexTweaks.Tweaks;

[Tweak(debug: true)]
public unsafe class InstantReturn : Tweak
{
    public override string Name => "Quick Return";
    public override string Description => "Calls the return function directly";

    private readonly Memory.AgentReturn Return = new();
    public override void Enable()
    {
        Return.ReturnHook.Enable();
        Svc.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "SelectYesno", HandleReturn);
    }

    public override void Disable()
    {
        Return.ReturnHook.Disable();
        Svc.AddonLifecycle.UnregisterListener(HandleReturn);
    }

    private void HandleReturn(AddonEvent type, AddonArgs args)
    {
        var agent = AgentModule.Instance()->GetAgentByInternalId(AgentId.Return);
        if (agent is null || agent->AddonId != args.Addon.Id) return;

        args.ReceiveEvent(AtkEventType.ButtonClick, 0, args.GenerateEvent(), args.GenerateEventData());
    }
}
