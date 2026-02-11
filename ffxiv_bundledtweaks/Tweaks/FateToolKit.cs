using ECommons;
using Lumina.Excel;
using Lumina.Excel.Sheets;

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
    [IntConfig(DefaultValue = 900)] public int MaxDuration = 900;
    [IntConfig(DefaultValue = 120)] public int MinTimeRemaining = 120;
    [IntConfig(DefaultValue = 90)] public int MaxProgress = 90;
    [BoolConfig] public bool SwapZones = true;

    public string DisplayNameFormat = "[{Level}] {Name}";
    public Vector4 BarColour = new(0.404f, 0.259f, 0.541f, 1f);
    public Dictionary<FateType, HashSet<uint>> Blacklist = [];
    public List<FateSortOrder> SortOrder =
    [
        new() { Criteria = FateSortCriteria.HasBonusWithTwist, Descending = true },
        new() { Criteria = FateSortCriteria.Progress, Descending = true },
        new() { Criteria = FateSortCriteria.HasBonus, Descending = true },
        new() { Criteria = FateSortCriteria.TimeRemainingUrgent, Descending = false },
        new() { Criteria = FateSortCriteria.TimeRemaining, Descending = false },
        new() { Criteria = FateSortCriteria.Distance, Descending = false },
    ];
}

/*
 * TODO:
 * announce next fate in party chat? might be good if you were multiboxing
 * better handling of hitting a mob at the end of the fight (don't think I can do anything tbh, vbm needs to) // done?
 * identify fate chains and wait around for the next
 * config: blacklist fate types
 * gemstone spending or at least stop when full
 * more dynamic pull sizes. Like if fates have a ton of enemies, they're generally low health and you could just pull them all
 * better handling of new fates spawning on top of you
 * 
 * vbm:
 * treat all engaged enemies as your own
 * somehow fix engaging enemies as the fight is ending
 * calculate enemies to kill/things to turn in by the fate progress step size
 */

[Tweak]
[Requires(Ipc.Navmesh | Ipc.BossMod | Ipc.TextAdvance)]
public partial class FateToolKit : Tweak<FateToolKitConfig, FateToolKitWindow> {
    public override string Name => "Fate Tool Kit (Date With Destiny)";
    public override string Description => "Fate tracker with additional fate automations. This is a WIP v3 of Date With Destiny.";

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
        [FateSortCriteria.HasBonusWithTwist] = f => f.HasBonus && Player.Status.FirstOrDefault(x => DateWithDestiny.TwistOfFateStatusIDs.Contains(x.StatusId)) != null,
        [FateSortCriteria.Progress] = f => f.Progress,
        [FateSortCriteria.HasBonus] = f => f.HasBonus,
        [FateSortCriteria.TimeRemainingUrgent] = f => f.TimeRemaining < MinTimeToPrioritise,
        [FateSortCriteria.Distance] = f => Player.DistanceTo(f.Position),
        [FateSortCriteria.TimeRemaining] = f => f.TimeRemaining < 0 ? float.MaxValue : f.TimeRemaining,
        [FateSortCriteria.Level] = f => f.Level,
        [FateSortCriteria.Name] = f => f.Name,
    };

    public string CurrentState { get; internal set; } = "Idle";
    public int CompletedCount { get; private set; }
    public int? RunUntilCompleted { get; private set; }
    public int? RemainingUntilCompleted => RunUntilCompleted is { } runUntil ? Math.Max(0, runUntil - CompletedCount) : null;

    public bool Running {
        get;
        internal set {
            field = value;
            if (value) {
                CompletedCount = 0;
                Service.Automation.Start(new FateGrind(this));
            }
            else {
                CurrentState = "Idle";
                Service.BossMod.ClearActive();
                Svc.Automation.Stop();
                RunUntilCompleted = null;
            }
        }
    }

    public override void Enable() => Svc.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "FateReward", OnFateRewardPostSetup);
    public override void Disable() => Svc.AddonLifecycle.UnregisterListener(OnFateRewardPostSetup);

    private void OnFateRewardPostSetup(AddonEvent type, AddonArgs args) {
        if (!Running)
            return;

        CompletedCount++;
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
            Running = false;
    }

    internal void SyncRunningState() {
        if (Running && !Service.Automation.Running)
            Running = false;
    }

    public void ToggleRunning() {
        RunUntilCompleted = null;
        Running ^= true;
    }

    [CommandHandler(["/dwd", "/vfate"], "Opens the FATE tracker")]
    private void OnCommand(string _, string arguments) {
        var result = Router.Execute(arguments, this, "/dwd");
        if (!string.IsNullOrWhiteSpace(result.Help)) {
            ModuleMessage(result.Help!);
            return;
        }

        if (!result.Success) {
            ModuleMessage(result.Error!);
            if (!string.IsNullOrWhiteSpace(result.Usage))
                ModuleMessage(result.Usage!);
        }
    }

    internal bool IsBlacklisted(PublicEvent f)
        => Config.Blacklist.TryGetValue(f.FateType, out var set) && set.Contains(f.Id);

    public void ToggleBlacklist(PublicEvent f) {
        if (!Config.Blacklist.TryGetValue(f.FateType, out var set))
            Config.Blacklist[f.FateType] = set = [];

        if (!set.Add(f.Id))
            set.Remove(f.Id);
    }

    public bool FateConditions(PublicEvent f)
        => f.Duration <= Config.MaxDuration
        && f.Progress <= Config.MaxProgress
        && (f.TimeRemaining < 0 || f.TimeRemaining > Config.MinTimeRemaining)
        && !IsBlacklisted(f);

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
            var keySelector = sort.Criteria == FateSortCriteria.TimeRemaining && sort.Descending
                ? (f => f.TimeRemaining < 0 ? float.MinValue : f.TimeRemaining)
                : SortKeys.TryGetValue(sort.Criteria, out var key) ? key : (_ => 0);

            ordered = ordered == null
                ? sort.Descending ? source.OrderByDescending(keySelector) : source.OrderBy(keySelector)
                : sort.Descending ? ordered.ThenByDescending(keySelector) : ordered.ThenBy(keySelector);
        }

        return ordered ?? source.OrderBy(_ => 0);
    }
}

public partial class FateToolKit {
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
    }

    public readonly Dictionary<string, YokaiEntry> Yokai = new() {
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
}
