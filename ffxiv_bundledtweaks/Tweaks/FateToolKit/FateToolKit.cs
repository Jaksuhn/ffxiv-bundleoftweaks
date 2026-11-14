using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;

namespace ComplexTweaks.Tweaks;

public enum FateSortCriteria {
    HasBonusWithTwist,
    Progress,
    HasBonus,
    TimeRemainingUrgent,
    Distance,
    TimeRemaining,
    Level,
    Name,
}

public class FateSortOrder {
    public FateSortCriteria Criteria { get; set; }
    public bool Descending { get; set; }
}

public class FateToolKitConfig {
    [IntConfig] public int MaxDuration = 900;
    [IntConfig] public int MinTimeRemaining = 120;
    [IntConfig] public int MaxProgress = 90;
    [IntConfig(Min = 1, Max = 100)] public int MinLevel = 1;
    [IntConfig(Min = 1, Max = 100)] public int MaxLevel = 100;
    [StringConfig(Description = "Available tokens: {Level}, {Name}, {Id}, {Progress}, {TimeRemaining}, {Distance}, {State}")]
    public string DisplayNameFormat = "[{Level}] {Name}";
    [BoolConfig(Description = "Doesn't apply to grind modes")] public bool SwapZones = true;
    [ColorConfig] public Vector4 BarColour = new(0.404f, 0.259f, 0.541f, 1f);
    [BoolConfig(Description = "When gemstones are >= 1400, use Knightshopper to spend them, then keep running fates.")]
    [Requires(Ipc.Knightshopper)]
    public bool SpendCappedGemstones = false;

    public Dictionary<FateType, HashSet<uint>> Blacklist = [];
    public HashSet<PublicEvent.FateRule> BlacklistedRules = [];
    public List<FateSortOrder> SortOrder =
    [
        new() { Criteria = FateSortCriteria.HasBonusWithTwist, Descending = true },
        new() { Criteria = FateSortCriteria.Progress, Descending = true },
        new() { Criteria = FateSortCriteria.HasBonus, Descending = true },
        new() { Criteria = FateSortCriteria.TimeRemainingUrgent, Descending = true },
        new() { Criteria = FateSortCriteria.TimeRemaining, Descending = false },
        new() { Criteria = FateSortCriteria.Distance, Descending = false },
    ];
}

/*
 * TODO:
 * identify fate chains and wait around for the next // hacked together. Needs some RE if the client even knows this
 * more dynamic pull sizes. Like if fates have a ton of enemies, they're generally low health and you could just pull them all
 * better handling of new fates spawning on top of you
 * watch gear durability. Either self repair or just stop if I cba
 * grind modes: check item count on way to fate or something as an interrupt since you don't get the item when fatereward pops
 * vbm:
 * treat all engaged enemies as your own
 * somehow fix engaging enemies as the fight is ending
 * calculate enemies to kill/things to turn in by the fate progress step size
 */

[Requires(Ipc.Navmesh | Ipc.BossMod | Ipc.TextAdvance)]
public class FateToolKit : Tweak<FateToolKitConfig, FateToolKitWindow>, IFateGrindRunState {
    public override string Name => "Fate Tool Kit (Date With Destiny)";
    public override string Description => "Fate tracker with additional fate automations.";

    private const int MinTimeToPrioritise = 240;
    private static readonly CommandRouter<FateToolKit> Router = new(
        CommandNode<FateToolKit>
            .Root()
            .Default(tweak => tweak.Window<FateToolKitWindow>()?.Toggle())
            .Sub("run", "Run until completed count target", node => node
                .ArgInt("count", min: 1)
                .Handle((tweak, args) => tweak.RunUntil(args.Get<int>("count"))))
            .Sub("stop", $"Stops {nameof(FateGrind)} task", node => node.Handle((tweak, _) => tweak.Running = false))
    );

