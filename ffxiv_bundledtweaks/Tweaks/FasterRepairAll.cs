using ECommons;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace ComplexTweaks.Tweaks;

[Tweak]
public class FasterRepairAll : Tweak {
    public override string Name => "Faster Repair All";
    public override string Description => "Is this company stupid";

    private readonly uint eventParamId = 0x43425400;
    public override void Enable() {
        Svc.AddonLifecycle.RegisterListener(AddonEvent.PreReceiveEvent, "SelectYesno", CheckYes);
        //Svc.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "Repair", AddEvent);
        //Svc.AddonLifecycle.RegisterListener(AddonEvent.PreReceiveEvent, "Repair", HandleEvent);
    }

    private void CheckYes(AddonEvent type, AddonArgs args) {
        if (((AddonReceiveEventArgs)args).EventParam == 1) // when you click no
            ((AddonReceiveEventArgs)args).EventParam = 0; // make it impossible?
    }

    public override void Disable() {
        Svc.AddonLifecycle.UnregisterListener(CheckYes);
        //Svc.AddonLifecycle.UnregisterListener(AddEvent);
        //Svc.AddonLifecycle.UnregisterListener(HandleEvent);
    }

    private unsafe void AddEvent(AddonEvent type, AddonArgs args) {
        var node = args.GetAddon<AtkUnitBase>()->GetNodeById<AtkResNode>(12);
        node->AddEvent(AtkEventType.MouseClick, eventParamId, &args.GetAddon<AtkUnitBase>()->AtkEventListener, null, false);
    }

    private void HandleEvent(AddonEvent type, AddonArgs args) {
        if (args is AddonReceiveEventArgs { EventParam: var param }) {
            if (param == eventParamId)
                RepairAll();
            else
                ((AddonReceiveEventArgs)args).EventParam = 0;
        }
    }

    private void RepairAll() => RepairCategory.Values.ToList().ForEach(inv => GameMain.ExecuteCommand(CommandFlag.RepairAllItemsNPC.Value, inv.Value));
}
