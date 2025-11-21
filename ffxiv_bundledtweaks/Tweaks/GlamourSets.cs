using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Lumina.Excel.Sheets;
using System.Collections.Immutable;
using System.Collections.ObjectModel;

namespace ComplexTweaks.Tweaks;

public class GlamourSetsTrackerConfiguration
{
    [BoolConfig] public bool ShowOnlyMissing = false;
}

[Tweak]
public unsafe class GlamourSets : Tweak<GlamourSetsTrackerConfiguration, GlamourSetsWindow>
{
    public override string Name => "Glamour Sets Tracker";
    public override string Description => "A tracking window for glamour sets";

    [CommandHandler("/glamoursets", "Toggle the Glamour Sets Tracker window")]
    internal void OnCommand(string command, string arguments) => Window<GlamourSetsWindow>()?.Toggle();
}

public unsafe class GlamourSetsWindow : Window
{
    private const uint ItemWolfMarks = 25;
    private const uint ItemMgp = 29;
    private const uint ItemTrophyCrystals = 36656;

    private static readonly (uint ItemId, string Name)[] AlliedSocietyCurrencies =
    [
        (21074, "Vanu Whitebone"),
        (21079, "Black Copper Gil"),
        (21081, "Kojin Sango"),
    ];

    private static readonly ImmutableHashSet<uint> MgpMakaiSets = new HashSet<uint>
    {
        // makai gear
        45249, 45466, 45467, 45255, 45256, 45257, 45254, 45259, 45260, 45261, 45258, 45465, 45464, 45251, 45253,
        45250, 45252
    }.ToImmutableHashSet();

    private static readonly ImmutableHashSet<uint> UndyedRathalosSets = new HashSet<uint>
    {
        45324, 45323
    }.ToImmutableHashSet();

    private static readonly ImmutableHashSet<uint> EternalBondingSets = new HashSet<uint>
    {
        45139, 45140, 45141, 45142, 45143, 45144
    }.ToImmutableHashSet();

    private static readonly ImmutableHashSet<uint> UnobtainableSets = new HashSet<uint>
    {
        // old pvp rewards
        45437, 45320, 45248, 45247, 45508, 45529, 45306, 45340, 45289, 45339, 45222, 45330, 45223, 45424, 45423, 45564
    }.ToImmutableHashSet();

    private const uint MostRecentPvpSet = 47704; // air cell series 9

    private readonly List<InventoryType> _inventoryTypes =
    [
        InventoryType.Inventory1,
            InventoryType.Inventory2,
            InventoryType.Inventory3,
            InventoryType.Inventory4,
            InventoryType.SaddleBag1,
            InventoryType.SaddleBag2,
            InventoryType.PremiumSaddleBag1,
            InventoryType.PremiumSaddleBag2,
            InventoryType.EquippedItems,
            InventoryType.ArmoryMainHand,
            InventoryType.ArmoryOffHand,
            InventoryType.ArmoryHead,
            InventoryType.ArmoryBody,
            InventoryType.ArmoryHands,
            InventoryType.ArmoryLegs,
            InventoryType.ArmoryFeets,
            InventoryType.ArmoryEar,
            InventoryType.ArmoryNeck,
            InventoryType.ArmoryWrist,
            InventoryType.ArmoryRings,
        ];

    private readonly GlamourSets _tweak;
    private readonly ReadOnlyCollection<GlamourSet> _glamourSets;
    private readonly Dictionary<uint, int> _ownedCurrencies = [];

    public GlamourSetsWindow(GlamourSets tweak) : base($"Glamour Sets Tracker##{nameof(GlamourSetsWindow)}")
    {
        _tweak = tweak;
        var armoireItems = GetSheet<Cabinet>().Where(x => x.RowId > 0).Select(x => x.Item.RowId).ToHashSet();
        var specialShopItems = BuildSpecialShopItems();
        _glamourSets = BuildGlamourSets(armoireItems, specialShopItems);
    }

