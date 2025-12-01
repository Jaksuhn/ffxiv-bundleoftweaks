using ECommons.ExcelServices;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using Lumina.Excel.Sheets;
using System.Threading.Tasks;

namespace ComplexTweaks.Tasks;

public sealed class BuyCeruleumTanks : CommonTasks {
    private const uint CeruleumTankId = 10155;
    private const uint MammetVoyagerENpcId = 1011274;
    private readonly Memory.FreeCompanyDialogIPCReceive ipc = new();

    protected override async Task Execute() {
        await GoToWorkshop();
        await WaitUntil(Game.IsTerritoryLoaded, "WaitingForTerritoryToLoad");
        var npc = Game.GetNPCInfo(MammetVoyagerENpcId, Player.Territory, CeruleumTankId);
        ErrorIf(npc == null, $"Failed to find NPC {MammetVoyagerENpcId} in {Player.Territory}");
        ErrorIf(npc!.ShopId == 0, $"Failed to find shop for NPC {MammetVoyagerENpcId} in {Player.Territory}");

        Status = $"Moving to {npc.Location}";
        await MoveToDirectly(npc.Location, 0.5f);
        await BuyFromFccShop(MammetVoyagerENpcId, npc!.ShopId, CeruleumTankId, 999 - Inventory.GetItemCount(CeruleumTankId, false));
    }

    private async Task GoToWorkshop() {
        using var scope = BeginScope("GoToWorkshop");
        static bool PlayerInWorkshop() => GetRow<TerritoryType>(Player.Territory) is { } t && t.BGM.RowId == 328;
        if (PlayerInWorkshop()) return; // already there

        if (Player.TerritoryIntendedUse == TerritoryIntendedUseEnum.Housing_Instances) {
            await EnterWorkshop();
            return;
        }

        Status = "Teleporting to FC";
        Service.Lifestream.ExecuteCommand("fc");
        await WaitUntilThenFalse(() => Service.Lifestream.IsBusy(), $"LifestreamWaitForFinish");
        if (EstateHallDoor is { } door) {
            await MoveTo(door.Position, MovementConfig.InteractRange);
            await InteractWith(door, () => Player.TerritoryIntendedUse == TerritoryIntendedUseEnum.Housing_Instances, null, UiSkipOptions.YesNo);
            await WaitWhile(() => !Game.IsTerritoryLoaded(), "WaitingForTerritoryToLoad");
            await EnterWorkshop();
        }
        else
            Error("Failed to find estate hall door");
    }

    private async Task EnterWorkshop() {
        using var scope = BeginScope("EnterWorkshop");
        ErrorIf(Player.TerritoryIntendedUse != TerritoryIntendedUseEnum.Housing_Instances, "Not in a house");
        if (WorkshopDoor is { } door) {
            await MoveTo(door.Position, MovementConfig.InteractRange);
            await InteractWith(door, () => GetRow<TerritoryType>(Player.Territory) is { BGM.RowId: 328 }, 0);
            await WaitUntil(Game.IsTerritoryLoaded, "WaitingForTerritoryToLoad");
        }
        else
            Error("Failed to find workshop door");
    }

    private async Task BuyFromFccShop(ulong vendorInstanceId, uint shopId, uint itemId, int count) {
        using var scope = BeginScope("Buy");
        Status = "Opening shop";
        if (!Game.AddonActive("FreeCompanyCreditShop")) {
            Log("Opening shop...");
            ErrorIf(!Game.OpenShop(vendorInstanceId, shopId), $"Failed to open shop {vendorInstanceId:X}.{shopId:X}");
            await WaitWhile(() => !Game.AddonActive("FreeCompanyCreditShop"), "WaitForFCCShop");
            await WaitWhile(() => !Svc.Condition[ConditionFlag.OccupiedInEvent], "WaitForCondition");
        }

        Log("Buying...");
        if (TryGetAddonMaster<AddonMaster.FreeCompanyCreditShop>(out var am)) {
            var tanks = am.Items.First(x => x.ItemId == itemId);
            while (count > 0) {
                Status = $"Buying x{count} ceruleum tanks";
                tanks.Buy(Math.Min(count, tanks.MaxPurchaseSize));
                count -= tanks.MaxPurchaseSize;
                await WaitUntilSkipping(() => GetAddonTankCount() != Inventory.GetItemCount(tanks.ItemId, false), "WaitingForPurchase", UiSkipOptions.YesNo);
                Status = "Waiting for purchase to go through";
                // I could just wait until the atkvalue equals the real inventory count again but this was a fun experiment.
                using var stop = new OnDispose(ipc.FreeCompanyDialogPacketReceiveHook.Disable);
                await WaitUntilServerIPC();
            }
        }

        Status = "Closing shop";
        Log("Closing shop...");
        unsafe bool Close() => am.Base->Close(true);
        ErrorIf(!Close(), $"Failed to close shop {vendorInstanceId:X}.{shopId:X}");
        await WaitWhile(() => Game.AddonActive("FreeCompanyCreditShop"), "WaitForClose");
        await WaitWhile(() => Svc.Condition[ConditionFlag.OccupiedInEvent], "WaitForCondition");
        await NextFrame();
    }

    private async Task WaitUntilServerIPC() {
        using var scope = BeginScope("WaitForPacketFreeCompanyDialog");
        ipc.FreeCompanyDialogPacketReceiveHook.Enable();
        var lastPacketTimestamp = ipc.LastPacketTimestamp;
        while (ipc.LastPacketTimestamp == lastPacketTimestamp) {
            Log($"waiting...");
            await NextFrame();
        }
        ipc.FreeCompanyDialogPacketReceiveHook.Disable();
    }

    private unsafe DGameObject? EstateHallDoor => Svc.Objects.Where(o => o!.IsTargetable && o.EventInfo() is { EventId.ContentId: EventHandlerContent.Warp, EventId.Id: 131148 }).OrderBy(Player.DistanceTo).FirstOrDefault(defaultValue: null);
    private unsafe DGameObject? WorkshopDoor => Svc.Objects.FirstOrDefault(o => o!.IsTargetable && o.EventInfo() is { EventId.ContentId: EventHandlerContent.CustomTalk, EventId.Id: 721074 }, null);
    private int GetAddonTankCount() => TryGetAddonMaster<AddonMaster.FreeCompanyCreditShop>(out var am) ? am.Items.First(x => x.ItemId == CeruleumTankId).QuantityInInventory : 0;
}
