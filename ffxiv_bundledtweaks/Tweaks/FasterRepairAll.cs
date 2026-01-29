using ECommons;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System.Threading.Tasks;

namespace ComplexTweaks.Tweaks;

[Tweak]
[RequiresClientStructs(7240)]
public partial class FasterRepairAll : Tweak {
    public override string Name => "Faster Npc Repair All";
    public override string Description => "Instantly repair all inventories when repairing at an npc.";

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
        // you have to match the event type that you're trying to replace or else the custom event doesn't go through
    }

    private unsafe void HandleEvent(AddonEvent type, AddonArgs args) {
        if (args is not AddonReceiveEventArgs rea || AgentRepair.Instance()->IsSelfRepairOpen) return;
        // the normal event. Set both to 0 to block
        if (rea is { AtkEventType: (byte)AtkEventType.ButtonClick, EventParam: 5 }) {
            rea.AtkEventType = 0;
            rea.EventParam = 0;
        }
        // custom event. Still has to be set to 0 or else the normal triggers (why?!)
        if (rea is { AtkEventType: (byte)AtkEventType.ButtonClick, EventParam: (int)eventParamId }) {
            rea.AtkEventType = 0;
            rea.EventParam = 0;
            RepairAll();
        }
    }

    private unsafe void RepairAll() {
        if (AgentRepair.Instance()->IsSelfRepairOpen) {
            //Svc.Automation.Start(new RepairAllTask());
            return;
        }
        else {
            GameMain.ExecuteCommand(CommandFlag.RepairEquippedItemsNPC, (int)InventoryType.EquippedItems);
            RepairCategory.Values.ForEach(inv => GameMain.ExecuteCommand(CommandFlag.RepairAllItemsNPC, (int)inv));
        }
    }

    private class RepairAllTask : TaskBase {
        // all this avoids is the progress UI, but takes longer since it will "repair" empty inventories and waste time
        protected override async Task Execute() {
            await WaitUntil(() => Svc.Condition.HasPermission(39), "WaitForCanRepair");
            RepairEquipped();
            foreach (var inv in RepairCategory.Values) {
                await WaitUntil(() => Svc.Condition.HasPermission(39), "WaitForCanRepair");
                inv.Repair();
            }
        }

        private static unsafe void RepairEquipped() => RepairManager.Instance()->RepairEquipped(false);
    }
}
