using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Lumina.Excel.Sheets;
using LuminaSupplemental.Excel.Model;
using LuminaSupplemental.Excel.Services;
using System.Collections.Immutable;
using System.Collections.ObjectModel;

namespace ComplexTweaks.Tweaks;

public class GlamourSetsTrackerConfiguration {
    [BoolConfig] public bool ShowOnlyMissing = false;
    [BoolConfig] public bool ShowPartiallyCompleted = false;
    [BoolConfig] public bool ShowCanAfford = false;
    [BoolConfig] public bool ShowDoneNotInDresser = false;
    [BoolConfig] public bool ShowMarketboardPurchasable = false;
}

[Tweak]
public unsafe class GlamourSets : Tweak<GlamourSetsTrackerConfiguration, GlamourSetsWindow> {
    public override string Name => "Glamour Sets Tracker";
    public override string Description => "A tracking window for glamour sets";

    [CommandHandler("/glamoursets", "Toggle the Glamour Sets Tracker window")]
    internal void OnCommand(string _, string __) => Window<GlamourSetsWindow>()?.Toggle();
}

public unsafe class GlamourSetsWindow : Window {
    private static readonly ImmutableHashSet<uint> UnobtainableSets = new HashSet<uint>
    {
        // old feast rewards
        45320, 45248, 45247, 45306, 45340, 45289, 45339, 45222, 45330, 45223, 45424, 45423
    }.ToImmutableHashSet();

    private class OutfitCategory {
        public string Name { get; set; } = "";
        public List<uint> Discriminators { get; set; } = []; // currency item id in an item's cost
        public Func<uint, bool>? AmountDiscriminator { get; set; }
        public Func<Item, bool>? ItemPredicateDiscriminator { get; set; }
        public Func<SpecialShop, bool>? SpecialShopPredicateDiscriminator { get; set; }
    }

    private static byte[] TradeScripSpecialIds => [1, 2, 3, 4, 6, 7];

    private readonly List<OutfitCategory> OutfitCategories = [
        new OutfitCategory {
            Name = "Saucer",
            Discriminators = [29, 41629] // MGP, MGF
        },
        new OutfitCategory {
            Name = "PvP",
            Discriminators = [25, 36656, 40479] // Wolf Marks, Trophy Crystals, Commendation Crystals
        },
        new OutfitCategory {
            Name = "Tribes",
            Discriminators = [.. FindRows<BeastTribe>(r => r.CurrencyItem.RowId != 0).Select(r => r.CurrencyItem.RowId)]
        },
        new OutfitCategory { // must be after tribes because some tribe outfits are gil based
            Name = "Gil",
            Discriminators = [1], // Gil
            AmountDiscriminator = amount => amount > 0 // job gear is always 0, filter them
        },
        new OutfitCategory {
            Name = "Trades",
            Discriminators = [.. TradeScripSpecialIds.Select(sid => CurrencyManager.Instance()->GetItemIdBySpecialId(sid))]
        },
        new OutfitCategory {
            Name = "Job Gear",
            SpecialShopPredicateDiscriminator = shop => shop.UseCurrencyType == 8 && shop.Quest.RowId > 0
        },
        new OutfitCategory {
            Name = "Eureka",
            Discriminators = [21801, 21803] // Protean/Anemos Crystals
        },
        new OutfitCategory {
            Name = "Crescent",
            Discriminators = [45043, 45044] // Enlightenment pieces
        },
        new OutfitCategory {
            Name = "Raids",
            Discriminators = [.. Svc.Data.GetSupplemental<DungeonBossDrop>(CsvLoader.DungeonBossDropResourceName).Where(r => r.FightNo is 0).Select(r => r.ItemId), 22599, 23383, 47100]
            // all totems + monster hunter scales (they don't drop from the boss)
        },
        new OutfitCategory {
            Name = "Variant/DD",
            Discriminators = [15422, 23164, 38533, 39884, 41078, 46186] // All potsherds
        },
        new OutfitCategory {
            Name = "Fates",
            Discriminators = [12252, 27972, 36634, 41804] // Coeurlregina horn, Archaeotania's horn, Daivadipa's bead, Mica magicog
        },
        new OutfitCategory {
            Name = "Island",
            Discriminators = [37549, 37550] // Searfarer/Islander Cowries
        },
        new OutfitCategory {
            Name = "Eternal Bonding",
            ItemPredicateDiscriminator = item => item.WithLanguage(Dalamud.Game.ClientLanguage.English).Description.ToString().Equals("Fits: Everyone ♥", StringComparison.OrdinalIgnoreCase)
        },
        new OutfitCategory {
            Name = "Vintage",
            Discriminators = [9387, 9388, 9389, 9390, 9391] // Antique pieces from 2.x dungeons
        },
    ];