    public override void Draw()
    {
        var agent = ItemFinderModule.Instance();
        if (agent is null)
        {
            ImGui.Text("You are not logged in.");
            return;
        }

        unsafe
        {
            var inventoryManager = InventoryManager.Instance();
            if (inventoryManager != null)
            {
                _ownedCurrencies[ItemMgp] = inventoryManager->GetItemCountInContainer(ItemMgp, InventoryType.Currency);
                _ownedCurrencies[ItemWolfMarks] = (int)inventoryManager->GetWolfMarks();
                _ownedCurrencies[ItemTrophyCrystals] = inventoryManager->GetInventoryItemCount(ItemTrophyCrystals);
                foreach (var (itemId, _) in AlliedSocietyCurrencies)
                    _ownedCurrencies[itemId] = inventoryManager->GetInventoryItemCount(itemId);
            }
            else
                _ownedCurrencies.Clear();
        }

        var ownedSets = _glamourSets.Where(x => agent->GlamourDresserItemIds.Contains(x.ItemId)).ToList();
        ImGui.Text($"Complete Sets: {ownedSets.Count} / {_glamourSets.Count(x => x.SetType != ESetType.Unobtainable || ownedSets.Contains(x))}");
        ImGui.Text($"Space saved: {ownedSets.Sum(x => x.Items.Count - 1)} items");

        var config = _tweak.GetConfig<GlamourSetsTrackerConfiguration>();
        if (config == null) return;

        var missingOnly = config.ShowOnlyMissing;
        if (ImGui.Checkbox("Show missing only", ref missingOnly))
            config.ShowOnlyMissing = missingOnly;

        ImGui.Separator();

        using var tabBar = ImRaii.TabBar("Tabs");
        if (tabBar)
        {
            DrawTab("Normal", ownedSets, ESetType.Default);
            DrawTab("PvP", ownedSets, ESetType.PvP);
            DrawTab("MGP", ownedSets, ESetType.MGP);
            DrawTab("Allied Societies", ownedSets, ESetType.AlliedSociety);
            DrawSpecialtyTab(ownedSets);
            DrawTab("Unobtainable", ownedSets, ESetType.Unobtainable);
        }
    }

    private void DrawTab(string name, List<GlamourSet> ownedSets, ESetType setType)
    {
        using var tab = ImRaii.TabItem(name);
        if (!tab)
            return;

        var config = _tweak.GetConfig<GlamourSetsTrackerConfiguration>();
        var glamourSets = _glamourSets.Where(x => x.SetType == setType).ToList();
        if (config?.ShowOnlyMissing == true)
            glamourSets = [.. glamourSets.Except(ownedSets)];

        var ownedItems = GetOwnedItems();
        DrawMissingItemHeader(glamourSets, setType, ownedSets, ownedItems);

        using (ImRaii.Child("Sets"))
            DrawSetRange(glamourSets, ownedSets, ownedItems);
    }

    private void DrawSpecialtyTab(List<GlamourSet> ownedSets)
    {
        using var tab = ImRaii.TabItem("Special");
        if (!tab)
            return;

        var glamourSets = _glamourSets.Where(x => x.SetType == ESetType.Special).ToList();
        if (_tweak.Config!.ShowOnlyMissing)
            glamourSets = [.. glamourSets.Except(ownedSets)];

        var ownedItems = GetOwnedItems();
        DrawMissingItemHeader(glamourSets, ESetType.Special, ownedSets, ownedItems);
        if (ImGui.CollapsingHeader("Eternal Bonding"))
            DrawSetRange([.. glamourSets.Where(x => EternalBondingSets.Contains(x.ItemId))], ownedSets, ownedItems);
        if (ImGui.CollapsingHeader("Makai Sets (MGP)"))
            DrawSetRange([.. glamourSets.Where(x => MgpMakaiSets.Contains(x.ItemId))], ownedSets, ownedItems);
        if (ImGui.CollapsingHeader("Rathalos Sets (undyed)"))
            DrawSetRange([.. glamourSets.Where(x => UndyedRathalosSets.Contains(x.ItemId))], ownedSets, ownedItems);
    }

    private void DrawMissingItemHeader(List<GlamourSet> glamourSets, ESetType setType, List<GlamourSet> ownedSets,
        HashSet<uint> ownedItems)
    {
        var missingItems = glamourSets.Except(ownedSets).SelectMany(x => x.Items).Where(x => !ownedItems.Contains(x.ItemId)).ToList();
        if (setType == ESetType.PvP)
        {
            ImGui.Text($"Wolf Marks: {_ownedCurrencies.GetValueOrDefault(ItemWolfMarks):N0} / {missingItems.Where(x => x is { ShopItem.CostItemId: ItemWolfMarks }).Sum(x => x.ShopItem!.CostQuantity):N0}");
            ImGui.Text($"Trophy Crystals: {_ownedCurrencies.GetValueOrDefault(ItemTrophyCrystals):N0} / {missingItems.Where(x => x is { ShopItem.CostItemId: ItemTrophyCrystals }).Sum(x => x.ShopItem!.CostQuantity):N0}");
            ImGui.Separator();
        }
        else if (setType is ESetType.MGP or ESetType.Special)
        {
            ImGui.Text($"MGP: {_ownedCurrencies.GetValueOrDefault(ItemMgp):N0} / {missingItems.Where(x => x is { ShopItem.CostItemId: ItemMgp }).Sum(x => x.ShopItem!.CostQuantity):N0}");
            ImGui.Separator();
        }
        else if (setType == ESetType.AlliedSociety)
        {
            foreach (var (itemId, name) in AlliedSocietyCurrencies)
                ImGui.Text($"{name}: {_ownedCurrencies.GetValueOrDefault(itemId):N0} / {missingItems.Where(x => x is { ShopItem: { } shopItem } && shopItem.CostItemId == itemId).Sum(x => x.ShopItem!.CostQuantity):N0}");
            ImGui.Separator();
        }
    }

