using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using System.Threading;
using System.Threading.Tasks;
using TerritoryIntendedUse = FFXIVClientStructs.FFXIV.Client.Enums.TerritoryIntendedUse;

namespace ComplexTweaks.Tweaks;

public interface IFateGrindRunState {
    int CompletedCount { get; }
    int? RunUntilCompleted { get; }
    int? RemainingUntilCompleted { get; }
    int RelicsCompletedForStep { get; }
}

internal interface IFateGrindMode {
    string DisplayName { get; }
    int UiPriority => 0;

    /// <summary>Swap zone override</summary>
    IReadOnlySet<uint>? GetAllowedZones();

    /// <summary>Condition for mode being done (items collected, quest done, etc)</summary>
    bool IsComplete(IFateGrindRunState state);

    /// <summary>Optional chip display for progress</summary>
    string? GetRemainingDisplay(IFateGrindRunState state);

    IEnumerable<(uint TerritoryId, uint ItemId, int RequiredCount)>? GetZoneItemTargets(IFateGrindRunState? state = null);
    Task OnSwapZone(uint fromTerritoryId, uint toTerritoryId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    bool UsesRelicsCompletedForStep() => false;
    IReadOnlyList<uint>? RelicItemIds => null;
}

public sealed class NoneGrindMode : IFateGrindMode {
    public string DisplayName => "None";
    public int UiPriority => -1;

    public IReadOnlySet<uint>? GetAllowedZones() => null;
    public bool IsComplete(IFateGrindRunState state) => state.RunUntilCompleted is { } runUntil && state.CompletedCount >= runUntil;
    public string? GetRemainingDisplay(IFateGrindRunState state) => state.RemainingUntilCompleted is { } r && r > 0 ? $"{r} fates" : null;
    public IEnumerable<(uint TerritoryId, uint ItemId, int RequiredCount)>? GetZoneItemTargets(IFateGrindRunState? state = null) => null;
}

public static class FateGrindModes {
    private static readonly List<IFateGrindMode> _discovered;
    private static readonly List<IFateGrindMode> _registered = [];

    static FateGrindModes() {
        var iface = typeof(IFateGrindMode);
        _discovered = [.. typeof(IFateGrindMode).Assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && iface.IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) != null)
            .Select(t => (IFateGrindMode)Activator.CreateInstance(t)!)
            .OrderBy(m => m.DisplayName)];

        var zeniths = RelicItem.GetItemsByStep(2);
        Register(new RelicItemMultiZoneGrindMode(
            "Atma (Zodiac)",
            [(7851, [148]), (7852, [146]), (7853, [139]), (7854, [152]), (7855, [145]), (7856, [134]), (7857, [140]), (7858, [180]), (7859, [135]), (7860, [154]), (7861, [141]), (7862, [138])],
            relicItemIds: [.. zeniths.Select(r => r.RowId)],
            perRelicInfo: (1, zeniths.Count - 1), // subtract pld shield
            requiredCondition: () => zeniths.Any(i => i.Value.Handle.IsEquipped)));

        var animateds = QuestClassJobReward.GetRelicsByRow(3);
        Register(new RelicItemMultiZoneGrindMode(
            "Luminous Crystals (Anima)",
            [(13569, [397]), (13570, [401]), (13571, [402]), (13572, [398]), (13573, [400]), (13574, [399])],
            relicItemIds: [.. animateds.Select(r => r.RowId)],
            perRelicInfo: (1, animateds.Count - 1))); // subtract pld shield

        var augmented = QuestClassJobReward.GetRelicsByRow(17);
        Register(new RelicItemMultiZoneGrindMode(
            "Memories (Resistance)",
            [
                (31573, [397, 401]), // Coerthas Western Highlands, Sea of Clouds
                (31574, [398, 400]), // Dravanian Forelands, Churning Mists
                (31575, [399, 402]), // Dravanian Hinterlands, Azys Lla
            ],
            relicItemIds: [.. augmented.Select(r => r.RowId)],
            perRelicInfo: (20, augmented.Count - 1))); // subtract pld shield

        Register(new RelicItemMultiZoneGrindMode(
            "Law's Order (Resistance)",
            [
                (32957, [612, 620, 621], 18), // Fringes, Peaks, Lochs
                (32958, [613, 614, 622], 18), // Ruby Sea, Yanxia, Azim Steppe
            ],
            questId: 69575)); // The Resistance Remembers

        Register(new RelicItemMultiZoneGrindMode(
            "Demiatmas (Phantom)",
            [
                (47744, [1187], 3), // Urqopacha
                (47745, [1188], 3), // Kozama'uka
                (47746, [1189], 3), // Yak T'el
                (47747, [1190], 3), // Shaaloani
                (47748, [1191], 3), // Heritage Found
                (47749, [1192], 3), // Living Memory
            ],
            questId: 70855)); // Arcane Artistry