    private readonly GlamourSets _tweak;
    private readonly ReadOnlyCollection<GlamourSet> _glamourSets;
    private readonly ItemCostLookup _costsLookup;

    // caches
    private readonly Dictionary<string, List<uint>> _categoryDiscriminators;
    private readonly Dictionary<uint, string> _itemNames;
    private readonly Dictionary<(uint itemId, string? category), string?> _costDisplays;
    private readonly Dictionary<(uint itemId, string? category), List<(uint ItemId, uint Amount)>> _primaryCostsCache;
    private readonly Dictionary<ESetType, List<GlamourSet>> _glamourSetsByType;
    private readonly Dictionary<string, List<GlamourSet>> _glamourSetsByCategory;

    public GlamourSetsWindow(GlamourSets tweak) : base($"Glamour Sets Tracker##{nameof(GlamourSetsWindow)}") {
        _tweak = tweak;
        _costsLookup = new();

        _categoryDiscriminators = OutfitCategories
            .Where(c => c.Discriminators != null && c.Discriminators.Count > 0)
            .ToDictionary(c => c.Name, c => c.Discriminators);

        var armoireItems = GetSheet<Cabinet>().Where(x => x.RowId > 0).Select(x => x.Item.RowId).ToHashSet();
        _glamourSets = BuildGlamourSets(armoireItems, _costsLookup);

        _itemNames = [];
        _costDisplays = [];
        _primaryCostsCache = [];
        BuildItemCaches();

        _glamourSetsByType = _glamourSets.GroupBy(s => s.SetType).ToDictionary(g => g.Key, g => g.ToList());
        _glamourSetsByCategory = _glamourSets
            .Where(s => s.SetType == ESetType.Custom && s.CustomCategoryName != null)
            .GroupBy(s => s.CustomCategoryName!)
            .ToDictionary(g => g.Key, g => g.ToList());
    }