    private void DrawSetRange(List<GlamourSet> glamourSets, List<GlamourSet> ownedSets, HashSet<uint> ownedItems)
    {
        foreach (var glamourSet in glamourSets)
        {
            if (ownedSets.Contains(glamourSet))
                ImGui.TextColored(ImGuiColors.ParsedGreen, glamourSet.Name);
            else
            {
                var ownedCount = glamourSet.Items.Count(x => ownedItems.Contains(x.ItemId));
                if (ownedCount == glamourSet.Items.Count)
                    ImGui.TextColored(ImGuiColors.ParsedBlue, $"{glamourSet.Name} (Can be completed)");
                else if (CanAffordAllMissingGearPieces(glamourSet, ownedItems))
                    ImGui.TextColored(ImGuiColors.DalamudViolet, $"{glamourSet.Name} (Can afford)");
                else if (ownedCount > 0)
                    ImGui.TextColored(ImGuiColors.DalamudYellow, glamourSet.Name);
                else
                    ImGui.Text(glamourSet.Name);

                using (ImRaii.PushIndent())
                {
                    foreach (var item in glamourSet.Items)
                    {
                        if (ownedItems.Contains(item.ItemId))
                            ImGui.TextColored(ImGuiColors.ParsedGreen, item.Name);
                        else if (item.ShopItem is { } shopItem)
                            ImGui.Text($"{item.Name} ({shopItem.CostQuantity:N0}x {shopItem.CostName})");
                        else
                            ImGui.Text(item.Name);

                        if (ImGui.IsItemClicked())
                        {
                            try
                            {
                                Svc.Chat.Print(SeString.CreateItemLink(item.ItemId, false));
                            }
                            catch (Exception)
                            {
                                // doesn't matter, just nice-to-have
                            }
                        }
                    }
                }
            }
        }
    }

    private HashSet<uint> GetOwnedItems()
    {
        HashSet<uint> ownedItems = [.. ItemFinderModule.Instance() != null ? ItemFinderModule.Instance()->GlamourDresserItemIds : []];
        unsafe
        {
            var inventoryManager = InventoryManager.Instance();
            if (inventoryManager != null)
            {
                foreach (var inventoryType in _inventoryTypes)
                {
                    var inventoryContainer = inventoryManager->GetInventoryContainer(inventoryType);
                    if (inventoryContainer == null)
                        continue;

                    for (var i = 0; i < inventoryContainer->Size; ++i)
                    {
                        var item = inventoryContainer->GetInventorySlot(i);
                        if (item != null && item->ItemId != 0)
                            ownedItems.Add(ItemUtil.GetBaseId(item->ItemId).ItemId);
                    }
                }
            }
        }

        return ownedItems;
    }

    private static ReadOnlyCollection<GlamourSet> BuildGlamourSets(HashSet<uint> armoireItems, Dictionary<uint, SpecialShopItem> specialShopItems)
    {
        return GetSheet<MirageStoreSetItem>().Where(x => x.RowId > 0).Select(x =>
        {
            var items = new List<uint>
            {
                x.MainHand.RowId,
                x.OffHand.RowId,
                x.Head.RowId,
                x.Body.RowId,
                x.Hands.RowId,
                x.Legs.RowId,
                x.Feet.RowId,
                x.Earrings.RowId,
                x.Necklace.RowId,
                x.Bracelets.RowId,
                x.Ring.RowId
            }
            .Where(y => y > 0)
            .Select(y => GetRow<Item>(y)!.Value)
            .Select(y => new GlamourItem
            {
                ItemId = y.RowId,
                Name = y.Name.ToString(),
                ShopItem = specialShopItems.GetValueOrDefault(y.RowId),
            })
            .Where(y => !string.IsNullOrEmpty(y.Name))
            .ToList()
            .AsReadOnly();

            return new GlamourSet
            {
                ItemId = x.RowId,
                Name = GetRow<Item>(x.RowId)?.Name.ToString() ?? "",
                Items = items,
                SetType = DetermineSetType(x, items),
            };
        })
        .Where(x => x.Items.Count > 0 && x.Items.Any(y => !armoireItems.Contains(y.ItemId)))
        .OrderBy(x => x.Name)
        .ThenBy(x => x.ItemId)
        .ToList()
        .AsReadOnly();
    }