        Register(new RelicItemMultiZoneGrindMode(
            "Paste (Phantom)",
            [(50059, TerritoryType.Where(r => r.IsInUse && !r.IsPvpZone && r.TerritoryIntendedUse.Value.StructsEnum is TerritoryIntendedUse.Overworld && r.ExVersion.RowId is 5).Select(r => r.RowId).ToList(), 1200)],
            questId: 70991)); // In Pursuit of Perfection
    }

    /// <summary>Discovered modes (None, Gemstone, etc.) by UiPriority then name; registered relic modes in expansion/registration order.</summary>
    internal static IReadOnlyList<IFateGrindMode> All => [.. _discovered.OrderBy(m => m.UiPriority).ThenBy(m => m.DisplayName), .. _registered];

    internal static IFateGrindMode? GetByDisplayName(string displayName) => All.FirstOrDefault(m => m.DisplayName == displayName);
    internal static IFateGrindMode? GetNoneMode() => All.FirstOrDefault(m => m.UiPriority == -1);

    internal static void Register(IFateGrindMode mode) {
        if (All.Any(m => m.DisplayName == mode.DisplayName)) return;
        _registered.Add(mode);
    }
}

public sealed class YokaiGrindMode : IFateGrindMode {
    public string DisplayName => "Yo-kai Watch (Medals)";

    public IReadOnlySet<uint>? GetAllowedZones() {
        if (GetCurrentMinionEntry() is { } entry)
            return entry.Zones.Select(z => z.RowId).ToHashSet();
        // return all possible zones so the zone selector still gets disabled
        return Yokai.Values.SelectMany(e => e.Zones.Select(z => z.RowId)).ToHashSet();
    }

    public bool IsComplete(IFateGrindRunState state)
        => state.RunUntilCompleted is { } runUntil && state.CompletedCount >= runUntil;

    public string? GetRemainingDisplay(IFateGrindRunState state) {
        if (state.RemainingUntilCompleted is { } r && r > 0) return $"{r} fates";
        var entry = GetCurrentMinionEntry();
        if (entry is null) return null;
        var count = GetItemCount(entry.Medal.RowId);
        var name = entry.Medal.Value.Name.ToString() ?? $"Item {entry.Medal.RowId}";
        return count < 10 ? $"{name} {count}/10" : null;
    }

    public IEnumerable<(uint TerritoryId, uint ItemId, int RequiredCount)>? GetZoneItemTargets(IFateGrindRunState? state = null) => null;

    public async Task OnSwapZone(uint fromTerritoryId, uint toTerritoryId, CancellationToken cancellationToken) {
        if (Yokai.Values.FirstOrDefault(e => e.Zones.Any(z => z.RowId == toTerritoryId) && GetItemCount(e.Medal.RowId) < 10 && e.Unlocked) is not { } entry) return;

        var watch = new ItemHandle(15222);
        if (!IsWatchEquipped() && watch.GetCount() > 0) {
            watch.Equip();
            while (!IsWatchEquipped())
                await NextFrames(30, cancellationToken);
        }

        ECommons.Automation.Chat.SendMessage($"/minion {entry.Minion.Value.Singular}");
        while (CurrentCompanion.RowId != entry.Minion.RowId)
            await NextFrames(30, cancellationToken);
    }

    private static Task NextFrames(int n, CancellationToken ct) => Svc.Framework.DelayTicks(n, ct);

    private static YokaiEntry? GetCurrentMinionEntry()
        => Yokai.Values.FirstOrDefault(e => e.Minion.RowId == CurrentCompanion.RowId);

    private static unsafe int GetItemCount(uint itemId) => InventoryManager.Instance()->GetInventoryItemCount(itemId);

    public record YokaiEntry {
        public RowRef<Companion> Minion { get; init; }
        public RowRef<Item> Medal { get; init; }
        public RowRef<Item> Weapon { get; init; }
        public List<RowRef<TerritoryType>> Zones { get; init; }

        public YokaiEntry(uint minion, uint medal, uint weapon, uint[] zones) {
            Minion = Companion.GetRef(minion);
            Medal = Item.GetRef(medal);
            Weapon = Item.GetRef(weapon);
            Zones = [.. zones.Select(z => TerritoryType.GetRef(z))];
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
    public static unsafe RowRef<Companion> CurrentCompanion => Companion.GetRef(Player.Character->CompanionObject->Character.GameObject.BaseId);
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

    public bool IsComplete(IFateGrindRunState _) => GetGemstoneRemaining() == 0;

    public string? GetRemainingDisplay(IFateGrindRunState _) {
        var remaining = GetGemstoneRemaining();
        return remaining > 0 ? $"{GetGemstoneRemaining()} left" : null;
    }

    public IEnumerable<(uint TerritoryId, uint ItemId, int RequiredCount)>? GetZoneItemTargets(IFateGrindRunState? state = null) => null;
    private static unsafe uint GetGemstoneRemaining() => CurrencyManager.Instance()->GetItemCountRemaining(BicolorGemstone);
}

// generic mode for relics where you have to collect x per relic or in total
public sealed class RelicZoneItemGrindMode(string displayName, IEnumerable<(uint TerritoryId, uint ItemId, int RequiredCount)> targets) : IFateGrindMode {
    public string DisplayName { get; } = displayName;