    public override void Draw() {
        var agent = ItemFinderModule.Instance();
        if (agent is null) {
            ImGui.Text("You are not logged in.");
            return;
        }

        var ownedItems = GetOwnedItems();
        var dresserItemIds = new HashSet<uint>(agent->GlamourDresserBaseItemIds.ToArray());
        var ownedSetsHashSet = new HashSet<GlamourSet>(_glamourSets.Where(x => dresserItemIds.Contains(x.ItemId)));
        var ownedSets = ownedSetsHashSet.ToList();

        var obtainableCount = _glamourSets.Count(x => x.SetType != ESetType.Unobtainable || ownedSetsHashSet.Contains(x));
        ImGui.Text($"Complete Sets: {ownedSets.Count} / {obtainableCount}");
        ImGui.Text($"Space saved: {ownedSets.Sum(x => x.Items.Count - 1)} items");

        var config = _tweak.GetConfig<GlamourSetsTrackerConfiguration>();
        if (config == null) return;

        var missingOnly = config.ShowOnlyMissing;
        if (ImGui.Checkbox("Missing", ref missingOnly))
            config.ShowOnlyMissing = missingOnly;

        ImGui.SameLine();
        var showPartiallyCompleted = config.ShowPartiallyCompleted;
        if (ImGui.Checkbox("Partials", ref showPartiallyCompleted))
            config.ShowPartiallyCompleted = showPartiallyCompleted;

        ImGui.SameLine();
        var showCanAfford = config.ShowCanAfford;
        if (ImGui.Checkbox("Affordable", ref showCanAfford))
            config.ShowCanAfford = showCanAfford;

        ImGui.SameLine();
        var showDoneNotInDresser = config.ShowDoneNotInDresser;
        if (ImGui.Checkbox("Completable", ref showDoneNotInDresser))
            config.ShowDoneNotInDresser = showDoneNotInDresser;

        ImGui.SameLine();
        var showMarketboardPurchasable = config.ShowMarketboardPurchasable;
        if (ImGui.Checkbox("Marketboard", ref showMarketboardPurchasable))
            config.ShowMarketboardPurchasable = showMarketboardPurchasable;

        ImGui.Separator();

        using var tabBar = ImRaii.TabBar("Tabs");
        if (tabBar) {
            DrawTab("Normal", ownedSetsHashSet, ownedItems, ESetType.Default);
            DrawCustomCategoryTabs(ownedSetsHashSet, ownedItems);
            DrawTab("Unobtainable", ownedSetsHashSet, ownedItems, ESetType.Unobtainable);
        }
    }

    private void DrawTab(string name, HashSet<GlamourSet> ownedSets, HashSet<uint> ownedItems, ESetType setType) {
        using var tab = ImRaii.TabItem(name);
        if (!tab)
            return;

        var config = _tweak.GetConfig<GlamourSetsTrackerConfiguration>();
        if (!_glamourSetsByType.TryGetValue(setType, out var glamourSets))
            glamourSets = [];

        if (config != null) {
            glamourSets = [.. glamourSets.Where(s => MatchesFilters(s, ownedSets, ownedItems, config))];
        }

        var missingItemIds = glamourSets.Where(s => !ownedSets.Contains(s)).SelectMany(x => x.Items).Where(itemId => !ownedItems.Contains(itemId)).ToList();
        DrawCurrencyTotals(missingItemIds);

        using (ImRaii.Child("Sets"))
            DrawSetRange(glamourSets, ownedSets, ownedItems);
    }

    private void DrawCustomCategoryTabs(HashSet<GlamourSet> ownedSets, HashSet<uint> ownedItems) {
        if (OutfitCategories.Count == 0)
            return;

        var config = _tweak.GetConfig<GlamourSetsTrackerConfiguration>();

        foreach (var category in OutfitCategories) {
            if (string.IsNullOrEmpty(category.Name))
                continue;

            using var tab = ImRaii.TabItem(category.Name);
            if (!tab)
                continue;

            if (!_glamourSetsByCategory.TryGetValue(category.Name, out var glamourSets))
                glamourSets = [];

            if (config != null) {
                glamourSets = [.. glamourSets.Where(s => MatchesFilters(s, ownedSets, ownedItems, config))];
            }

            DrawCustomCategoryHeader(glamourSets, category, ownedSets, ownedItems);

            using (ImRaii.Child("Sets"))
                DrawSetRange(glamourSets, ownedSets, ownedItems);
        }
    }