    private static readonly Dictionary<FateSortCriteria, Func<PublicEvent, IComparable>> SortKeys = new() {
        [FateSortCriteria.HasBonusWithTwist] = f => f.HasBonus && (IObjectTable.Get().LocalPlayer?.StatusList.HasTwistOfFate() ?? false),
        [FateSortCriteria.Progress] = f => f.Progress,
        [FateSortCriteria.HasBonus] = f => f.HasBonus,
        // Unactivated fates report negative time; treat them as non-urgent.
        [FateSortCriteria.TimeRemainingUrgent] = f => f.TimeRemaining is >= 0 and < MinTimeToPrioritise,
        [FateSortCriteria.Distance] = f => IObjectTable.Get().LocalPlayer?.DistanceTo(f.Position) ?? 0,
        // Only rank by remaining time for active + urgent fates.
        // Non-urgent and unactivated fates tie here so later criteria (e.g. distance) can decide.
        [FateSortCriteria.TimeRemaining] = f => f.TimeRemaining is >= 0 and < MinTimeToPrioritise ? f.TimeRemaining : MinTimeToPrioritise,
        [FateSortCriteria.Level] = f => f.Level,
        [FateSortCriteria.Name] = f => f.Name,
    };

    public string CurrentState { get; internal set; } = "Idle";
    public int CompletedCount { get; private set; }
    public int? RunUntilCompleted { get; private set; }
    public int? RemainingUntilCompleted => RunUntilCompleted is { } runUntil ? Math.Max(0, runUntil - CompletedCount) : null;
    internal HashSet<uint> SelectedSwapZones { get; } = [];
    internal string SelectedModeId {
        get;
        set {
            if (field == value)
                return;
            field = value;
            RefreshZoneItemTargets();
        }
    } = "None";
    internal bool PendingStopWhenSafe { get; set; } // task sets running = false once no CurrentFate and !InCombat
    internal bool GemstoneSpendExhausted { get; set; } // when KS can't spend anything
    public bool CanSpendGemstones
        => Config.SpendCappedGemstones && !GemstoneSpendExhausted && KnightshopperIPC.Get() is { IsLoaded: true, IsAvailable: true } ks && ks.HasShoppingList(KnightshopperIPC.CurrencyId.BicolorGemstone);
    private List<ZoneItemTarget> ZoneItemTargets { get; set; } = [];

    public bool Running {
        get;
        internal set {
            field = value;
            if (value) {
                PendingStopWhenSafe = false;
                GemstoneSpendExhausted = false;
                ZoneItemTargets = [];
                CompletedCount = 0;
                RefreshZoneItemTargets();
                Svc.Automation.Start(new FateGrind(this));
            }
            else {
                PendingStopWhenSafe = false;
                ZoneItemTargets = [];
                CurrentState = "Idle";
                BossModIPC.Get().ClearActive();
                Svc.Automation.Stop();
                RunUntilCompleted = null;
            }
        }
    }

    public override void OnEnable() => IAddonLifecycle.Get().RegisterListener(AddonEvent.PostSetup, "FateReward", OnFateRewardPostSetup);
    public override void OnDisable() => IAddonLifecycle.Get().UnregisterListener(OnFateRewardPostSetup);

    private void OnFateRewardPostSetup(AddonEvent type, AddonArgs args) {
        if (!Running)
            return;

        CompletedCount++;
        RefreshZoneItemTargets();
        StopIfNoRemaining();
    }

    private void RunUntil(int runUntil) {
        RunUntilCompleted = runUntil;
        if (!Running)
            Running = true;
        else
            StopIfNoRemaining();
    }

    internal void StopIfNoRemaining() {
        if (RunUntilCompleted is { } runUntil && CompletedCount >= runUntil)
            PendingStopWhenSafe = true;
        else if (GetCurrentMode().IsComplete(this))
            PendingStopWhenSafe = true;
    }

    internal bool IsZoneItemTargetComplete(uint currentTerritoryId, out uint destinationTerritoryId) {
        destinationTerritoryId = 0;
        RefreshZoneItemTargets();
        if (ZoneItemTargets.Count == 0)
            return false;

        var stillNeededHere = ZoneItemTargets.Any(t => t.TerritoryId == currentTerritoryId && !t.IsComplete);
        if (stillNeededHere) {
            if (GetCurrentMode() is YokaiGrindMode && YokaiGrindMode.NeedsMinionResync(currentTerritoryId)) {
                destinationTerritoryId = currentTerritoryId;
                return true;
            }
            return false;
        }

        if (GetNextPreferredSwapZone(currentTerritoryId) is { } next) {
            destinationTerritoryId = next;
            return true;
        }
        return false;
    }

