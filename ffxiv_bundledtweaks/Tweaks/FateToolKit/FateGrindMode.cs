using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using TerritoryIntendedUse = FFXIVClientStructs.FFXIV.Client.Enums.TerritoryIntendedUse;

namespace ComplexTweaks.Tweaks;

public record struct ZoneItemTarget(uint TerritoryId, uint ItemId, int RequiredCount) {
    public bool IsComplete { get; set; }
}

public interface IFateGrindRunState {
    int CompletedCount { get; }
    int? RunUntilCompleted { get; }
    int? RemainingUntilCompleted { get; }
    bool CanSpendGemstones { get; }
}

internal readonly record struct FateSwapZoneActions(uint? EquipItemId, RowRef<Companion> TargetCompanion);

internal interface IFateGrindMode {
    string DisplayName { get; }
    int UiPriority => 0;

    IReadOnlySet<uint>? GetAllowedZones();
    bool IsComplete(IFateGrindRunState state);
    string? GetRemainingDisplay(IFateGrindRunState state);

    IEnumerable<ZoneItemTarget>? GetZoneItemTargets(IFateGrindRunState? state = null);
    FateSwapZoneActions? GetSwapZoneActions(uint fromTerritoryId, uint toTerritoryId) => null;
}

public static class FateGrindModes {
    internal static IReadOnlyList<IFateGrindMode> All { get; } = Build();
    internal static IFateGrindMode? GetByDisplayName(string displayName) => All.FirstOrDefault(m => m.DisplayName == displayName);
    internal static IFateGrindMode None => All.First(m => m.UiPriority == -1);

    private static IReadOnlyList<IFateGrindMode> Build() {
        var zeniths = RelicItem.GetItemsByStep(2);
        var animateds = QuestClassJobReward.GetRelicsByRow(3);
        var augmented = QuestClassJobReward.GetRelicsByRow(17);

        return [
            new NoneGrindMode(),
            new GemstoneGrindMode(),
            new YokaiGrindMode(),

            new ZoneItemGrindMode {
                DisplayName = "Atma (Zodiac)",
                Goals = [
                    new(7851, [148]), new(7852, [146]), new(7853, [139]), new(7854, [152]),
                    new(7855, [145]), new(7856, [134]), new(7857, [140]), new(7858, [180]),
                    new(7859, [135]), new(7860, [154]), new(7861, [141]), new(7862, [138]),
                ],
                Kind = ZoneItemGoalKind.PerRelicRemaining,
                PerRelic = (1, zeniths.Count - 1), // subtract pld shield
                RelicItemIds = [.. zeniths.Select(r => r.RowId)],
                IsAvailable = () => zeniths.Any(i => i.Value.Handle.IsEquipped),
                UnavailableMessage = "Need relic equipped!",
            },
            new ZoneItemGrindMode {
                DisplayName = "Luminous Crystals (Anima)",
                Goals = [
                    new(13569, [397]), new(13570, [401]), new(13571, [402]),
                    new(13572, [398]), new(13573, [400]), new(13574, [399]),
                ],
                Kind = ZoneItemGoalKind.PerRelicRemaining,
                PerRelic = (1, animateds.Count - 1), // subtract pld shield
                RelicItemIds = [.. animateds.Select(r => r.RowId)],
            },
            new ZoneItemGrindMode {
                DisplayName = "Memories (Resistance)",
                Goals = [
                    new(31573, [397, 401]), // Coerthas Western Highlands, Sea of Clouds
                    new(31574, [398, 400]), // Dravanian Forelands, Churning Mists
                    new(31575, [399, 402]), // Dravanian Hinterlands, Azys Lla
                ],
                Kind = ZoneItemGoalKind.PerRelicRemaining,
                PerRelic = (20, augmented.Count - 1), // subtract pld shield
                RelicItemIds = [.. augmented.Select(r => r.RowId)],
            },
            new ZoneItemGrindMode {
                DisplayName = "Law's Order (Resistance)",
                Goals = [
                    new(32957, [612, 620, 621], 18), // Fringes, Peaks, Lochs
                    new(32958, [613, 614, 622], 18), // Ruby Sea, Yanxia, Azim Steppe
                ],
                IsAvailable = () => QuestAccepted(69575), // The Resistance Remembers
                IsFullyDone = () => QuestComplete(69575),
                UnavailableMessage = $"Need Quest {Quest.GetRow(69575).Name}",
            },
            new ZoneItemGrindMode {
                DisplayName = "Demiatmas (Phantom)",
                Goals = [
                    new(47744, [1187], 3), // Urqopacha
                    new(47745, [1188], 3), // Kozama'uka
                    new(47746, [1189], 3), // Yak T'el
                    new(47747, [1190], 3), // Shaaloani
                    new(47748, [1191], 3), // Heritage Found
                    new(47749, [1192], 3), // Living Memory
                ],
                IsAvailable = () => QuestAccepted(70855), // Arcane Artistry
                IsFullyDone = () => QuestComplete(70855),
                UnavailableMessage = $"Need Quest {Quest.GetRow(70855).Name}",
            },
            new ZoneItemGrindMode {
                DisplayName = "Paste (Phantom)",
                Goals = [
                    new(50059, [.. TerritoryType
                        .Where(r => r.IsInUse && !r.IsPvpZone && r.TerritoryIntendedUse.Value.StructsEnum is TerritoryIntendedUse.Overworld && r.ExVersion.RowId is 5)
                        .Select(r => r.RowId)], 1200),
                ],
                IsAvailable = () => QuestAccepted(70991), // In Pursuit of Perfection
                IsFullyDone = () => QuestComplete(70991),
                UnavailableMessage = $"Need Quest {Quest.GetRow(70991).Name}",
            },
        ];
    }

