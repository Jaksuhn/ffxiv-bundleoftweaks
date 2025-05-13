using Dalamud.Game.Inventory;
using Dalamud.Game.Inventory.InventoryEventArgTypes;
using Dalamud.Plugin.Ipc.Exceptions;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using System.Threading.Tasks;
using ValueType = FFXIVClientStructs.FFXIV.Component.GUI.ValueType;

namespace Automaton.Tasks;
public sealed class AutoDeliveroo(bool equipRecommendations) : CommonTasks()
{
    protected override async Task Execute()
    {
        Status = "Going to GC";
        await GoToGC();
        if (equipRecommendations)
        {
            Status = "Updating Gearsets";
            await EquipGearsetterUpgrades();
        }
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

    private async Task EquipGearsetterUpgrades()
    {
        using var scope = BeginScope("EquipGearsetterUpgrades");

        try
        {
            var test = GetGearsetRecommendations();
        }
        catch (IpcNotReadyError)
        {
            Log($"Skipping {nameof(EquipGearsetterUpgrades)}, {nameof(GearsetterIPC)} not ready.");
            return;
        }

        foreach (var gearset in GetValidGearsets())
            await ProcessGearset(gearset);
    }

    private async Task ProcessGearset(byte gearset)
    {
        using var scope = BeginScope("ProcessGearset");
        var recommendations = GetGearsetRecommendations(gearset);
        if (recommendations.Count == 0)
        {
            Log($"Skipping gearset #{gearset} {GetGearsetName(gearset)}: no recommendations.");
            return;
        }

        Log($"Recommendations: {string.Join(", ", recommendations)}");

        if (!TryEquipGearset(gearset))
        {
            Error($"Failed to equip gearset #{gearset}");
            return;
        }

        Log($"Equipped gearset #{gearset} {GetGearsetName(gearset)}");
        await WaitUntil(() => Player.JobId == GetGearsetClassJob(gearset), "WaitForJobChange");

        foreach (var (itemId, sourceInventoryType, sourceInventorySlot, targetEquipSlot) in recommendations)
        {
            await ProcessItem(itemId, sourceInventoryType, sourceInventorySlot, targetEquipSlot);
            await NextFrame();
        }

        UpdateCurrentGearset();
    }

    private async Task ProcessItem(uint itemId, InventoryType? sourceInventoryType, byte? sourceInventorySlot, RaptureGearsetModule.GearsetItemIndex targetEquipSlot)
    {
        using var scope = BeginScope("ProcessItem");
        if (sourceInventoryType is not { } cont || sourceInventorySlot is not { } slot)
        {
            Log($"Skipping #{itemId}. inv?: {sourceInventoryType is null}; slot?: {sourceInventorySlot is null}");
            return;
        }

        if (GetRow<Item>(itemId) is not { } item)
        {
            Error($"Item #{itemId} not found");
            return;
        }

        await EquipItem(item, cont, slot, (uint)targetEquipSlot);
    }

    private async Task EquipItem(Item item, InventoryType sourceContainer, byte sourceSlot, uint targetSlot)
    {
        using var scope = BeginScope("EquipItem");
        var equipItem = FindItem(item, sourceContainer, sourceSlot, out var discardItem);
        if (discardItem is { })
            await HandleDiscardFirst(discardItem);

        var dest = new Inventory.InventoryContainerWrapper(InventoryType.EquippedItems);
        if (equipItem.LocationODR is { Page: var page, Slot: var slot })
        {
            Log($"Equipping {equipItem} to slot #{targetSlot}");
            MoveItem(equipItem.Type + page, slot, targetSlot);
            await WaitUntil(() => dest.Contains(equipItem), "WaitingForItemInContainer");
        }
        else Warning($"Failed to find {equipItem} location");
    }

    private async Task HandleDiscardFirst(Inventory.InventoryItemWrapper item)
    {
        using var scope = BeginScope("HandleDiscard");
        Log($"Upgrade item requires free armoury slot to equip");
        if (item.LocationODR is { Page: var page, Slot: var slot })
        {
            foreach (var cont in Inventory.PlayerInventoryNoKeyItems)
            {
                if (new Inventory.InventoryContainerWrapper(cont) is { EmptySlots: > 1, FirstEmptySlotODR: uint destSlot } dest)
                {
                    Log($"Moving {item} [{item.Container} -> {dest}]");
                    MoveItem(item.Type + page, slot, destSlot, dest.Type);
                    await WaitUntil(() => dest.Contains(item), "WaitingForItemInContainer");
                    return;
                }
            }
            Error($"Failed to find free inventory slot to move {item}");
        }
    }

    private async Task InventoryChange(int timeoutMs = 5000)
    {
        using var scope = BeginScope("InventoryChange");
        var tcs = new TaskCompletionSource();
        void OnItemMoved(GameInventoryEvent type, InventoryEventArgs data)
        {
            Log($"Inventory changed: {type} {data}");
            tcs.TrySetResult();
        }

        Svc.GameInventory.ItemMoved += OnItemMoved;
        try
        {
            using var reg = CancelToken.Register(() => tcs.TrySetCanceled());
            var timeoutTask = Task.Delay(timeoutMs, CancelToken);
            var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);

            if (completedTask == timeoutTask)
                Error($"Inventory change timed out after {timeoutMs}ms");
        }
        finally
        {
            Svc.GameInventory.ItemMoved -= OnItemMoved;
        }
    }