    private static ESetType DetermineSetType(MirageStoreSetItem item, ReadOnlyCollection<GlamourItem> items)
    {
        // this would be a lot easier if the coffer item had any link to the mirage set
        if (GetSheet<PvPSeries>(language: Dalamud.Game.ClientLanguage.English).Reverse().FirstOrNull(x => x.LevelRewards[25].LevelRewardItem[0].ValueNullable?.Singular.ToString().Contains("attire coffer") ?? false) is { } latestReward)
            if (latestReward.LevelRewards[25].LevelRewardItem[0].Value.Singular.ToString().Replace("attire coffer", "").Trim() is { } baseName)
                if (MirageSetContainsName(item, baseName))
                    return ESetType.PvP;

        if (UnobtainableSets.Contains(item.RowId))
            return ESetType.Unobtainable;

        if (EternalBondingSets.Contains(item.RowId) ||
            UndyedRathalosSets.Contains(item.RowId) ||
            MgpMakaiSets.Contains(item.RowId))
            return ESetType.Special;

        var costItemId = items.FirstOrDefault()?.ShopItem?.CostItemId;
        if (AlliedSocietyCurrencies.Any(x => x.ItemId == costItemId))
            return ESetType.AlliedSociety;

        return costItemId switch
        {
            ItemWolfMarks or ItemTrophyCrystals => ESetType.PvP,
            ItemMgp => ESetType.MGP,
            _ => ESetType.Default,
        };
    }

    private static bool MirageSetContainsName(MirageStoreSetItem item, string name)
        => item.Head.ValueNullable?.Singular.ToString().Contains(name, StringComparison.OrdinalIgnoreCase) ?? false ||
            (item.Body.ValueNullable?.Singular.ToString().Contains(name, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (item.Hands.ValueNullable?.Singular.ToString().Contains(name, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (item.Legs.ValueNullable?.Singular.ToString().Contains(name, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (item.Feet.ValueNullable?.Singular.ToString().Contains(name, StringComparison.OrdinalIgnoreCase) ?? false);

    private bool CanAffordAllMissingGearPieces(GlamourSet glamourSet, HashSet<uint> ownedItems)
    {
        uint costItemId = 0;
        uint costQuantity = 0;
        foreach (var item in glamourSet.Items)
        {
            if (ownedItems.Contains(item.ItemId))
                continue;

            if (item.ShopItem == null)
                return false;

            costItemId = item.ShopItem.CostItemId;
            costQuantity += item.ShopItem.CostQuantity;
        }

        return costQuantity <= _ownedCurrencies.GetValueOrDefault(costItemId);
    }

    private static Dictionary<uint, SpecialShopItem> BuildSpecialShopItems()
        => GetSheet<SpecialShop>()
            .Where(x => x.RowId > 0 && !string.IsNullOrEmpty(x.Name.ToString()))
            .SelectMany(x => x.Item.SelectMany(y =>
                y.ReceiveItems.Select(z => new SpecialShopItem
                {
                    ItemId = z.Item.RowId,
                    CostItemId = y.ItemCosts[0].ItemCost.Value.RowId,
                    CostType = y.ItemCosts[0].ItemCost.Value.ItemUICategory.RowId,
                    CostName = y.ItemCosts[0].ItemCost.Value.Name.ToString(),
                    CostQuantity = y.ItemCosts[0].CurrencyCost,
                })
                    .Where(z => z.ItemId > 0 && (z.CostItemId < 100 || z.CostType == 100))))
            .GroupBy(x => x.ItemId)
            .ToDictionary(x => x.Key, x => x.First());

    private sealed class GlamourSet
    {
        public required uint ItemId { get; init; }
        public required string Name { get; init; }
        public required ESetType SetType { get; init; }
        public required IReadOnlyList<GlamourItem> Items { get; init; }
    }

    private sealed class GlamourItem
    {
        public required uint ItemId { get; init; }
        public required string Name { get; init; }
        public required SpecialShopItem? ShopItem { get; init; }
    }

    private sealed class SpecialShopItem
    {
        public required uint ItemId { get; init; }
        public required uint CostItemId { get; init; }
        public required uint CostType { get; init; }
        public required string CostName { get; init; }
        public required uint CostQuantity { get; init; }
    }

    private enum ESetType
    {
        Default,
        MGP,
        PvP,
        AlliedSociety,
        Special,
        Unobtainable,
    }
}
