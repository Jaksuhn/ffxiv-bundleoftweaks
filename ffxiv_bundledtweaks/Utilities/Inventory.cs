using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.Interop;
using Lumina.Excel.Sheets;
using System.Diagnostics.CodeAnalysis;

namespace ComplexTweaks.Utilities;

public unsafe class Inventory
{
    public static readonly InventoryType[] PlayerInventoryNoKeyItems =
    [
        InventoryType.Inventory1,
        InventoryType.Inventory2,
        InventoryType.Inventory3,
        InventoryType.Inventory4,
    ];
    public static readonly InventoryType[] PlayerInventory = [.. PlayerInventoryNoKeyItems, InventoryType.KeyItems]; // include EquippedItems?

    public static readonly InventoryType[] MainOffHand =
    [
        InventoryType.ArmoryMainHand,
        InventoryType.ArmoryOffHand
    ];

    public static readonly InventoryType[] LeftSideArmory =
    [
        InventoryType.ArmoryHead,
        InventoryType.ArmoryBody,
        InventoryType.ArmoryHands,
        InventoryType.ArmoryLegs,
        InventoryType.ArmoryFeets
    ];

    public static readonly InventoryType[] RightSideArmory =
    [
        InventoryType.ArmoryEar,
        InventoryType.ArmoryNeck,
        InventoryType.ArmoryWrist,
        InventoryType.ArmoryRings
    ];

    public static readonly InventoryType[] Armory = [.. MainOffHand, .. LeftSideArmory, .. RightSideArmory, InventoryType.ArmorySoulCrystal];
    public static readonly InventoryType[] Equippable = [.. PlayerInventory, .. Armory];

    public class InventoryItemWrapper
    {
        public InventoryItemWrapper(Item item) => ItemId = item.RowId;
        public InventoryItemWrapper(uint itemId) => ItemId = itemId;
        public InventoryItemWrapper(InventoryItem item) => ItemId = InventoryManager.Instance()->GetInventoryContainer(item.Container)->GetInventorySlot(item.Slot)->ItemId;
        public InventoryItemWrapper(InventoryType inv, int slot) => ItemId = InventoryManager.Instance()->GetInventoryContainer(inv)->GetInventorySlot(slot)->ItemId;

        public uint ItemId { get; set; }
        public Item Item => GetRow<Item>(ItemId)!.Value;
        public InventoryItem* Pointer => HasItem ? InventoryManager.Instance()->GetInventoryContainer(Location.Value.Container)->GetInventorySlot(Location.Value.Slot) : null;
        public ItemOrderModuleSorter* Sorter => HasItem ? Location.Value.Container.GetSorter() : null;
        public InventoryType ArmouryContainer => Item.GetArmouryContainer();
        public InventoryType Type => Pointer->GetInventoryType();
        public InventoryContainerWrapper Container => new(Pointer->Container.GetContainer());

        [MemberNotNullWhen(true, nameof(Location))]
        public bool HasItem => Location is not null;
        public (InventoryType Container, int Slot)? Location => GetItemLocationInInventory(ItemId, Equippable);
        public (uint Page, uint Slot)? LocationODR => HasItem ? GetPageAndSlot(ItemId, IsHq, Type, Sorter) : null;
        public bool IsEquipped => Location?.Container == InventoryType.EquippedItems;
        public bool IsHq => Pointer->Flags == InventoryItem.ItemFlags.HighQuality;
        public bool CanDesynth => Item.Desynth > 0;
        public bool InGearset
        {
            get
            {
                var gm = RaptureGearsetModule.Instance();
                for (byte i = 0; i < 100; ++i)
                {
                    if (!gm->IsValidGearset(i)) continue;
                    var gearset = gm->GetGearset(i);
                    if (gearset != null && gearset->Flags.HasFlag(RaptureGearsetModule.GearsetFlag.Exists))
                        if (gearset->Items.ToArray().Any(x => ItemUtil.GetBaseId(x.ItemId).ItemId == ItemId)) return true;
                }
                return false;
            }
        }