    private static unsafe bool QuestAccepted(uint questId) => QuestManager.Instance()->IsQuestAccepted(questId);
    private static bool QuestComplete(uint questId) => QuestManager.IsQuestComplete(questId);
}

public sealed class NoneGrindMode : IFateGrindMode {
    public string DisplayName => "None";
    public int UiPriority => -1;

    public IReadOnlySet<uint>? GetAllowedZones() => null;
    public bool IsComplete(IFateGrindRunState _) => false;
    public string? GetRemainingDisplay(IFateGrindRunState state) => state.RemainingUntilCompleted is { } r && r > 0 ? $"{r} fates" : null;
    public IEnumerable<ZoneItemTarget>? GetZoneItemTargets(IFateGrindRunState? state = null) => null;
}

public sealed class GemstoneGrindMode : IFateGrindMode {
    private const uint BicolorGemstone = 26807;

    public string DisplayName => "Gemstones";

    // shb+ zones, prio highest expac
    public IReadOnlySet<uint>? GetAllowedZones() {
        var unlocked = TerritoryType.Where(r => r.IsInUse && r.TerritoryIntendedUse.Value.StructsEnum is TerritoryIntendedUse.Overworld && r.ExVersion.RowId >= 3 && !r.IsPvpZone && r.IsPrimaryAetheryteUnlocked).ToList();
        if (unlocked.Count == 0)
            return new HashSet<uint>();
        var topEx = unlocked.Max(r => r.ExVersion.RowId);
        return unlocked.Where(r => r.ExVersion.RowId == topEx).Select(r => r.RowId).ToHashSet();
    }

    public bool IsComplete(IFateGrindRunState state) => IsCapped && !state.CanSpendGemstones;

    public string? GetRemainingDisplay(IFateGrindRunState state) {
        var remaining = GetGemstoneRemaining();
        return remaining > 0 ? $"{remaining} left" : state.CanSpendGemstones ? "Capped (spending)" : null;
    }

    public IEnumerable<ZoneItemTarget>? GetZoneItemTargets(IFateGrindRunState? state = null) => null;
    internal static bool IsCapped => GetGemstoneRemaining() == 0;
    private const uint SpendThreshold = 1400; // since a bonus fate w/ forlorn bonus can reward a ton, don't want to potentially waste those
    internal static unsafe bool ShouldSpend => CurrencyManager.Instance()->GetItemCount(BicolorGemstone) >= SpendThreshold;
    private static unsafe uint GetGemstoneRemaining() => CurrencyManager.Instance()->GetItemCountRemaining(BicolorGemstone);
}

public enum ZoneItemGoalKind {
    FixedPerItem,
    PerRelicRemaining,
}

public readonly record struct ItemZoneGoal(uint ItemId, IReadOnlyList<uint> Zones, int? FixedRequired = null);

public sealed class ZoneItemGrindMode : IFateGrindMode {
    public required string DisplayName { get; init; }
    public int UiPriority { get; init; } = 100;
    public required IReadOnlyList<ItemZoneGoal> Goals { get; init; }
    public ZoneItemGoalKind Kind { get; init; } = ZoneItemGoalKind.FixedPerItem;
    public (int PerRelic, int TotalRelics)? PerRelic { get; init; }
    public IReadOnlyList<uint>? RelicItemIds { get; init; }
    public Func<bool>? IsAvailable { get; init; }
    public Func<bool>? IsFullyDone { get; init; }
    public string? UnavailableMessage { get; init; }

    public IReadOnlySet<uint>? GetAllowedZones()
        => Goals.SelectMany(g => g.Zones).Where(id => id != 0).ToHashSet();