    internal IFateGrindMode GetCurrentMode() {
        var displayName = SelectedModeId;
        if (string.IsNullOrEmpty(displayName))
            return FateGrindModes.None;
        return FateGrindModes.GetByDisplayName(displayName) ?? FateGrindModes.None;
    }

    /// <summary>Returns whether the relic (by item ID) has completed the associated quest for this step. Fill in with quest/achievement check.</summary>
    public static bool IsRelicStepComplete(uint relicItemId) {
        // TODO: check quest (or achievement) for this relic; return true when the step is done for that relic
        return false;
    }

    internal static int GetRelicsCompletedForStep(IReadOnlyList<uint>? relicItemIds)
        => relicItemIds is { Count: > 0 } ids ? ids.Count(IsRelicStepComplete) : 0;

    /// <summary>Zones used for swap rotation: mode's allowed zones if set, otherwise selected swap zones.</summary>
    internal IReadOnlySet<uint>? GetEffectiveSwapZones() => GetCurrentMode().GetAllowedZones() ?? (SelectedSwapZones.Count > 0 ? SelectedSwapZones : null);

    /// <summary>True when the current mode defines its own zones; territory selector is disabled to avoid confusion.</summary>
    internal bool ModeSuppliesSwapZones => GetCurrentMode().GetAllowedZones() != null;

    /// <summary>Next zone to swap to; prefers zones where a mode item target is not yet met (e.g. relic atma).</summary>
    internal uint? GetNextPreferredSwapZone(uint currentTerritoryId)
        => ZoneItemTargets.Count > 0 && ZoneItemTargets.Where(t => !t.IsComplete).Select(t => t.TerritoryId).Distinct().ToList() is { Count: > 0 } incomplete
            ? incomplete.Where(z => z != currentTerritoryId).ToList() is { Count: > 0 } others
                ? others[Random.Shared.Next(others.Count)]
                : incomplete[0]
            : GetNextSelectedSwapZone(currentTerritoryId);

    private static unsafe int GetItemCount(uint itemId) => FFXIVClientStructs.FFXIV.Client.Game.InventoryManager.Instance()->GetInventoryItemCount(itemId);

    private void RefreshZoneItemTargets() {
        if (GetCurrentMode().GetZoneItemTargets(this) is not { } targets) {
            ZoneItemTargets = [];
            return;
        }

        ZoneItemTargets = [.. targets];
        CheckItemTargetCompletion();
    }

    private void CheckItemTargetCompletion() {
        if (ZoneItemTargets.Count == 0)
            return;

        for (var i = 0; i < ZoneItemTargets.Count; i++) {
            var target = ZoneItemTargets[i];
            target.IsComplete = GetItemCount(target.ItemId) >= target.RequiredCount;
            ZoneItemTargets[i] = target;
        }
    }

    internal void SyncRunningState() {
        if (Running && !Svc.Automation.Running)
            Running = false;
    }

    internal bool HasSelectedSwapZones => SelectedSwapZones.Count > 0;

    private int _selectedZoneRotation = -1;

    private List<uint> GetOrderedSwapZones(IReadOnlySet<uint> pool) {
        var distinct = pool.Where(id => id != 0).Distinct().ToList();
        return distinct.Count == 0 ? [] : [.. distinct.OrderBy(id => id)];
    }

    internal uint? GetNextSelectedSwapZone(uint currentTerritoryId) {
        var pool = GetEffectiveSwapZones();
        if (pool is null || pool.Count == 0)
            return null;

        var zones = GetOrderedSwapZones(pool);

        if (zones.Count == 0)
            return null;

        if (zones.Count == 1)
            return zones[0];

        _selectedZoneRotation = (_selectedZoneRotation + 1) % zones.Count;
        if (zones[_selectedZoneRotation] == currentTerritoryId)
            _selectedZoneRotation = (_selectedZoneRotation + 1) % zones.Count;
        return zones[_selectedZoneRotation];
    }

