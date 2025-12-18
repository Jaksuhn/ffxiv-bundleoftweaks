//using ECommons;
//using FFXIVClientStructs.FFXIV.Component.GUI;

//namespace ComplexTweaks.Tweaks;

//[Tweak]
//public class FasterRepairAll : Tweak {
//    public override string Name => "Faster Repair All";
//    public override string Description => "Is this company retarded";

//    private readonly uint eventParamId = 0x43425400;
//    public override void Enable() {
//        Svc.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "Repair", AddEvent);
//        Svc.AddonLifecycle.RegisterListener(AddonEvent.PreReceiveEvent, "Repair", HandleEvent);
//    }

//    public override void Disable() {
//        Svc.AddonLifecycle.UnregisterListener(AddEvent);
//        Svc.AddonLifecycle.UnregisterListener(HandleEvent);
//    }

//    private unsafe void AddEvent(AddonEvent type, AddonArgs args) {
//        var node = args.GetAddon<AtkUnitBase>()->GetNodeById<AtkResNode>(12);
//        node->AddEvent(AtkEventType.MouseClick, eventParamId, &args.GetAddon<AtkUnitBase>()->AtkEventListener, null, false);
//    }

//    private void HandleEvent(AddonEvent type, AddonArgs args) {
//        // if eventparam is the normal one, set it to 0 to not fire
//        if (args is AddonReceiveEventArgs { EventParam: var param }) {
//            if (param == eventParamId)
//                RepairAll();
//            else
//                ((AddonReceiveEventArgs)args).EventParam = 0;
//        }
//        else Svc.Log.Warning($"not receive event args");
//    }

//    private void RepairAll() {
//        Svc.Log.Info($"calling repair all");
//        //foreach (var inv in RepairCategory.Values)
//        //    GameMain.ExecuteCommand(CommandFlag.RepairAllItemsNPC.Value, inv.Value);
//    }
//}