    private readonly List<(uint TerritoryId, uint ItemId, int RequiredCount)> _targets = [.. targets];

    public IReadOnlySet<uint>? GetAllowedZones() => _targets.Select(t => t.TerritoryId).ToHashSet();

    public bool IsComplete(IFateGrindRunState _) {
        foreach (var (_, itemId, required) in _targets) {
            if (GetItemCount(itemId) < required) return false;
        }
        return true;
    }

    public string? GetRemainingDisplay(IFateGrindRunState _) {
        var total = _targets.Sum(t => Math.Max(0, t.RequiredCount - GetItemCount(t.ItemId)));
        return total == 0 ? null : $"{total} left";
    }

    public IEnumerable<(uint TerritoryId, uint ItemId, int RequiredCount)>? GetZoneItemTargets(IFateGrindRunState? state = null) => _targets;

    private static unsafe int GetItemCount(uint itemId) => InventoryManager.Instance()->GetInventoryItemCount(itemId);
}

public sealed class RelicItemMultiZoneGrindMode(
    string displayName,
    IEnumerable<(uint ItemId, IReadOnlyList<uint> TerritoryIds, int? TotalRequired)> itemZones,
    IReadOnlyList<uint>? relicItemIds = null,
    (int PerRelic, int TotalRelics)? perRelicInfo = null,
    Func<bool>? requiredCondition = null,
    Func<bool>? questCompleteOverride = null,
    uint? questId = null) : IFateGrindMode {

    public RelicItemMultiZoneGrindMode(
        string displayName,
        IEnumerable<(uint ItemId, IReadOnlyList<uint> TerritoryIds)> itemZones,
        IReadOnlyList<uint>? relicItemIds,
        (int PerRelic, int TotalRelics)? perRelicInfo,
        Func<bool>? requiredCondition = null,
        Func<bool>? questCompleteOverride = null,
        uint? questId = null)
        : this(displayName, itemZones.Select(x => (x.ItemId, x.TerritoryIds, (int?)null)), relicItemIds, perRelicInfo, requiredCondition, questCompleteOverride, questId) { }

    public string DisplayName { get; } = displayName;

    private readonly List<(uint ItemId, IReadOnlyList<uint> TerritoryIds, int? TotalRequired)> _itemZones = [.. itemZones];
    private readonly (int PerRelic, int TotalRelics)? _perRelicInfo = perRelicInfo;
    private readonly uint? _questId = questId;
    private readonly unsafe Func<bool>? _requiredCondition = questId is { } q ? () => QuestManager.Instance()->IsQuestAccepted(q) : requiredCondition;
    private readonly Func<bool>? _questCompleteOverride = questId is { } q ? () => QuestManager.IsQuestComplete(q) : questCompleteOverride;

    public IReadOnlyList<uint>? RelicItemIds => relicItemIds;

    public bool UsesRelicsCompletedForStep() => _perRelicInfo.HasValue;

    public IReadOnlySet<uint>? GetAllowedZones()
        => _itemZones.SelectMany(x => x.TerritoryIds).Where(id => id != 0).ToHashSet();

    public bool IsComplete(IFateGrindRunState state) {
        if (_questCompleteOverride?.Invoke() ?? false)
            return true;
        if (!_requiredCondition?.Invoke() ?? false)
            return false;
        foreach (var entry in _itemZones)
            if (GetItemCount(entry.ItemId) < GetEffectiveRequired(entry, state)) return false;
        return true;
    }

    public string? GetRemainingDisplay(IFateGrindRunState state) {
        if (IsComplete(state))
            return "Done";
        if (!_requiredCondition?.Invoke() ?? false)
            return _questId is { } q ? $"Need Quest #{q}" : "Need relic equipped!";
        var total = _itemZones.Sum(e => Math.Max(0, GetEffectiveRequired(e, state) - GetItemCount(e.ItemId)));
        return total == 0 ? null : $"{total} left";
    }

    public IEnumerable<(uint TerritoryId, uint ItemId, int RequiredCount)>? GetZoneItemTargets(IFateGrindRunState? state = null) {
        foreach (var entry in _itemZones) {
            var total = GetEffectiveRequired(entry, state);
            if (total <= 0) continue;
            var current = GetItemCount(entry.ItemId);
            var remaining = Math.Max(0, total - current);
            if (remaining <= 0) continue;
            foreach (var territoryId in entry.TerritoryIds.Where(id => id != 0))
                yield return (territoryId, entry.ItemId, remaining);
        }
    }

    private int GetEffectiveRequired((uint ItemId, IReadOnlyList<uint> TerritoryIds, int? TotalRequired) entry, IFateGrindRunState? state) {
        if (_perRelicInfo is (var per, var totalRelics)) {
            var done = state?.RelicsCompletedForStep ?? 0;
            return Math.Max(0, (totalRelics - done) * per);
        }
        return entry.TotalRequired ?? 0;
    }

    private static unsafe int GetItemCount(uint itemId) => InventoryManager.Instance()->GetInventoryItemCount(itemId);
}