        public override string ToString() => $"[#{ItemId} {Item.Name}] [IM:{Location?.Container.ToString() ?? "None"}:{Location?.Slot ?? -1} ODR:{LocationODR?.Page ?? uint.MaxValue}:{LocationODR?.Slot ?? uint.MaxValue}]";
    }

    public class InventoryContainerWrapper
    {
        public InventoryContainerWrapper(InventoryType inv) => Pointer = inv.GetContainer();
        public InventoryContainerWrapper(InventoryContainer* container) => Pointer = container;
        public InventoryContainer* Pointer { get; set; }
        public ItemOrderModuleSorter* Sorter => Pointer->Type.GetSorter();
        public int Size => Pointer->Size;
        public int Count => Sorter->Items.Count;
        public InventoryType Type => Pointer->Type;
        public uint? FirstEmptySlotODR
        {
            get
            {
                for (uint i = 0; i < Count; i++)
                {
                    var entry = Sorter->Items[i].Value;
                    var item = InventoryManager.Instance()->GetInventorySlot(Type + entry->Page, entry->Slot);
                    if (item is null || item->ItemId == 0) return i;
                }
                return null;
            }
        }
        public int EmptySlots
        {
            get
            {
                var count = 0;
                //for (var i = 0; i < Pointer->Size; ++i)
                //    if (GetSlotRaw(i)->ItemId == 0)
                //        count++;
                for (var i = 0; i < Count; i++)
                {
                    var item = Pointer->Items[i];
                    if (item.ItemId == 0) count++;
                }
                return count;
            }
        }

        public InventoryItemWrapper? FirstNonGearset
        {
            get
            {
                for (var i = 0; i < Count; i++)
                {
                    if (Pointer->Items[i] is { ItemId: not 0 } item && new InventoryItemWrapper(item) is { InGearset: false } wrapper)
                        return wrapper;
                }
                return null;
            }
        }

        public bool Contains(InventoryItemWrapper item) => Contains(item.ItemId);
        public bool Contains(uint itemId)
        {
            for (var i = 0; i < Size; i++)
            {
                var item = Pointer->Items[i];
                if (item.ItemId == itemId) return true;
            }
            return false;
        }

        public InventoryItem* GetSlotRaw(int slot) => Pointer->GetInventorySlot(slot);

        public override string ToString() => $"{Type} Slots: {Count} Empty: {EmptySlots}";
    }

    public static (uint page, uint slot)? GetPageAndSlot(uint itemId, bool isHq, InventoryType inventoryType, ItemOrderModuleSorter* sorter)
    {
        var inventoryManager = InventoryManager.Instance();
        for (var i = 0U; i < sorter->Items.LongCount; i++)
        {
            var entry = sorter->Items[i].Value;
            var item = inventoryManager->GetInventorySlot(inventoryType + entry->Page, entry->Slot);
            if (item is null) continue;
            if (item->ItemId == itemId && item->Flags.HasFlag(InventoryItem.ItemFlags.HighQuality) == isHq)
            {
                var page = (uint)(i / sorter->ItemsPerPage);
                var slot = (uint)(i % sorter->ItemsPerPage);
                return (page, slot);
            }
        }

        if (inventoryType != InventoryType.Inventory1) return GetPageAndSlot(itemId, isHq, InventoryType.Inventory1, ItemOrderModule.Instance()->InventorySorter);
        return null;
    }

    public static unsafe (InventoryType inv, int slot)? GetItemLocationInInventory(uint itemId, IEnumerable<InventoryType> inventories)
    {
        foreach (var inv in inventories)
        {
            var cont = InventoryManager.Instance()->GetInventoryContainer(inv);
            for (var i = 0; i < cont->Size; ++i)
                if (cont->GetInventorySlot(i)->ItemId == itemId)
                    return (inv, i);
        }
        return null;
    }