    public bool IsComplete(IFateGrindRunState state) {
        if (IsFullyDone?.Invoke() ?? false)
            return true;
        if (!(IsAvailable?.Invoke() ?? true))
            return false;
        foreach (var goal in Goals)
            if (GetItemCount(goal.ItemId) < GetEffectiveRequired(goal)) return false;
        return true;
    }

    public string? GetRemainingDisplay(IFateGrindRunState state) {
        if (IsComplete(state))
            return "Done";
        if (!(IsAvailable?.Invoke() ?? true))
            return UnavailableMessage ?? "Unavailable";
        var total = Goals.Sum(g => Math.Max(0, GetEffectiveRequired(g) - GetItemCount(g.ItemId)));
        return total == 0 ? null : $"{total} left";
    }

    public IEnumerable<ZoneItemTarget>? GetZoneItemTargets(IFateGrindRunState? state = null) {
        foreach (var goal in Goals) {
            var total = GetEffectiveRequired(goal);
            if (total <= 0) continue;
            var remaining = Math.Max(0, total - GetItemCount(goal.ItemId));
            if (remaining <= 0) continue;
            foreach (var territoryId in goal.Zones.Where(id => id != 0))
                yield return new ZoneItemTarget(territoryId, goal.ItemId, total);
        }
    }

    private int GetEffectiveRequired(ItemZoneGoal goal) {
        if (Kind == ZoneItemGoalKind.PerRelicRemaining && PerRelic is (var per, var totalRelics)) {
            var done = FateToolKit.GetRelicsCompletedForStep(RelicItemIds);
            return Math.Max(0, (totalRelics - done) * per);
        }
        return goal.FixedRequired ?? 0;
    }

    private static unsafe int GetItemCount(uint itemId) => InventoryManager.Instance()->GetInventoryItemCount(itemId);
}

public sealed class YokaiGrindMode : IFateGrindMode {
    private const int MedalsRequired = 10;

    public string DisplayName => "Yo-kai Watch (Medals)";

    public IReadOnlySet<uint>? GetAllowedZones() {
        var needing = EntriesNeedingFarm().ToList();
        if (needing.Count > 0)
            return needing.SelectMany(e => e.Zones.Select(z => z.RowId)).ToHashSet();
        // keep zone selector disabled when mode supplies zones
        return Yokai.Values.SelectMany(e => e.Zones.Select(z => z.RowId)).ToHashSet();
    }

    public bool IsComplete(IFateGrindRunState _) => !EntriesNeedingFarm().Any();

    public string? GetRemainingDisplay(IFateGrindRunState state) {
        if (state.RemainingUntilCompleted is { } r && r > 0) return $"{r} fates";
        if (IsComplete(state)) return "Done";

        var entry = GetCurrentMinionEntry();
        if (entry is not null && NeedsFarm(entry)) {
            var count = GetItemCount(entry.Medal.RowId);
            var name = entry.Medal.Value.Name.ToString() ?? $"Item {entry.Medal.RowId}";
            return $"{name} {count}/{MedalsRequired}";
        }

        var remaining = EntriesNeedingFarm().Sum(e => Math.Max(0, MedalsRequired - GetItemCount(e.Medal.RowId)));
        return remaining > 0 ? $"{remaining} medals left" : null;
    }

    public IEnumerable<ZoneItemTarget>? GetZoneItemTargets(IFateGrindRunState? state = null) {
        // One entry at a time so shared zones (e.g. Enma/Damona) don't block swaps.
        var entry = GetCurrentMinionEntry() is { } current && NeedsFarm(current) ? current : EntriesNeedingFarm().FirstOrDefault();
        if (entry is null)
            return null;

        return entry.Zones.Select(z => new ZoneItemTarget(z.RowId, entry.Medal.RowId, MedalsRequired));
    }

    /// <summary>True when this zone still has farm work but the summoned minion is not the one that needs it.</summary>
    public static bool NeedsMinionResync(uint territoryId) {
        if (GetCurrentMinionEntry() is { } current && NeedsFarm(current))
            return false;
        return Yokai.Values.Any(e => NeedsFarm(e) && e.Zones.Any(z => z.RowId == territoryId));
    }

    FateSwapZoneActions? IFateGrindMode.GetSwapZoneActions(uint fromTerritoryId, uint toTerritoryId) {
        if (Yokai.Values.FirstOrDefault(e => e.Zones.Any(z => z.RowId == toTerritoryId) && NeedsFarm(e)) is not { } entry)
            return null;

        uint? equipItemId = null;
        var watch = new ItemHandle(15222);
        if (!IsWatchEquipped() && watch.GetCount() > 0)
            equipItemId = watch.ItemId;

        uint? summonCompanionId = IPlayerState.Get().Minion.RowId == entry.Minion.RowId ? null : entry.Minion.RowId;
        if (equipItemId is null && summonCompanionId is null)
            return null;

        return new FateSwapZoneActions(equipItemId, summonCompanionId is not null and uint id ? Companion.GetRowRef(id) : default);
    }