    private void DrawCustomCategoryHeader(List<GlamourSet> glamourSets, OutfitCategory category, HashSet<GlamourSet> ownedSets, HashSet<uint> ownedItems) {
        if (category.Discriminators == null || category.Discriminators.Count == 0)
            return;

        var missingItemIds = glamourSets.Where(s => !ownedSets.Contains(s)).SelectMany(x => x.Items).Where(itemId => !ownedItems.Contains(itemId)).ToList();

        var discriminatorTotals = new Dictionary<uint, uint>();
        var discriminatorSet = category.Discriminators.ToHashSet();
        foreach (var itemId in missingItemIds) {
            var costs = _costsLookup.GetItemCosts(itemId);
            foreach (var cost in costs) {
                if (discriminatorSet.Contains(cost.ItemId)) {
                    discriminatorTotals.TryGetValue(cost.ItemId, out var current);
                    discriminatorTotals[cost.ItemId] = current + cost.Amount;
                }
            }
        }

        foreach (var kvp in discriminatorTotals.OrderBy(x => GetRow<Item>(x.Key)?.Name.ToString() ?? x.Key.ToString())) {
            if (kvp.Value is 0) continue; // skip any unused
            var itemName = GetRow<Item>(kvp.Key)?.Name.ToString() ?? $"Item {kvp.Key}";
            var ownedCount = InventoryManager.Instance()->GetInventoryItemCount(kvp.Key);
            ImGui.Text($"{itemName}: {ownedCount:N0} / {kvp.Value:N0}");
        }

        if (discriminatorTotals.Count != 0)
            ImGui.Separator();
    }

    private void DrawCurrencyTotals(List<uint> missingItemIds) {
        var currencyGroups = missingItemIds
            .SelectMany(itemId => GetPrimaryCosts(itemId))
            .GroupBy(cost => cost.ItemId)
            .Select(g => {
                var firstCost = g.First();
                return new {
                    CostItemId = g.Key,
                    CostName = GetRow<Item>(g.Key)?.Name.ToString() ?? $"Item {g.Key}",
                    TotalRequired = g.Sum(x => x.Amount),
                    FirstCost = firstCost
                };
            })
            .Where(x => x.TotalRequired > 1)
            .OrderBy(x => x.CostName)
            .ToList();

        if (currencyGroups.Count == 0)
            return;

        foreach (var currency in currencyGroups) {
            var ownedCount = GetOwnedCountForCost(currency.CostItemId);
            ImGui.Text($"{currency.CostName}: {ownedCount:N0} / {currency.TotalRequired:N0}");
        }
        ImGui.Separator();
    }

    private void BuildItemCaches() {
        var allItemIds = _glamourSets.SelectMany(s => s.Items).Distinct().ToList();
        var itemCategories = new Dictionary<uint, HashSet<string?>>();
        foreach (var set in _glamourSets) {
            foreach (var itemId in set.Items) {
                if (!itemCategories.TryGetValue(itemId, out var categories)) {
                    categories = [];
                    itemCategories[itemId] = categories;
                }
                categories.Add(set.CustomCategoryName);
            }
        }

        foreach (var itemId in allItemIds) {
            var item = Item.GetRef(itemId).Value;
            _itemNames[itemId] = !item.IsUntradable ? $"{item.Name} {SeIconChar.Gil.ToIconString()}" : item.Name.ToString();

            var categories = itemCategories.GetValueOrDefault(itemId, []);
            categories.Add(null);

            foreach (var categoryName in categories) {
                var primaryCosts = GetPrimaryCosts(itemId,
                    !string.IsNullOrEmpty(categoryName) && _categoryDiscriminators.TryGetValue(categoryName, out var disc) ? disc : null);
                _primaryCostsCache[(itemId, categoryName)] = primaryCosts;
                _costDisplays[(itemId, categoryName)] = BuildCostDisplay(itemId, categoryName, primaryCosts);
            }
        }
    }

    private string? BuildCostDisplay(uint itemId, string? categoryName, List<(uint ItemId, uint Amount)>? costs = null) {
        costs ??= GetPrimaryCosts(itemId,
            !string.IsNullOrEmpty(categoryName) && _categoryDiscriminators.TryGetValue(categoryName, out var disc) ? disc : null);

        if (costs.Count == 0) {
            return _costsLookup.GetItemCost(itemId) is { } itemCost ? itemCost.ToString() : null;
        }

        return $"{string.Join(", ", costs.Select(c => $"{c.Amount:N0}x {Item.GetRow(c.ItemId).Name}"))}";
    }

