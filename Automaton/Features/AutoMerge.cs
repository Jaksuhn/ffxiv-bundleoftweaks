using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;

namespace Automaton.Features;

[Tweak]
public class AutoMerge : Tweak
{
    public override string Name => "Auto Merge";
    public override string Description => "Merge incomplete stacks upon opening your inventory.";

    private Dictionary<uint, Item> Sheet { get; set; } = null!;
    private readonly List<InventorySlot> inventorySlots = [];
    private readonly List<string> inventoryAddonNames =
        ["InventoryGrid0E", "InventoryGrid1E", "InventoryGrid2E", "InventoryGrid3E", "InventoryExpansion", // open all
        "Inventory", "InventoryGrid", "InventoryGridCrystal", // normal
        "InventoryEventGrid0", "InventoryEventGrid1", "InventoryEventGrid2", "InventoryGrid0", "InventoryGrid1", "InventoryLarge"]; // expanded

    public override void Enable()
    {
        Service.AddonObserver.AddonOpen += OnSetup;
        Sheet = GetSheet<Item>().ToDictionary(x => x.RowId, x => x);
    }

    public override void Disable()
    {
        Service.AddonObserver.AddonOpen -= OnSetup;
    }

    public class InventorySlot
    {
        public InventoryType Container { get; set; }
        public ushort Slot { get; set; }
        public uint ItemID { get; set; }
        public bool ItemHQ { get; set; }
    }

    private unsafe void OnSetup(string addonName)
    {
        try
        {
            if (Player.IsBusy || !inventoryAddonNames.Contains(addonName)) return;

            inventorySlots.Clear();
            var inv = InventoryManager.Instance();
            for (var container = InventoryType.Inventory1; container <= InventoryType.Inventory4; container++)
            {
                var invContainer = inv->GetInventoryContainer(container);
                for (var i = 1; i <= invContainer->Size; i++)
                {
                    var item = invContainer->GetInventorySlot(i - 1);
                    if (item->Flags.HasFlag(InventoryItem.ItemFlags.Collectable)) continue;
                    if (item->Quantity == Sheet[item->ItemId].StackSize || item->ItemId == 0) continue;

                    var slot = new InventorySlot()
                    {
                        Container = container,
                        ItemID = item->ItemId,
                        Slot = (ushort)item->Slot,
                        ItemHQ = item->Flags.HasFlag(InventoryItem.ItemFlags.HighQuality)
                    };

                    inventorySlots.Add(slot);
                }
            }

            foreach (var item in inventorySlots.GroupBy(x => new { x.ItemID, x.ItemHQ }).Where(x => x.Count() > 1))
            {
                var firstSlot = item.First();
                for (var i = 1; i < item.Count(); i++)
                {
                    var slot = item.ToList()[i];
                    inv->MoveItemSlot(slot.Container, slot.Slot, firstSlot.Container, firstSlot.Slot, true);
                }
            }
        }
        catch (Exception ex) { ex.Log(); }
    }
}
