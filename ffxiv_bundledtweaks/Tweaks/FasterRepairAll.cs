using ECommons;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace ComplexTweaks.Tweaks;

#if DEBUG
[Tweak]
public partial class FasterRepairAll : Tweak {
    public override string Name => "Faster Repair All";
    public override string Description => "Is this company stupid";

    private const uint eventParamId = 0x43425400;
    public override void Enable() {
        Svc.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "Repair", AddEvent);
        Svc.AddonLifecycle.RegisterListener(AddonEvent.PreReceiveEvent, "Repair", HandleEvent);
    }

    public override void Disable() {
        Svc.AddonLifecycle.UnregisterListener(AddEvent);
        Svc.AddonLifecycle.UnregisterListener(HandleEvent);
    }

    private unsafe void AddEvent(AddonEvent type, AddonArgs args) {
        var node = args.GetAddon<AtkUnitBase>()->GetNodeById<AtkResNode>(12);
        node->AddEvent(AtkEventType.ButtonClick, eventParamId, &args.GetAddon<AtkUnitBase>()->AtkEventListener, null, false);
    }

    private void HandleEvent(AddonEvent type, AddonArgs args) {
        if (args is not AddonReceiveEventArgs rea) return;
        Log($"Received Repair Event: Type={rea.AtkEventType}, Param={rea.EventParam}");
        if (rea is { AtkEventType: (byte)AtkEventType.ButtonClick, EventParam: 5 }) {
            rea.AtkEventType = 0;
            rea.EventParam = 0;
        }
        if (rea is { AtkEventType: (byte)AtkEventType.ButtonClick, EventParam: (int)eventParamId }) {
            rea.AtkEventType = 0;
            rea.EventParam = 0;
            RepairAll();
        }
    }

    private unsafe void RepairAll() {
        if (AgentRepair.Instance()->IsSelfRepairOpen) { // TODO: offset on this might be wrong or it's the wrong thing entirely to check for
            GameMain.ExecuteCommand(CommandFlag.RepairEquippedItems.Value, 1000);
            RepairCategory.Values.ToList().ForEach(inv => GameMain.ExecuteCommand(CommandFlag.RepairAllItems.Value, inv.Value));
        }
        else {
            GameMain.ExecuteCommand(CommandFlag.RepairEquippedItemsNPC.Value, 1000);
            RepairCategory.Values.ToList().ForEach(inv => GameMain.ExecuteCommand(CommandFlag.RepairAllItemsNPC.Value, inv.Value));
        }
    }
}
#endif