    private static IEnumerable<YokaiEntry> EntriesNeedingFarm() => Yokai.Values.Where(NeedsFarm);

    private static bool NeedsFarm(YokaiEntry entry)
        => entry.Unlocked && GetItemCount(entry.Weapon.RowId) == 0 && GetItemCount(entry.Medal.RowId) < MedalsRequired;

    private static YokaiEntry? GetCurrentMinionEntry()
        => Yokai.Values.FirstOrDefault(e => e.Minion.RowId == IPlayerState.Get().Minion.RowId);

    private static unsafe int GetItemCount(uint itemId) => InventoryManager.Instance()->GetInventoryItemCount(itemId);

    public record YokaiEntry {
        public RowRef<Companion> Minion { get; init; }
        public RowRef<Item> Medal { get; init; }
        public RowRef<Item> Weapon { get; init; }
        public List<RowRef<TerritoryType>> Zones { get; init; }

        public YokaiEntry(uint minion, uint medal, uint weapon, uint[] zones) {
            Minion = Companion.GetRowRef(minion);
            Medal = Item.GetRowRef(medal);
            Weapon = Item.GetRowRef(weapon);
            Zones = [.. zones.Select(z => TerritoryType.GetRowRef(z))];
        }

        public unsafe bool Unlocked => UIState.Instance()->IsCompanionUnlocked(Minion.RowId);
    }

    public static readonly Dictionary<string, YokaiEntry> Yokai = new() {
        ["Jibanyan"] = new(200, 15168, 15210, [148, 135, 141]), // CentralShroud, LowerLaNoscea, CentralThanalan
        ["Komasan"] = new(201, 15169, 15216, [152, 138, 145]), // EastShroud, WesternLaNoscea, EasternThanalan
        ["Whisper"] = new(202, 15170, 15212, [153, 139, 146]), // SouthShroud, UpperLaNoscea, SouthernThanalan
        ["Blizzaria"] = new(203, 15171, 15217, [154, 180, 134]), // NorthShroud, OuterLaNoscea, MiddleLaNoscea
        ["Kyubi"] = new(204, 15172, 15213, [140, 148, 135]), // WesternThanalan, CentralShroud, LowerLaNoscea
        ["Komajiro"] = new(205, 15173, 15219, [141, 152, 138]), // CentralThanalan, EastShroud, WesternLaNoscea
        ["Manjimutt"] = new(206, 15174, 15218, [145, 153, 139]), // EasternThanalan, SouthShroud, UpperLaNoscea
        ["Noko"] = new(207, 15175, 15220, [146, 154, 180]), // SouthernThanalan, NorthShroud, OuterLaNoscea
        ["Venoct"] = new(208, 15176, 15211, [134, 140, 148]), // MiddleLaNoscea, WesternThanalan, CentralShroud
        ["Shogunyan"] = new(209, 15177, 15221, [135, 141, 152]), // LowerLaNoscea, CentralThanalan, EastShroud
        ["Hovernyan"] = new(210, 15178, 15214, [138, 145, 153]), // WesternLaNoscea, EasternThanalan, SouthShroud
        ["Robonyan"] = new(211, 15179, 15215, [139, 146, 154]), // UpperLaNoscea, SouthernThanalan, NorthShroud
        ["USApyon"] = new(212, 15180, 15209, [180, 134, 140]), // OuterLaNoscea, MiddleLaNoscea, WesternThanalan
        ["Lord Enma"] = new(390, 30805, 30809, [612, 613, 614, 620, 621, 622]), // TheFringes, TheRubySea, Yanxia, ThePeaks, TheLochs, TheAzimSteppe
        ["Lord Ananta"] = new(391, 30804, 30808, [397, 398, 399, 400, 401, 402]), // CoerthasWesternHighlands, TheDravanianForelands, TheDravanianHinterlands, TheChurningMists, TheSeaofClouds, AzysLla
        ["Zazel"] = new(392, 30803, 30807, [397, 398, 399, 400, 401, 402]), // CoerthasWesternHighlands, TheDravanianForelands, TheDravanianHinterlands, TheChurningMists, TheSeaofClouds, AzysLla
        ["Damona"] = new(393, 30806, 30810, [612, 613, 614, 620, 621, 622]), // TheFringes, TheRubySea, Yanxia, ThePeaks, TheLochs, TheAzimSteppe
    };

    public static unsafe bool IsWatchEquipped() => InventoryManager.Instance()->GetInventoryContainer(InventoryType.EquippedItems)->GetInventorySlot(10)->ItemId == 15222;
}
