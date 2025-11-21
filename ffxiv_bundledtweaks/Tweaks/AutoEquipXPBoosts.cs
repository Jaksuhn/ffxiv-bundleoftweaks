using ComplexTweaks.Tasks;
using ECommons;
using ECommons.ExcelServices;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Component.Excel;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using Task = System.Threading.Tasks.Task;

namespace ComplexTweaks.Tweaks;

[Tweak]
internal class AutoEquipXPBoosts : Tweak
{
    public override string Name => "Auto Equip Exp Items";
    public override string Description => "Automatically equips any exp boosting item on level change.";

    public override void Enable() => Svc.ClientState.LevelChanged += CheckForLevelSync;
    public override void Disable() => Svc.ClientState.LevelChanged -= CheckForLevelSync;

    private static RowRef<Item> RowRef(uint id) => GenericHelpers.CreateRowRef<Item>(id);

    private readonly List<ExpItem> _expItems =
    [
        new ExpItem(RowRef(14043), 30, 30), // Brand-new ring
        new ExpItem(RowRef(16039), 50, 30), // Ala Mhigan earrings
        new ExpItem(RowRef(24589), 70, 30), // Aetheryte earring
        new ExpItem(RowRef(31393), 80, 10), // Bozjan earrings
        new ExpItem(RowRef(33648), 80, 30), // Menphina's earring
        new ExpItem(RowRef(41081), 90, 30), // Azeyma's earrings
        new ExpItem(RowRef(44410), 60, 30), // Neophyte's ring
    ];

    private unsafe void CheckForLevelSync(uint classJobId, uint level)
    {
        var expItems = _expItems.GroupBy(x => x.Item.Value.EquipSlotCategory.RowId)
            .Where(group => group.Any(x => level <= x.MaxLevel && Inventory.HasItem(x.Item.RowId)))
            .Select(group => group.Where(x => level <= x.MaxLevel && Inventory.HasItem(x.Item.RowId))
            .OrderByDescending(x => x.Item.Value.LevelItem.RowId)
            .ThenByDescending(x => x.Percent)
            .First()).ToList();
        Service.Automation.Start(new EquipItems(expItems));
    }

    private readonly unsafe struct ExpItem(RowRef<Item> Item, int MaxLevel, int Percent)
    {
        public RowRef<Item> Item { get; init; } = Item;
        public int MaxLevel { get; init; } = MaxLevel;
        public int Percent { get; init; } = Percent;
        public readonly ExcelRow* Row = Framework.Instance()->ExcelModuleInterface->ExdModule->GetRowBySheetIndexAndRowIndex(10, Item.RowId);
    }

    private sealed class EquipItems(List<ExpItem> expItems) : CommonTasks
    {
        protected override async Task Execute()
        {
            using var scope = BeginScope("EquipItems");
            await WaitUntil(() => Player.ReadyAndLoaded, "WaitForLoad");
            if (Player.TerritoryIntendedUse is not (TerritoryIntendedUseEnum.Dungeon or TerritoryIntendedUseEnum.Raid or TerritoryIntendedUseEnum.Raid_2 or TerritoryIntendedUseEnum.Alliance_Raid)) return;
            if (GetRow<ContentFinderCondition>(Player.CurrentCfc) is { ContentType.RowId: 28 }) return; // skip ults

            foreach (var expItem in expItems)
            {
                unsafe
                {
                    if (Service.Memory.CanEquip?.Invoke(expItem.Item.RowId, Player.Race, Player.Sex, (ushort)Player.Level, (byte)Player.JobId, (byte)Player.GrandCompany, Player.PvPRank, expItem.Row) ?? false)
                    {
                        Log($"Can't equip [#{expItem.Item.RowId}] {expItem.Item.Value.Name}");
                        continue;
                    }
                }
                await WaitUntil(() => Player.ReadyAndLoaded, "WaitForNotBusy");
                await WaitUntil(() => Game.HasPermission([109, 134]), "WaitForPermission");
                Log($"Equipping [#{expItem.Item.RowId}] {expItem.Item.Value.Name}");
                PlayerEx.Equip(expItem.Item.RowId);
            }
        }
    }
}