    private List<(uint ItemId, uint Amount)> GetPrimaryCosts(uint itemId, List<uint>? priorityDiscriminators = null) {
        var costs = _costsLookup.GetItemCosts(itemId);
        if (costs.Count == 0)
            return [];

        // prioritise showing costs that match discriminators
        if (priorityDiscriminators != null && priorityDiscriminators.Count > 0) {
            var prioritizedCost = costs.FirstOrDefault(c => priorityDiscriminators.Contains(c.ItemId));
            if (prioritizedCost != default)
                return [prioritizedCost];
        }

        return [.. costs];
    }

    private unsafe int GetOwnedCountForCost(uint costItemId)
        => CurrencyManager.Instance()->SpecialItemBucket.TryGetValue(costItemId, out var value, true)
            ? (int)value.Count
            : InventoryManager.Instance()->GetInventoryItemCount(costItemId);

    private void DrawSetRange(List<GlamourSet> glamourSets, HashSet<GlamourSet> ownedSets, HashSet<uint> ownedItems) {
        foreach (var glamourSet in glamourSets) {
            if (ownedSets.Contains(glamourSet))
                ImGui.TextColored(ImGuiColors.ParsedGreen, glamourSet.Name);
            else {
                var ownedCount = 0;
                foreach (var itemId in glamourSet.Items) {
                    if (ownedItems.Contains(itemId))
                        ownedCount++;
                }

                if (ownedCount == glamourSet.Items.Count)
                    ImGui.TextColored(ImGuiColors.ParsedBlue, $"{glamourSet.Name} (Can be completed)");
                else if (CanAffordAllMissingGearPieces(glamourSet, ownedItems))
                    ImGui.TextColored(ImGuiColors.DalamudViolet, $"{glamourSet.Name} (Can afford)");
                else if (ownedCount > 0)
                    ImGui.TextColored(ImGuiColors.DalamudYellow, glamourSet.Name);
                else
                    ImGui.Text(glamourSet.Name);

                using (ImRaii.PushIndent()) {
                    foreach (var itemId in glamourSet.Items) {
                        var isOwned = ownedItems.Contains(itemId);
                        if (isOwned) {
                            ImGui.TextColored(ImGuiColors.ParsedGreen, _itemNames.GetValueOrDefault(itemId, $"Item {itemId}"));
                        }
                        else {
                            var itemName = _itemNames.GetValueOrDefault(itemId, $"Item {itemId}");
                            var costDisplay = _costDisplays.GetValueOrDefault((itemId, glamourSet.CustomCategoryName));
                            if (costDisplay != null) {
                                ImGui.Text($"{itemName} ({costDisplay})");
                            }
                            else {
                                ImGui.Text(itemName);
                            }
                        }

                        if (ImGui.IsItemClicked(ImGuiMouseButton.Left)) {
                            try {
                                Svc.Chat.Print(SeString.CreateItemLink(itemId, false));
                            }
                            catch (Exception) {
                                // doesn't matter, just nice-to-have
                            }
                        }
                        else if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                            Svc.ItemVendorLocation.OpenVendorResults(itemId);
                    }
                }
            }
        }
    }

    private unsafe HashSet<uint> GetOwnedItems() {
        HashSet<uint> ownedItems = [.. ItemFinderModule.Instance() != null ? ItemFinderModule.Instance()->GlamourDresserBaseItemIds : []];
        var inventoryManager = InventoryManager.Instance();
        if (inventoryManager != null) {
            foreach (var inventoryType in InventoryType.AllPlayer) {
                var inventoryContainer = inventoryManager->GetInventoryContainer(inventoryType);
                if (inventoryContainer == null)
                    continue;

                for (var i = 0; i < inventoryContainer->Size; ++i) {
                    var item = inventoryContainer->GetInventorySlot(i);
                    if (item != null && item->ItemId != 0)
                        ownedItems.Add(ItemUtil.GetBaseId(item->ItemId).ItemId);
                }
            }
        }

        return ownedItems;
    }

