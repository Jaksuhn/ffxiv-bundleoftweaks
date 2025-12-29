using ECommons;
using ECommons.ExcelServices;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Component.Excel;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using Task = System.Threading.Tasks.Task;

namespace ComplexTweaks.Tweaks;

[Tweak]
internal class AutoEquipXPBoosts : Tweak {
    public override string Name => "Auto Equip Exp Items";
    public override string Description => "Automatically equips any exp boosting item on level change.";

    public override void Enable() => Svc.ClientState.LevelChanged += CheckForLevelSync;
    public override void Disable() => Svc.ClientState.LevelChanged -= CheckForLevelSync;

    private readonly List<ExpItem> _expItems =
    [
        new ExpItem(14043, 30, 30), // Brand-new ring
        new ExpItem(16039, 50, 30), // Ala Mhigan earrings
        new ExpItem(24589, 70, 30), // Aetheryte earring
        new ExpItem(31393, 80, 10), // Bozjan earrings
        new ExpItem(33648, 80, 30), // Menphina's earring
        new ExpItem(41081, 90, 30), // Azeyma's earrings
        new ExpItem(44410, 60, 30), // Neophyte's ring
    ];

    private unsafe void CheckForLevelSync(uint classJobId, uint level) {
        var expItems = _expItems.GroupBy(x => x.GameData.Value.EquipSlotCategory.RowId)
            .Where(group => group.Any(x => level <= x.MaxLevel && x.GameData.Value.Handle.HasItem))
            .Select(group => group.Where(x => level <= x.MaxLevel && x.GameData.Value.Handle.HasItem)
            .OrderByDescending(x => x.GameData.Value.LevelItem.RowId)
            .ThenByDescending(x => x.Percent)
            .First()).ToList();
        Service.Automation.Start(new EquipItems(expItems));
    }

    private readonly unsafe struct ExpItem(uint ItemId, int MaxLevel, int Percent) {
        public RowRef<Item> GameData { get; init; } = Item.GetRef(ItemId);
        public int MaxLevel { get; init; } = MaxLevel;
        public int Percent { get; init; } = Percent;
        public readonly ExcelRow* Row = Framework.Instance()->ExcelModuleInterface->ExdModule->GetRowBySheetIndexAndRowIndex(10, ItemId);

        public ItemHandle Handle => (ItemHandle)GameData;
    }

    private sealed class EquipItems(List<ExpItem> expItems) : TaskBase {
        protected override async Task Execute() {
            using var scope = BeginScope("EquipItems");
            await WaitWhile(() => Player.IsBusy, "WaitForLoad");
            if (Player.TerritoryIntendedUseEnum is not (TerritoryIntendedUseEnum.Dungeon or TerritoryIntendedUseEnum.Raid or TerritoryIntendedUseEnum.Raid_2 or TerritoryIntendedUseEnum.Alliance_Raid)) return;
            if (Player.ContentFinderCondition is { Value.ContentType.RowId: 28 }) return; // skip ults

            foreach (var expItem in expItems) {
                if (!expItem.Handle.CanEquip(out var errorMsg)) {
                    Log($"Can't equip [#{expItem.GameData.RowId}] {expItem.GameData.Value.Name}: {errorMsg.Value.Text}");
                    continue;
                }
                await WaitWhile(() => Player.IsBusy, "WaitForNotBusy");
                await WaitUntil(() => Svc.Condition.HasPermission([109, 134]), "WaitForPermission");
                expItem.Handle.Equip();
            }
        }
    }
}
