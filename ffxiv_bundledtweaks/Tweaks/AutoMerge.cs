using FFXIVClientStructs.FFXIV.Client.Game;

namespace ComplexTweaks.Tweaks;

[Tweak]
public class AutoMerge : Tweak {
    public override string Name => "Auto Merge";
    public override string Description => "Merge incomplete stacks upon opening your inventory.";

    public override void Enable() => Svc.AddonLifecycle.RegisterListener(AddonEvent.PostShow, ["InventoryExpansion", "InventoryLarge", "Inventory"], OnSetup);
    public override void Disable() => Svc.AddonLifecycle.UnregisterListener(OnSetup);

    private unsafe void OnSetup(AddonEvent type, AddonArgs args) {
        try {
            if (Player.IsBusy || !Svc.Condition.CanMoveItems()) return;
            var inv = InventoryManager.Instance();

            var incompleteStacks = InventoryType.Bags
                .SelectMany(container => inv->GetItems(container))
                .Select(item => (ItemHandle)item)
                .Where(handle => handle.ItemId != 0 && !handle.IsCollectible && handle.ItemLocation!.GetInventoryItem()->Quantity < handle.GameData.ValueNullable?.StackSize)
                .GroupBy(handle => new { handle.ItemId, handle.IsHighQuality })
                .Where(group => group.Count() > 1);

            foreach (var group in incompleteStacks) {
                var firstSlot = group.First();
                if (firstSlot.ItemLocation == null) continue;

                foreach (var slot in group.Skip(1)) {
                    if (slot.ItemLocation == null) continue;
                    inv->MoveItemSlot(slot.ItemLocation.Container, slot.ItemLocation.Slot,
                        firstSlot.ItemLocation.Container, firstSlot.ItemLocation.Slot, true);
                }
            }
        }
        catch (Exception ex) { ex.Log(); }
    }
}