    private ReadOnlyCollection<GlamourSet> BuildGlamourSets(HashSet<uint> armoireItems, ItemCostLookup costsLookup) {
        var specialShopByItemId = GetSheet<SpecialShop>()
            .Where(s => s.RowId > 0 && !string.IsNullOrEmpty(s.Name.ToString()))
            .SelectMany(s => s.Item.SelectMany(item => item.ReceiveItems.Select(r => new {
                Shop = s,
                ItemId = r.Item.RowId
            })))
            .Where(x => x.ItemId > 0)
            .GroupBy(x => x.ItemId)
            .ToDictionary(g => g.Key, g => g.First().Shop);

        return GetSheet<MirageStoreSetItem>().Where(x => x.RowId > 0).Select(x => {
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
            .Where(y => {
                var item = Item.GetRef(y).Value;
                return !string.IsNullOrEmpty(item.Name.ToString());
            })
            .ToList()
            .AsReadOnly();

            SpecialShop? specialShopRow = null;
            foreach (var itemId in items) {
                if (specialShopByItemId.TryGetValue(itemId, out var shop)) {
                    specialShopRow = shop;
                    break;
                }
            }

            var (setType, customCategoryName) = DetermineSetType(x, items, costsLookup, specialShopRow);
            return new GlamourSet {
                ItemId = x.RowId,
                Name = Item.GetRef(x.RowId).Value.Name.ToString(),
                Items = items,
                SetType = setType,
                CustomCategoryName = customCategoryName,
                SpecialShopRow = specialShopRow,
            };
        })
        .Where(x => x.Items.Count > 0 && x.Items.Any(y => !armoireItems.Contains(y)))
        .OrderBy(x => x.Name)
        .ThenBy(x => x.ItemId)
        .ToList()
        .AsReadOnly();
    }

    private (ESetType SetType, string? CustomCategoryName) DetermineSetType(MirageStoreSetItem item, ReadOnlyCollection<uint> itemIds, ItemCostLookup costsLookup, SpecialShop? specialShopRow) {

        foreach (var row in GetSheet<PvPSeries>().Skip(1)) {
            if (!row.AttireItems.ContainsAll(itemIds))
                continue;

            if (row.RowId == FFXIVClientStructs.FFXIV.Client.Game.UI.PvPProfile.Instance()->Series) { // current series attire is by defintion obtainable
                return (ESetType.Custom, "PvP");
            }

            var hasCosts = itemIds.Any(i => costsLookup.GetItemCosts(i).Count > 0);
            if (!hasCosts) { // has no costs = hasn't been brought back yet
                return (ESetType.Unobtainable, null);
            }

            // has costs so find category
            if (FindCategoryForItems(itemIds, costsLookup, specialShopRow) is { } oldSeries) {
                return (ESetType.Custom, oldSeries);
            }
        }

        if (FindCategoryForItems(itemIds, costsLookup, specialShopRow) is { } cat) {
            return (ESetType.Custom, cat);
        }

        if (UnobtainableSets.Contains(item.RowId))
            return (ESetType.Unobtainable, null);

        return (ESetType.Default, null);
    }

    private string? FindCategoryForItems(ReadOnlyCollection<uint> itemIds, ItemCostLookup costsLookup, SpecialShop? specialShopRow) {
        foreach (var cat in OutfitCategories) {
            if (specialShopRow != null && cat.SpecialShopPredicateDiscriminator?.Invoke(specialShopRow.Value) == true) {
                return cat.Name;
            }

            foreach (var itemId in itemIds) {
                if (cat is { Discriminators.Count: > 0 }) {
                    foreach (var cost in costsLookup.GetItemCosts(itemId)) {
                        if (cat.Discriminators.Contains(cost.ItemId)) {
                            if (cat.AmountDiscriminator == null || cat.AmountDiscriminator.Invoke(cost.Amount)) {
                                return cat.Name;
                            }
                        }
                    }
                }

                if (cat.ItemPredicateDiscriminator?.Invoke(Item.GetRef(itemId).Value) ?? false) {
                    return cat.Name;
                }
            }
        }
        return null;
    }