    private static unsafe int InternalGetItemCount(uint itemId, bool isHq) => InventoryManager.Instance()->GetInventoryItemCount(itemId, isHq);
    public static unsafe int GetItemCount(uint itemId, bool includeHQ = true) => includeHQ ? InternalGetItemCount(itemId, true) + InternalGetItemCount(itemId, false) : InternalGetItemCount(itemId, false);

    public static unsafe bool HasItem(uint itemId) => GetItemInInventory(itemId, Equippable) != null;
    public static unsafe bool HasItemEquipped(uint itemId)
    {
        var cont = InventoryManager.Instance()->GetInventoryContainer(InventoryType.EquippedItems);
        for (var i = 0; i < cont->Size; ++i)
            if (cont->GetInventorySlot(i)->ItemId == itemId)
                return true;
        return false;
    }

    public static unsafe InventoryItem* GetItemInInventory(uint itemId, IEnumerable<InventoryType> inventories, bool mustBeHQ = false)
    {
        foreach (var inv in inventories)
        {
            var cont = InventoryManager.Instance()->GetInventoryContainer(inv);
            for (var i = 0; i < cont->Size; ++i)
                if (cont->GetInventorySlot(i)->ItemId == itemId && (!mustBeHQ || cont->GetInventorySlot(i)->Flags == InventoryItem.ItemFlags.HighQuality))
                    return cont->GetInventorySlot(i);
        }
        return null;
    }

    public static unsafe List<Pointer<InventoryItem>> GetHQItems(IEnumerable<InventoryType> inventories)
    {
        List<Pointer<InventoryItem>> items = [];
        foreach (var inv in inventories)
        {
            var cont = InventoryManager.Instance()->GetInventoryContainer(inv);
            for (var i = 0; i < cont->Size; ++i)
                if (cont->GetInventorySlot(i)->Flags == InventoryItem.ItemFlags.HighQuality)
                    items.Add(cont->GetInventorySlot(i));
        }
        return items;
    }

    public static unsafe List<Pointer<InventoryItem>> GetDesynthableItems(IEnumerable<InventoryType> inventories)
    {
        List<Pointer<InventoryItem>> items = [];
        foreach (var inv in inventories)
        {
            var cont = InventoryManager.Instance()->GetInventoryContainer(inv);
            for (var i = 0; i < cont->Size; ++i)
                if (GetRow<Item>(cont->GetInventorySlot(i)->ItemId)?.Desynth > 0)
                    items.Add(cont->GetInventorySlot(i));
        }
        return items;
    }

    public static unsafe uint GetEmptySlots(InventoryType inv) => GetEmptySlots([inv]);
    public static unsafe uint GetEmptySlots(IEnumerable<InventoryType>? inventories = null)
    {
        if (inventories == null)
            return InventoryManager.Instance()->GetEmptySlotsInBag();
        else
        {
            uint count = 0;
            foreach (var inv in inventories)
            {
                var cont = InventoryManager.Instance()->GetInventoryContainer(inv);
                for (var i = 0; i < cont->Size; ++i)
                    if (cont->GetInventorySlot(i)->ItemId == 0)
                        count++;
            }
            return count;
        }
    }

    public static unsafe Item? GetItemInSlot(InventoryType inv, int slot)
        => GetRow<Item>(InventoryManager.Instance()->GetInventoryContainer(inv)->GetInventorySlot(slot)->ItemId);

    public static unsafe InventoryItem* GetFirstEmptySlot(InventoryType? inv = null)
    {
        if (inv is null)
        {
            foreach (var i in PlayerInventory)
            {
                if (i == InventoryType.KeyItems) continue;
                var cont = InventoryManager.Instance()->GetInventoryContainer(i);
                for (var j = 0; j < cont->Size; ++j)
                    if (cont->GetInventorySlot(j)->ItemId == 0)
                        return cont->GetInventorySlot(j);
            }
        }
        else
        {
            var cont = InventoryManager.Instance()->GetInventoryContainer(inv.Value);
            for (var i = 0; i < cont->Size; ++i)
                if (cont->GetInventorySlot(i)->ItemId == 0)
                    return cont->GetInventorySlot(i);
        }
        return null;
    }

