using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using System.Threading.Tasks;

namespace Automaton.Tasks;
public sealed class AutoDeliveroo : CommonTasks
{
    protected override async Task Execute()
    {
        Status = "Going to GC";
        await GoToGC();
        Status = "Turning in Gear";
        await TurnIn();
        Status = "Going Home";
        await GoHome();
    }

    private async Task GoToGC()
    {
        using var scope = BeginScope("GoToGC");
        Service.Lifestream.ExecuteCommand("gc");
        await WaitUntilThenFalse(() => Service.Lifestream.IsBusy(), $"{nameof(GoToGC)}");
    }

    /*
     * Problems with this approach:
     * - Inventory outside of armoury chest isn't considered
     * - Could potentially overwrite gearsets on valuable characters (meant for an alt-only thing where they can gear up based on what they bring back from ventures)
     */
    private async Task EquipRecommended()
    {
        using var scope = BeginScope("EquipRecommended");
        var updating = false;
        unsafe
        {
            var mod = RecommendEquipModule.Instance();
            if (mod == null) return;
            updating = mod->IsUpdating;
        }
        await WaitUntil(() => !updating, $"WaitingFor{nameof(RecommendEquipModule)}Update");

        unsafe
        {
            var mod = RecommendEquipModule.Instance();
            var equippedItems = InventoryManager.Instance()->GetInventoryContainer(InventoryType.EquippedItems);
            var isAllEquipped = true;
            foreach (var recommendedItemPtr in mod->RecommendedItems)
            {
                var recommendedItem = recommendedItemPtr.Value;
                if (recommendedItem == null || recommendedItem->ItemId == 0)
                    continue;

                var isEquipped = false;
                for (var i = 0; i < equippedItems->Size; ++i)
                {
                    var equippedItem = equippedItems->Items[i];
                    if (equippedItem.ItemId != 0 && equippedItem.ItemId == recommendedItem->ItemId)
                    {
                        isEquipped = true;
                        break;
                    }
                }

                if (!isEquipped)
                    isAllEquipped = false;
            }

            if (!isAllEquipped)
                mod->EquipRecommendedGear();

        }
        await WaitUntil(() => !PlayerEx.IsBusy, $"WaitingForNotBusy");
    }

    private async Task TurnIn()
    {
        using var scope = BeginScope("TurnIn");
        Svc.Commands.ProcessCommand("/deliveroo enable");
        await WaitUntilThenFalse(() => Service.Deliveroo.IsTurnInRunning(), $"{nameof(TurnIn)}");
    }

    private async Task GoHome()
    {
        using var scope = BeginScope("GoHome");
        Service.Lifestream.ExecuteCommand("auto");
        await WaitUntilThenFalse(() => Service.Lifestream.IsBusy(), $"{nameof(GoHome)}");
    }
}
