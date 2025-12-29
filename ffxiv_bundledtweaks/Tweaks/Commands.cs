using ComplexTweaks.Tasks;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.Interop;
using System.Threading.Tasks;
using ValueType = FFXIVClientStructs.FFXIV.Component.GUI.ValueType;

namespace ComplexTweaks.Tweaks;

public class CommandsConfiguration {
    [BoolConfig(Label = "/tpflag")]
    public bool EnableTPFlag = false;

    [BoolConfig(Label = "/equip")]
    public bool EnableEquip = false;

    [BoolConfig(Label = "/desynth")]
    public bool EnableDesynth = false;

    [BoolConfig(Label = "/lowerquality")]
    public bool EnableLowerQuality = false;

    [BoolConfig(Label = "/item")]
    public bool EnableUseItem = false;

    [BoolConfig(Label = "/killflag")]
    public bool EnableKillFlag = false;

    [BoolConfig(Label = "/gotoflag")]
    public bool EnableGoToFlag = false;

    [BoolConfig(Label = "/travel")]
    public bool EnableTravel = false;
}

[Tweak]
public partial class Commands : Tweak<CommandsConfiguration> {
    public override string Name => "Commands";
    public override string Description => "Miscellanous commands";

    #region Teleport Flag
    [CommandHandler(["/tpf", "/tpflag"], "Teleport to the aetheryte nearest your flag", nameof(Config.EnableTPFlag))]
    internal void OnCommmandTeleportFlag(string command, string arguments) {
        if (Coords.FindClosestAetheryte(Player.MapFlag, false) is { } aetheryte)
            Coords.ExecuteTeleport(aetheryte);
    }
    #endregion

    #region Equip
    [CommandHandler("/equip", "Equip an item by ID", nameof(Config.EnableEquip))]
    internal unsafe void OnCommmandEquip(string command, string arguments) {
        if (!uint.TryParse(arguments, out var itemId)) return;
        var item = new ItemHandle(itemId);
        if (!item.TrySetItemLocation()) {
            DuoLog.Error($"Failed to find item {itemId} in inventory");
            return;
        }
        if (item.CanEquip(out var logMessage))
            item.Equip();
        else Svc.Log.Warning($"Unable to equip item {item}: {logMessage.Value.Text}");
    }
    #endregion

    #region Desynth
    [CommandHandler("/desynth", "Desynth an item by ID", nameof(Config.EnableDesynth))]
    internal unsafe void OnCommmandDesynth(string command, string arguments) {
        if (!uint.TryParse(arguments, out var itemId)) return;
        var item = new ItemHandle(itemId);
        if (!item.TrySetItemLocation()) {
            DuoLog.Error($"Failed to find item {item} in inventory");
            return;
        }

        if (item.GameData.Value.Desynth == 0) {
            DuoLog.Error($"Item {item} is not desynthable");
            return;
        }

        AgentSalvage.Instance()->SalvageItem(item.ItemLocation.GetInventoryItem());
        var retval = new AtkValue();
        Span<AtkValue> param = [
            new AtkValue { Type = ValueType.Int, Int = 0 },
            new AtkValue { Type = ValueType.Bool, Byte = 1 }
        ];
        AgentSalvage.Instance()->AgentInterface.ReceiveEvent(&retval, param.GetPointer(0), 2, 1);
    }
    #endregion

    #region Lower Quality
    [CommandHandler("/lowerquality", "Lower the quality of an item by ID, or pass all", nameof(Config.EnableLowerQuality))]
    internal unsafe void OnCommmandLowerQuality(string command, string arguments) {
        if (!uint.TryParse(arguments, out var itemId) && arguments != "all") return;
        if (arguments == "all") {
            if (AgentInventoryContext.Instance() == null) {
                Warning("AgentInventoryContext is null, cannot lower quality on items");
                return;
            }
            foreach (var i in InventoryManager.GetHqItems(InventoryType.Bags)) {
                // TODO: this still sometimes can just cause a crash, idk why
                Log($"Lowering quality on item [{i}] in {i.ItemLocation}");
                TaskManager.EnqueueDelay(250);
                TaskManager.Enqueue(() => AgentInventoryContext.Instance() != null, "Checking if AgentInventoryContext is null");
                TaskManager.Enqueue(() => !RaptureAtkModule.Instance()->AgentUpdateFlag.HasFlag(RaptureAtkModule.AgentUpdateFlags.InventoryUpdate), "checking for no inventory update");
                TaskManager.Enqueue(Svc.Condition.CanLowerItemQuality, "checking perm #135");
                TaskManager.Enqueue(() => AgentInventoryContext.Instance()->LowerItemQuality(i.ItemLocation!.GetInventoryItem(), i.ItemLocation.Container, i.ItemLocation.Slot, 0), $"Lowering quality on item [{i}] in {i.ItemLocation}");
            }
        }
        else {
            if (new ItemHandle(itemId) is ItemHandle item && item.TrySetItemLocation()) {
                Log($"Lowering quality on item [{item}] in {item.ItemLocation}");
                AgentInventoryContext.Instance()->LowerItemQuality(item.ItemLocation.GetInventoryItem(), item.ItemLocation.Container, item.ItemLocation.Slot, 0);
            }
        }
    }

    private class LowerQualityAll : TaskBase {
        protected override async Task Execute() {
            foreach (var i in InventoryManager.GetHqItems(InventoryType.Bags)) {
                while (!i.LowerItemQuality())
                    await NextFrame();
            }
        }
    }
    #endregion

    #region Use Item
    [CommandHandler("/item", "Use an item by ID", nameof(Config.EnableUseItem))]
    internal unsafe void OnCommandUseItem(string command, string arguments) {
        if (!uint.TryParse(arguments, out var itemId)) return;
        var agent = ActionManager.Instance();
        if (agent == null) return;

        agent->UseAction(itemId >= 2_000_000 ? ActionType.EventItem : ActionType.Item, itemId, extraParam: 65535);
    }
    #endregion

    #region Kill Flag
    [Requires(Ipc.BossMod | Ipc.Navmesh)]
    [CommandHandler(["/killflag", "/kf"], "Goes to flag, kills hunt mob at destination. Requires BossMod.", nameof(Config.EnableKillFlag))]
    internal unsafe void OnCommandKillFlag(string command, string arguments) => Service.Automation.Start(new KillFlag(arguments));
    #endregion

    #region Go to flag
    [Requires(Ipc.Navmesh)]
    [CommandHandler(["/gotoflag", "/gtf"], "Goes to flag location", nameof(Config.EnableGoToFlag))]
    internal void OnGoToFlagCommand(string command, string arguments) => Service.Automation.Start(new GoToFlagTask());

    private class GoToFlagTask : TaskBase {
        protected override async Task Execute() => await MoveTo(Player.MapFlag, MovementConfig.Everything);
    }
    #endregion

    #region World Travel
    //[CommandHandler("/travel", "Invoke world travel. Still have to be in a starting city.", nameof(Config.EnableTravel))]
    //private unsafe void OnTravelCommand(string command, string arguments) => AgentWorldTravel.Instance()->Travel(arguments);
    #endregion
}
