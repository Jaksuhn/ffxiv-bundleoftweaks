using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Component.Excel;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using Task = System.Threading.Tasks.Task;
using TerritoryIntendedUse = FFXIVClientStructs.FFXIV.Client.Enums.TerritoryIntendedUse;

namespace ComplexTweaks.Tweaks;

internal class AutoEquipXPBoosts : Tweak {
    public override string Name => "Auto Equip Exp Items";
    public override string Description => "Automatically equips any exp boosting item on level change.";

    public override void OnEnable() => IClientState.Get().LevelChanged += CheckForLevelSync;
    public override void OnDisable() => IClientState.Get().LevelChanged -= CheckForLevelSync;

    private readonly List<ExpItem> _expItems =
    [
        new ExpItem(2634, 10, 20), // Helm of Light
        new ExpItem(8567, 25, 20), // Friendship Circlet
        new ExpItem(14043, 30, 30), // Brand-new ring
        new ExpItem(16039, 50, 30), // Ala Mhigan earrings
        new ExpItem(24589, 70, 30), // Aetheryte earring
        new ExpItem(31393, 80, 10), // Bozjan earrings
        new ExpItem(33648, 80, 30), // Menphina's earring
        new ExpItem(41081, 90, 30), // Azeyma's earrings
        new ExpItem(44410, 60, 30), // Neophyte's ring
        new ExpItem(52439, 100, 30), // Halone's earring
    ];

    private void CheckForLevelSync(uint classJobId, uint level) {
        if (!IPlayerState.Get().IsInDuty) return;
        var expItems = _expItems.GroupBy(x => x.GameData.Value.EquipSlotCategory.RowId)
            .Where(group => group.Any(x => level <= x.MaxLevel && x.GameData.Value.Handle.HasItem))
            .Select(group => group.Where(x => level <= x.MaxLevel && x.GameData.Value.Handle.HasItem)
            .OrderByDescending(x => x.GameData.Value.LevelItem.RowId)
            .ThenByDescending(x => x.Percent)
            .First()).ToList();
        Svc.Automation.Start(new EquipItems(expItems));
    }

    private readonly unsafe struct ExpItem(uint ItemId, int MaxLevel, int Percent) {
        public RowRef<Item> GameData { get; init; } = Item.GetRowRef(ItemId);
        public int MaxLevel { get; init; } = MaxLevel;
        public int Percent { get; init; } = Percent;
        public readonly ExcelRow* Row = Framework.Instance()->ExcelModuleInterface->ExdModule->GetRowBySheetIndexAndRowIndex(10, ItemId);

        public ItemHandle Handle => (ItemHandle)GameData;
    }

    private sealed class EquipItems(List<ExpItem> expItems) : TaskBase {
        protected override async Task Execute() {
            using var scope = BeginScope(nameof(EquipItems));
            await WaitWhileBusy();
            if (IPlayerState.Get().TerritoryIntendedUse is not (TerritoryIntendedUse.Dungeon or TerritoryIntendedUse.Raid1 or TerritoryIntendedUse.Raid2 or TerritoryIntendedUse.AllianceRaid)) return;
            if (IPlayerState.Get().ContentFinderCondition is { Value.ContentType.RowId: 28 }) return; // skip ults

            foreach (var expItem in expItems) {
                if (!expItem.Handle.CanEquip(out var errorMsg)) {
                    Log($"Can't equip [#{expItem.GameData.RowId}] {expItem.GameData.Value.Name}: {errorMsg.Value.Text}");
                    continue;
                }
                await WaitWhileBusy();
                await WaitUntil(() => ICondition.Get().HasPermission([109, 134]), "WaitForPermission");
                expItem.Handle.Equip();
            }
        }
    }
}
