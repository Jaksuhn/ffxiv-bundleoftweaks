using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Fates;
using Dalamud.Game.Inventory;
using Dalamud.Game.NativeWrapper;
using ECommons;
using ECommons.ImGuiMethods;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace ComplexTweaks.Utilities.Extensions;

public static unsafe class DalamudExtensions {
    extension(GameInventoryItem item) {
        public RowRef<Item> GameData => GetGameData(item);
        internal RowRef<Item> GetGameData() => GenericHelpers.CreateRowRef<Item>(item.BaseItemId);
    }

    public static AtkUnitBase* ToPtr(this AddonArgs args) => (AtkUnitBase*)args.Addon.Address;
    public static AtkUnitBase* ToPtr(this AtkUnitBasePtr wrapper) => (AtkUnitBase*)wrapper.Address;
    public static InventoryItem* ToPtr(this GameInventoryItem item) => (InventoryItem*)item.Address;

    public static AtkEvent* GenerateEvent(this AddonArgs args) {
        var atkUnit = args.ToPtr();
        var evt = new AtkEvent() { Listener = &args.ToPtr()->AtkEventListener, Target = &AtkStage.Instance()->AtkEventTarget };
        return &evt;
    }

    public static AtkEventData* GenerateEventData(this AddonArgs args) {
        var data = new AtkEventData();
        return &data;
    }

    public static void ReceiveEvent(this AddonArgs args, AtkEventType eventType, int eventParam, AtkEvent* atkEvent, AtkEventData* atkEventData = null)
        => args.ToPtr()->ReceiveEvent(eventType, eventParam, atkEvent, atkEventData);

    public static bool AllTargetable(this IPartyList party) => party.All(p => p.GameObject?.IsTargetable ?? false);

    public static unsafe FateContext* Struct(this IFate fate) => (FateContext*)fate.Address;

    public static uint? EventItem(this IFate fate) => fate.GameData.Value.EventItem.RowId is not 0 ? fate.GameData.Value.EventItem.RowId : null;
    public static int EventItemInventoryCount(this IFate fate) => fate.EventItem() is { } item ? Inventory.GetItemCount(item) : 0;
    public static DGameObject? GetMotivationNpc(this IFate fate) => Svc.Objects.FirstOrDefault(o => o.EntityId == fate.Struct()->MotivationNpc);

    public static void Print(this IFate fate) {
        ImGui.TextColored(Colors.Grey3, $"[{fate.FateId}]");
        ImGui.SameLine();
        ImGui.TextColored(EzColor.White, $"{fate.Name}");

        ImGui.Indent();

        ImGui.FieldAndValue("State", fate.State);
        ImGui.FieldAndValue("Position", fate.Position);
        ImGui.FieldAndValue("Progress", $"{fate.Progress}%");
        ImGui.FieldAndValue("Time Remaining", $"{TimeSpan.FromSeconds(fate.TimeRemaining):mm\\:ss} / {TimeSpan.FromSeconds(fate.Duration):mm\\:ss}");
        ImGui.FieldAndValue("Level", $"{fate.Level}-{fate.MaxLevel}");
        ImGui.FieldAndValue("Bonus", fate.HasBonus);
        ImGui.FieldAndValue("Distance To", Player.DistanceTo(fate.Position));
        ImGui.FieldAndValue("Hand In Count", fate.HandInCount);
        ImGui.FieldAndValue("Event Item", fate.GameData.ValueNullable?.EventItem.ValueNullable?.Print() ?? "N/A", fate.GameData.ValueNullable?.EventItem.RowId != 0);
        ImGui.FieldAndValue("Event Item Count", fate.EventItemInventoryCount());
        ImGui.FieldAndValue("Distance to Aetheryte", Vector3.Distance(fate.Position, Coords.AetherytePosition(Coords.FindClosestAetheryte(fate.TerritoryType.RowId, fate.Position) ?? 0)));
        ImGui.FieldAndValue("Worth Teleporting", Coords.IsTeleportingFaster(fate.Position));

        ImGui.Unindent();
    }

    public static string Stringify(this IFate fate) => $"[{fate.FateId}] {fate.Position} {fate.Progress}%% {TimeSpan.FromSeconds(fate.TimeRemaining):mm\\:ss} / {TimeSpan.FromSeconds(fate.Duration):mm\\:ss} [{fate.GameData.Value.Rule}]";
}