    private bool CanAffordAllMissingGearPieces(GlamourSet glamourSet, HashSet<uint> ownedItems) {
        (uint CostItemId, uint TotalAmount)? firstCost = null;
        uint totalCostQuantity = 0;

        foreach (var itemId in glamourSet.Items) {
            if (ownedItems.Contains(itemId))
                continue;

            var costs = _primaryCostsCache.GetValueOrDefault((itemId, glamourSet.CustomCategoryName));
            if (costs == null || costs.Count == 0)
                return false;

            var cost = costs[0];
            firstCost ??= (cost.ItemId, 0);

            if (firstCost.Value.CostItemId != cost.ItemId)
                return false; // All items must use the same currency

            totalCostQuantity += cost.Amount;
        }

        if (firstCost == null)
            return false;

        var ownedCount = GetOwnedCountForCost(firstCost.Value.CostItemId);
        return totalCostQuantity <= ownedCount;
    }

    private bool IsPartiallyCompleted(GlamourSet glamourSet, HashSet<GlamourSet> ownedSets, HashSet<uint> ownedItems) {
        if (ownedSets.Contains(glamourSet))
            return false;

        var ownedCount = 0;
        foreach (var itemId in glamourSet.Items) {
            if (ownedItems.Contains(itemId))
                ownedCount++;
        }

        return ownedCount > 0 && ownedCount < glamourSet.Items.Count;
    }

    private bool IsDoneButNotInDresser(GlamourSet glamourSet, HashSet<GlamourSet> ownedSets, HashSet<uint> ownedItems) {
        if (ownedSets.Contains(glamourSet))
            return false;

        var ownedCount = 0;
        foreach (var itemId in glamourSet.Items) {
            if (ownedItems.Contains(itemId))
                ownedCount++;
        }

        return ownedCount == glamourSet.Items.Count;
    }

    private bool IsMarketboardPurchasable(GlamourSet glamourSet) {
        foreach (var itemId in glamourSet.Items) {
            var item = Item.GetRef(itemId).Value;
            if (!item.IsUntradable)
                return true;
        }
        return false;
    }

    private bool MatchesFilters(GlamourSet glamourSet, HashSet<GlamourSet> ownedSets, HashSet<uint> ownedItems, GlamourSetsTrackerConfiguration config) {
        if (config.ShowOnlyMissing && ownedSets.Contains(glamourSet))
            return false;

        var hasPositiveFilters = config.ShowPartiallyCompleted || config.ShowCanAfford || config.ShowDoneNotInDresser || config.ShowMarketboardPurchasable;
        if (!hasPositiveFilters)
            return true;

        var matchesAnyFilter = false;

        if (config.ShowPartiallyCompleted && IsPartiallyCompleted(glamourSet, ownedSets, ownedItems))
            matchesAnyFilter = true;

        if (config.ShowCanAfford && CanAffordAllMissingGearPieces(glamourSet, ownedItems))
            matchesAnyFilter = true;

        if (config.ShowDoneNotInDresser && IsDoneButNotInDresser(glamourSet, ownedSets, ownedItems))
            matchesAnyFilter = true;

        if (config.ShowMarketboardPurchasable && IsMarketboardPurchasable(glamourSet))
            matchesAnyFilter = true;

        return matchesAnyFilter;
    }

    private string? GetCostDisplay(uint itemId, string? categoryName = null) {
        return _costDisplays.GetValueOrDefault((itemId, categoryName));
    }

    private sealed class GlamourSet {
        public required uint ItemId { get; init; }
        public required string Name { get; init; }
        public required ESetType SetType { get; init; }
        public required IReadOnlyList<uint> Items { get; init; }
        public string? CustomCategoryName { get; init; }
        public SpecialShop? SpecialShopRow { get; init; }
    }

    private enum ESetType {
        Default,
        Special,
        Unobtainable,
        Custom,
    }
}
