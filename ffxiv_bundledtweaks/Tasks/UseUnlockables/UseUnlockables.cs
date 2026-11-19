using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using System.Threading.Tasks;

namespace ComplexTweaks.Tasks;

[RunnableTask]
public sealed class UseUnlockables : TaskBase {
    protected override async Task Execute() {
        using var scope = BeginScope(nameof(UseUnlockables));
        var items = GetPending();
        if (items.Count == 0) {
            Log("Nothing to unlock.");
            return;
        }

        int used = 0, failed = 0;
        foreach (var item in items) {
            Status = $"[{used + failed + 1}/{items.Count}] {item.Name}";
            if (await TryUnlock(item))
                used++;
            else {
                failed++;
                Warning($"Failed to unlock {item.Name} ({item.RowId})");
            }
        }
        Log($"Unlocked {used}/{items.Count}{(failed > 0 ? $", {failed} failed" : "")}.");
    }

    private async Task<bool> TryUnlock(Item item) {
        using var scope = BeginScope($"{nameof(TryUnlock)}:{item.RowId}");
        await WaitWhile(() => IObjectTable.Get().LocalPlayer.IsBusy || InventoryManager.IsUpdating, "WaitForIdle", timeout: TimeSpan.FromSeconds(30), abortOnTimeout: false);
        await WaitUntil(() => GetStatus(item.RowId) == 0, "WaitForUsable", timeout: TimeSpan.FromSeconds(5), abortOnTimeout: false);
        if (GetStatus(item.RowId) is var status and not 0) {
            Log($"Not usable (status {status})");
            return false;
        }

        UseItem(item.RowId);
        var deadline = Environment.TickCount64 + 15_000;
        while (!IUnlockState.Get().IsItemUnlocked(item)) {
            if (Environment.TickCount64 > deadline)
                return false;
            if (AtkUnitBase.IsAddonReady("SelectYesno"))
                AddonSelectYesno.Yes();
            await NextFrame(100);
        }
        return true;
    }

    private static List<Item> GetPending() => [.. InventoryType.Bags.SelectMany(type => type.Items)
        .Where(i => i.ItemId != 0 && i.GameData.IsValid && IUnlockState.Get().IsItemUnlockable(i.GameData.Value) && !IUnlockState.Get().IsItemUnlocked(i.GameData.Value))
        .Select(i => i.GameData.Value)];

    private static unsafe uint GetStatus(uint itemId) => ActionManager.Instance()->GetActionStatus(ActionType.Item, itemId);
    private static unsafe void UseItem(uint itemId) => ActionManager.Instance()->UseAction(ActionType.Item, itemId, extraParam: 65535);
}