    public void ToggleRunning() {
        RunUntilCompleted = null;
        Running ^= true;
    }

    protected override void DrawCustomConfig() {
        ImGui.DrawSection("Blacklisted Rules");

        var rules = Enum.GetValues<PublicEvent.FateRule>().Where(r => r is not PublicEvent.FateRule.None).ToArray();
        var columns = 3;
        if (ImGui.BeginTable("###BlacklistedRules", columns, ImGuiTableFlags.SizingStretchProp)) {
            for (var i = 0; i < rules.Length; i++) {
                if (i % columns == 0)
                    ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.CollectionCheckbox(rules[i].ToString(), rules[i], Config.BlacklistedRules);
            }
            ImGui.EndTable();
        }

        ImGui.DrawSection("Priority Order");
        ImGui.TextColoredWrapped(Colors.Grey, "Order used when selecting the next FATE to complete.");

        var sortOrder = Config.SortOrder.ToList();
        for (var i = 0; i < sortOrder.Count; i++) {
            using var id = ImRaii.PushId($"sort_{i}");
            var item = sortOrder[i];
            var criteria = item.Criteria;

            var handleSize = new Vector2(ImGui.GetFrameHeight());
            ImGui.Button($"##Drag{i}", handleSize);

            ImGui.DragDropSource(i, "FATE_SORT_ITEM"u8, criteria.ToString().Replace("_", " "));
            ImGui.DragDropTarget(i, "FATE_SORT_ITEM"u8, sortOrder.Count, (sourceIndex, insertIndex) => {
                var dragged = sortOrder[sourceIndex];
                sortOrder.RemoveAt(sourceIndex);
                if (sourceIndex < insertIndex)
                    insertIndex--;
                sortOrder.Insert(insertIndex, dragged);
                Config.SortOrder = sortOrder;
            });

            ImGui.TooltipOnHover("Drag to change priority order");
            ImGui.SameLine();

            ImGui.SetNextItemWidth(200);
            using (var critCombo = ImRaii.Combo($"###Criteria{i}", criteria.ToString().Replace("_", " "))) {
                if (critCombo) {
                    foreach (var crit in Enum.GetValues<FateSortCriteria>()) {
                        if (ImGui.Selectable(crit.ToString().Replace("_", " "), crit == criteria)) {
                            item.Criteria = crit;
                            Config.SortOrder[i] = item;
                        }
                    }
                }
            }

            ImGui.SameLine();
            var arrowIcon = item.Descending ? FontAwesomeIcon.ArrowDown : FontAwesomeIcon.ArrowUp;
            if (ImGuiComponents.IconButton($"###Dir{i}", arrowIcon)) {
                item.Descending = !item.Descending;
                Config.SortOrder[i] = item;
            }
            ImGui.TooltipOnHover(item.Descending ? "Descending (highest first)" : "Ascending (lowest first)");

            ImGui.SameLine();
            if (ImGuiComponents.IconButton($"###Remove{i}", FontAwesomeIcon.Trash)) {
                sortOrder.RemoveAt(i);
                Config.SortOrder = sortOrder;
                i--;
            }
            else
                ImGui.TooltipOnHover("Remove this sort criteria");
        }

        if (ImGui.Button("Add Sort Criteria"))
            ImGui.OpenPopup("###AddSortCriteria");

        using (var popup = ImRaii.Popup("###AddSortCriteria")) {
            if (popup) {
                foreach (var crit in Enum.GetValues<FateSortCriteria>()) {
                    if (ImGui.Selectable(crit.ToString().Replace("_", " "))) {
                        sortOrder.Add(new FateSortOrder { Criteria = crit, Descending = true });
                        Config.SortOrder = sortOrder;
                    }
                }
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("Reset to Default"))
            Config.SortOrder = new FateToolKitConfig().SortOrder;
    }

    [CommandHandler(["/dwd", "/vfate"], "Opens the FATE tracker")]
    private void OnCommand(string _, string arguments) {
        var result = Router.Execute(arguments, this, "/dwd");
        if (!string.IsNullOrWhiteSpace(result.Help)) {
            ModuleMessage(result.Help);
            return;
        }

        if (!result.Success) {
            result.Error?.ModuleMessage(this);
            result.Usage?.ModuleMessage(this);
        }
    }

    public void ToggleBlacklist(PublicEvent f) {
        if (!Config.Blacklist.TryGetValue(f.FateType, out var set))
            Config.Blacklist[f.FateType] = set = [];

        if (!set.Add(f.Id))
            set.Remove(f.Id);
    }

    public bool FateConditions(PublicEvent f) => EvaluateFateConditions(f);

    public (bool IsEligible, List<string> FailedConditions) GetFateConditionDetails(PublicEvent f) {
        var failed = new List<string>();
        return (EvaluateFateConditions(f, failed), failed);
    }

    private bool EvaluateFateConditions(PublicEvent f, List<string>? failures = null) {
        var collect = failures is not null;
        var anyFailed = false;

        if (f.Duration > Config.MaxDuration) {
            if (!collect) return false;
            failures!.Add($"Duration {f.Duration}s > MaxDuration {Config.MaxDuration}s");
            anyFailed = true;
        }

        if (f.Progress > Config.MaxProgress) {
            if (!collect) return false;
            failures!.Add($"Progress {f.Progress}% > MaxProgress {Config.MaxProgress}%");
            anyFailed = true;
        }

        if (f.TimeRemaining >= 0 && f.TimeRemaining <= Config.MinTimeRemaining) {
            if (!collect) return false;
            failures!.Add($"TimeRemaining {f.TimeRemaining:F0}s <= MinTimeRemaining {Config.MinTimeRemaining}s");
            anyFailed = true;
        }

        if (Config.Blacklist.TryGetValue(f.FateType, out var set) && set.Contains(f.Id)) {
            if (!collect) return false;
            failures!.Add($"Blacklisted id {f.Id}");
            anyFailed = true;
        }

        if (Config.BlacklistedRules.Contains(f.Rule)) {
            if (!collect) return false;
            failures!.Add($"Blacklisted rule ({f.Rule})");
            anyFailed = true;
        }

        if (f.Level < Config.MinLevel || f.Level > Config.MaxLevel) {
            if (!collect) return false;
            failures!.Add($"Level {f.Level} outside [{Config.MinLevel}, {Config.MaxLevel}]");
            anyFailed = true;
        }

        if (f.IsPending) {
            if (!collect) return false;
            failures!.Add("Pending (not yet active / not on map)");
            anyFailed = true;
        }

        return !anyFailed;
    }

    public IEnumerable<(PublicEvent Fate, bool IsAvailable)> GetOrderedFates() {
        var all = PublicEvent.Fates.ToList();
        if (all.Count == 0)
            yield break;

        var available = all.Where(FateConditions).ToList();
        var unavailable = all.Where(f => !FateConditions(f)).ToList();

        foreach (var f in ApplySortOrder(available, Config.SortOrder))
            yield return (f, true);

        foreach (var f in ApplySortOrder(unavailable, Config.SortOrder))
            yield return (f, false);
    }

    internal static IOrderedEnumerable<PublicEvent> ApplySortOrder(IEnumerable<PublicEvent> source, IReadOnlyList<FateSortOrder> sortOrder) {
        if (!sortOrder.Any())
            return source.OrderBy(_ => 0);

        IOrderedEnumerable<PublicEvent>? ordered = null;

        foreach (var sort in sortOrder) {
            var keySelector = SortKeys.TryGetValue(sort.Criteria, out var key) ? key : (_ => 0);
            ordered = ordered == null
                ? sort.Descending ? source.OrderByDescending(keySelector) : source.OrderBy(keySelector)
                : sort.Descending ? ordered.ThenByDescending(keySelector) : ordered.ThenBy(keySelector);
        }

        return ordered ?? source.OrderBy(_ => 0);
    }
}