    private unsafe string GetGearsetName(byte? index = null) => RaptureGearsetModule.Instance()->GetGearset(index ?? RaptureGearsetModule.Instance()->CurrentGearsetIndex)->NameString;
    private unsafe byte GetGearsetClassJob(byte? index = null) => RaptureGearsetModule.Instance()->GetGearset(index ?? RaptureGearsetModule.Instance()->CurrentGearsetIndex)->ClassJob;
    private unsafe bool TryEquipGearset(byte id)
        => RaptureGearsetModule.Instance()->CurrentGearsetIndex == id || RaptureGearsetModule.Instance()->EquipGearset(id) == 0;

    private unsafe List<(uint itemId, InventoryType? inventoryType, byte? sourceInventorySlot, RaptureGearsetModule.GearsetItemIndex targetSlot)> GetGearsetRecommendations(byte? index = null)
        => Service.Gearsetter.GetRecommendationsForGearset(index ?? (byte)RaptureGearsetModule.Instance()->CurrentGearsetIndex);

    private Inventory.InventoryItemWrapper FindItem(Item item, InventoryType sourceContainer, byte sourceSlot, out Inventory.InventoryItemWrapper? discardItem)
    {
        var wrapper = new Inventory.InventoryItemWrapper(item); // do not initiate by location, it can wind up with the wrong item
        if (new Inventory.InventoryContainerWrapper(wrapper.ArmouryContainer) is { EmptySlots: 0 } container)
        {
            discardItem = container.FirstNonGearset;
            return wrapper;
        }

        discardItem = null;
        return wrapper;
    }

    private unsafe void MoveItem(InventoryType sourceInventory, uint sourceSlot, uint equipSlot, InventoryType? destInventory = null)
    {
        var sourceContainerId = GetContainerId(sourceInventory);
        var destinationContainerId = GetContainerId(destInventory ?? InventoryType.EquippedItems);
        if (sourceContainerId == 0 || destinationContainerId == 0) return;

        unsafe
        {
            var eis = stackalloc AtkValue[4];
            var dropOut = stackalloc byte[32];
            for (var i = 0; i < 4; i++) eis[i].Type = ValueType.UInt;
            eis[0].UInt = sourceContainerId;
            eis[1].UInt = sourceSlot;
            eis[2].UInt = destinationContainerId;
            eis[3].UInt = destinationContainerId == GetContainerId(InventoryType.EquippedItems) && equipSlot > 5 ? equipSlot - 1 : equipSlot; // account for belts not existing anymore
            var atkModule = RaptureAtkModule.Instance();
            if (Service.Memory.MoveItem is { } moveItem)
            {
                Log($"MoveItem {eis[0].UInt}:{eis[1].UInt} -> {eis[2].UInt}:{eis[3].UInt}");
                moveItem.Invoke(atkModule, dropOut, eis);
            }
            else Error($"MoveItem delegate not found");
        }
    }

    private unsafe bool ItemIsEquipped(uint itemId, int slot) => InventoryManager.Instance()->GetInventoryContainer(InventoryType.EquippedItems)->Items[slot].ItemId == itemId;
    private unsafe void UpdateCurrentGearset() => RaptureGearsetModule.Instance()->UpdateGearset(RaptureGearsetModule.Instance()->CurrentGearsetIndex);

    private unsafe List<byte> GetValidGearsets()
    {
        var gm = RaptureGearsetModule.Instance();
        if (gm is null) return [];
        List<byte> gearsets = [];
        for (byte i = 0; i < 100; ++i)
        {
            if (!gm->IsValidGearset(i)) continue;
            var gearset = gm->GetGearset(i);
            if (gearset != null && gearset->Flags.HasFlag(RaptureGearsetModule.GearsetFlag.Exists) && GetRow<ClassJob>(gearset->ClassJob)?.Unknown8 != 0)
                if (gearset->NameString.Split((char)0)[0] is { } name && !name.ContainsAny(StringComparison.OrdinalIgnoreCase, "Eureka", "Bozja", "_"))
                    gearsets.Add(i);
        }
        return gearsets;
    }

    private uint GetContainerId(InventoryType inventoryType) => inventoryType switch
    {
        InventoryType.Inventory1 => 48,
        InventoryType.Inventory2 => 49,
        InventoryType.Inventory3 => 50,
        InventoryType.Inventory4 => 51,
        InventoryType.ArmoryMainHand => 57,
        InventoryType.ArmoryHead => 58,
        InventoryType.ArmoryBody => 59,
        InventoryType.ArmoryHands => 60,
        InventoryType.ArmoryLegs => 61,
        InventoryType.ArmoryFeets => 62,
        InventoryType.ArmoryOffHand => 63,
        InventoryType.ArmoryEar => 64,
        InventoryType.ArmoryNeck => 65,
        InventoryType.ArmoryWrist => 66,
        InventoryType.ArmoryRings => 67,
        InventoryType.ArmorySoulCrystal => 68,
        InventoryType.EquippedItems => 4,
        _ => 0
    };
}