    public static List<uint> GetGearsetItemIds()
    {
        var gm = RaptureGearsetModule.Instance();
        List<uint> itemIds = [];
        for (byte i = 0; i < 100; ++i)
        {
            if (!gm->IsValidGearset(i)) continue;
            var gearset = gm->GetGearset(i);
            if (gearset != null && gearset->Flags.HasFlag(RaptureGearsetModule.GearsetFlag.Exists))
                itemIds.AddRange(gearset->Items.ToArray().Where(x => x.ItemId != 0).Select(x => ItemUtil.GetBaseId(x.ItemId).ItemId));
        }
        return itemIds;
    }

    public static unsafe InventoryItem* GetFirstNonGearsetItem(InventoryType inv)
    {
        var cont = InventoryManager.Instance()->GetInventoryContainer(inv);
        var gearsetItems = GetGearsetItemIds();
        for (var i = 0; i < cont->Size; ++i)
            if (!gearsetItems.Contains(cont->GetInventorySlot(i)->ItemId))
                return cont->GetInventorySlot(i);
        return null;
    }

    public static InventoryType GetItemArmouryContainer(uint itemId) => GetRow<Item>(itemId)!.Value.GetArmouryContainer();
}

public static unsafe class InventoryExtensions
{
    public static InventoryContainer* GetContainer(this InventoryType inv) => InventoryManager.Instance()->GetInventoryContainer(inv);
    public static ItemOrderModuleSorter* GetSorter(this InventoryType inv)
    {
        var m = ItemOrderModule.Instance();
        var sorter = inv switch
        {
            InventoryType.ArmoryMainHand => m->ArmouryMainHandSorter,
            InventoryType.ArmoryHead => m->ArmouryHeadSorter,
            InventoryType.ArmoryBody => m->ArmouryBodySorter,
            InventoryType.ArmoryHands => m->ArmouryHandsSorter,
            InventoryType.ArmoryLegs => m->ArmouryLegsSorter,
            InventoryType.ArmoryFeets => m->ArmouryFeetSorter,
            InventoryType.ArmoryOffHand => m->ArmouryOffHandSorter,
            InventoryType.ArmoryEar => m->ArmouryEarsSorter,
            InventoryType.ArmoryNeck => m->ArmouryNeckSorter,
            InventoryType.ArmoryWrist => m->ArmouryWristsSorter,
            InventoryType.ArmoryRings => m->ArmouryRingsSorter,
            InventoryType.ArmorySoulCrystal => m->ArmourySoulCrystalSorter,
            InventoryType.SaddleBag1 or InventoryType.SaddleBag2 => m->SaddleBagSorter,
            InventoryType.PremiumSaddleBag1 or InventoryType.PremiumSaddleBag2 => m->PremiumSaddleBagSorter,
            InventoryType.Inventory1 or InventoryType.Inventory2 or InventoryType.Inventory3 or InventoryType.Inventory4 => m->InventorySorter,
            _ => null
        };
        return sorter;
    }

    public static InventoryType GetArmouryContainer(this Item item) => item.EquipSlotCategory.Value switch
    {
        { MainHand: 1 } => InventoryType.ArmoryMainHand,
        { OffHand: 1 } => InventoryType.ArmoryOffHand,
        { Head: 1 } => InventoryType.ArmoryHead,
        { Body: 1 } => InventoryType.ArmoryBody,
        { Gloves: 1 } => InventoryType.ArmoryHands,
        { Legs: 1 } => InventoryType.ArmoryLegs,
        { Feet: 1 } => InventoryType.ArmoryFeets,
        { Ears: 1 } => InventoryType.ArmoryEar,
        { Neck: 1 } => InventoryType.ArmoryNeck,
        { Wrists: 1 } => InventoryType.ArmoryWrist,
        { FingerL: 1 } => InventoryType.ArmoryRings,
        { FingerR: 1 } => InventoryType.ArmoryRings,
        _ => throw new ArgumentOutOfRangeException(nameof(item), item, null)
    };
}
