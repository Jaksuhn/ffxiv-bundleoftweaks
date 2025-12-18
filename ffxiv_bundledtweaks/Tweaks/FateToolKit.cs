using Automaton.Events;
using ECommons;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.Exd;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using System.Threading.Tasks;
using TerritoryIntendedUse = FFXIVClientStructs.FFXIV.Client.Enums.TerritoryIntendedUse;

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
    [StringConfig(DefaultValue = "[{Level}] {Name}")] public string DisplayNameFormat = "[{Level}] {Name}";
    [ColorConfig] public Vector4 BarColour = new(0.404f, 0.259f, 0.541f, 1f);

    public Dictionary<FateType, HashSet<uint>> Blacklist = [];
    public List<FateSortOrder> SortOrder =
    [
        new() { Criteria = FateSortCriteria.HasBonusWithTwist, Descending = true },
        new() { Criteria = FateSortCriteria.Progress, Descending = true },
        new() { Criteria = FateSortCriteria.HasBonus, Descending = true },
        new() { Criteria = FateSortCriteria.TimeRemainingUrgent, Descending = false },
        new() { Criteria = FateSortCriteria.Distance, Descending = false },
    ];
}

[Tweak]
[Requires(Ipc.Navmesh | Ipc.BossMod)]
public partial class FateToolKit : Tweak<FateToolKitConfig, FateToolKitWindow> {
    public override string Name => "Fate Tool Kit (Date With Destiny)";
    public override string Description => "Fate tracker with additional fate automations. This is a WIP v3 of Date With Destiny.";

    private const string _presetName = "CBT - DwD";
    private const string _presetCompressed = "G9ofAORUXTtl2E+eebRnK1Vgg5x8KyZxwCk+KT7o7///qSAU4hzPCw6QiMVCHe8HqLWc0tgDpFjjFrDGa+JY19yagv2CAku33ZzO9NHs9/kEgAk1aOYBC4MSniA6R2zxbgTH+thrxzwoOycAC9A5jvXSPxKOSIoM2iU3rvLz52NKNe41MQ8+K+lQW6EwvhxV1TEPWOKXMR+66exXdVOZByy2atfrmlU1/ov6p2ZKTWwZFPP4/VldXaKM9k2XAalyF2WXkQiBpBUcm8bx7pIj4aDIVBebYGKke3VltoJwrJkHrDiYhhYQ0tingPyN8kPib2KBqaXdGrvXgAd4Kiaod7DwMAvSxMqtxISvg+dZZNQ+ROLrwcK4Np2PB39tejhTM2vDaVvZNAa2YTmOCNALkyMZ9PnwbMkMWjjKiDbiY2UdOQKwFD7Xu7Q03lZzB6y8bKXZYEXkoquCJRWBS7qzcFvHSiRX4d4i/eiD5TWb7mLFL8r4aAnkp/S+ZQ07yGC9fLGNIMjtRV/tqy+X95EwMda4Q4jDbqR4sMjII+IkkmlQN6g7lyjSz2R6Z/ThFFdbgaH9aedlWEyB46B6jic9aLtJbom0I2P9C0exTW8VHRpja4ls8wnA/3zyDw==";
    private static readonly string _preset = _presetCompressed.FromBase64();
    private const int MinTimeToPrioritise = 240;
    private uint? _nextFateId;

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

    private static IOrderedEnumerable<PublicEvent> ApplySortOrder(IEnumerable<PublicEvent> source, IReadOnlyList<FateSortOrder> sortOrder) {
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
    private int PullSize => ExdModule.GetRoleForClassJobId(Player.ClassJob.RowId) switch {
        1 => 0, // tank - unlimited
        2 => 3, // dps
        3 => 5, // healer
        _ => 1, // non-combat
    };

    public bool Running {
        get;
        private set {
            field = value;
            if (value) {
                EnableEventHandlers();
                Service.Automation.Start(new FateGrind(this));
            }
            else {
                DisableEventHandlers();
                Svc.Automation.Stop();
            }
        }
    }

    public void ToggleRunning() => Running ^= true;
    [CommandHandler(["/dwd", "/vfate"], "Opens the FATE tracker")]
    private void OnCommand(string _, string __) => Window<FateToolKitWindow>()?.Toggle();

    [TweakEvent(TweakEvent.FateJoined, AutoEnable = false)]
    private void OnFateJoined(Type _, EventArgs args) {
        if (args is FateEventArgs { FateId: var id } && _nextFateId.HasValue && id != _nextFateId.Value) return;

        if (Service.BossMod.GetActive() != _presetName) {
            if (Service.BossMod.Get(_presetName) is null)
                Service.BossMod.Create(_preset, true);
            else
                Service.BossMod.SetActive(_presetName);
        }

        Svc.BossMod.AddTransientStrategy(_presetName, "BossMod.Autorotation.MiscAI.AutoTarget", "MaxTargets", PullSize.ToString());
    }

    [TweakEvent(TweakEvent.FateLeft, AutoEnable = false)]
    private void OnFateLeft(Type _, EventArgs __) {
        _nextFateId = null;
        Service.BossMod.ClearActive();
        if (!Svc.Condition[ConditionFlag.Unconscious]) // we only want natural leavings to retrigger the grind, otherwise it would conflict with reviving
            Service.Automation.Start(new FateGrind(this));
    }

    [TweakEvent(TweakEvent.Died, AutoEnable = false)]
    private void OnDeath(Type _, EventArgs __) {
        Service.Automation.Start(new FateGrind(this));
    }

    [TweakEvent(TweakEvent.Revived, AutoEnable = false)]
    private void OnRevived(Type senderType, EventArgs __) {
        Service.Automation.Start(new FateGrind(this), queue: true);
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

    private sealed class FateGrind(FateToolKit tweak) : TaskBase {
        protected override async Task Execute() {
            try {
                await (State switch {
                    FateState.Unconscious => Revive(),
                    FateState.Moving => MoveToFate(),
                    FateState.WaitingForFates => HandleNoFates(),
                    _ => NextFrame(),
                });
            }
            catch (Exception ex) {
                Error($"Error: {ex}");
                tweak.Running = false;
            }
        }

        public PublicEvent? NextFate { get; set { field = value; tweak._nextFateId = field?.Id; } }

        public unsafe IOrderedEnumerable<PublicEvent> AvailableFates => ApplySortOrder(PublicEvent.Fates.Where(FateConditions), tweak.Config.SortOrder);

        private bool FateConditions(PublicEvent f)
            => f.Duration <= tweak.Config.MaxDuration
            && f.Progress <= tweak.Config.MaxProgress
            && (f.TimeRemaining < 0 || f.TimeRemaining > tweak.Config.MinTimeRemaining)
            && !tweak.IsBlacklisted(f);

        private FateState State {
            get {
                if (Svc.Condition[ConditionFlag.Unconscious])
                    return FateState.Unconscious;

                // Always engage current fate, even if it doesn't meet conditions anymore (e.g., raised and fate progressed while dead)
                if (PublicEvent.CurrentFate is { })
                    return FateState.Engaging;

                if (AvailableFates.FirstOrDefault() is { })
                    return FateState.Moving;

                if (!AvailableFates.Any())
                    return FateState.WaitingForFates;

                return FateState.Idle;
            }
        }

        private enum FateState {
            Idle,
            WaitingForFates,
            Moving,
            Engaging,
            Unconscious,
        }

        private async Task Revive() {
            using var scope = BeginScope(nameof(Revive));
            await WaitUntil(() => Player.Revivable, "WaitForRevivable");
            (var lastZone, var lastPos) = (Player.Territory, Player.Position);
            if (Svc.Party.Length is 0) {
                GameMain.ExecuteCommand(CommandFlag.Revive.Value, AgentReviveOp.Return.Value);
            }
            else {
                await WaitUntil(() => Player.ReviveState is 2, "WaitingForRaise"); // 1 = return, 2 = raise
                GameMain.ExecuteCommand(CommandFlag.Revive.Value, AgentReviveOp.AcceptRevive.Value); // a1=5 for raises
            }
            await WaitWhile(() => Svc.Condition[ConditionFlag.Unconscious], "WaitForAlive");

            if (Player.Territory.RowId != lastZone.RowId) {
                await TeleportTo(lastZone.RowId, lastPos);
            }
        }

        private async Task MoveToFate() {
            using var scope = BeginScope(nameof(MoveToFate));
            if (AvailableFates.FirstOrDefault() is not { } nextFate) return;
            NextFate = nextFate;
            await WaitWhile(() => Player.IsBusy, "WaitingForNotBusy");
            var rnd = NextFate.Position.RandomPoint(NextFate.Radius * 0.5f);
            var msh = rnd.OnMesh();
            Log($"NextFate Position: {NextFate.Position} -> {rnd} -> {msh}");
            await MoveTo(msh, MovementConfig.Everything);
            if (NextFate is { State: FFXIVClientStructs.FFXIV.Client.Game.Fate.FateState.Preparing })
                await StartFate();
        }

        private async Task StartFate() {
            using var scope = BeginScope(nameof(StartFate));
            if (NextFate?.MotivationNpc is not { } npc) return;
            await MoveTo(npc.Position, MovementConfig.InteractRange);
            await InteractWith(npc, () => NextFate?.State == FFXIVClientStructs.FFXIV.Client.Game.Fate.FateState.Running);
        }

        private async Task HandleNoFates() {
            if (tweak.Config.SwapZones) {
                using var scope = BeginScope("SwapZones");
                await TeleportTo(GetNextAchievementZone() ?? GetRandomSameExpacZone(), default);
                Svc.Automation.Start(new FateGrind(tweak)); // TODO: hacky
            }
            else {
                using var scope = BeginScope("WaitForFates");
                Status = "Waiting for fates to spawn";
                await NextFrame(60);
            }
        }

        private unsafe uint? GetNextAchievementZone() {
            var agent = AgentFateProgress.Instance();
            if (agent == null) return null;

            // prioritise zones in the same expac as current area
            var currentTabIndex = Array.FindIndex(agent->Tabs.ToArray(), tab => tab.Zones.ToArray().Any(zone => Player.Territory.RowId == zone.TerritoryTypeId));
            var zones = (currentTabIndex != -1 && currentTabIndex < agent->Tabs.Length - 1)
                ? agent->Tabs[currentTabIndex].Zones.ToArray()
                : agent->Tabs.ToArray().SelectMany(tab => tab.Zones.ToArray());

            return zones.FirstOrNull(zone => zone.NeededFates - zone.FateProgress > 0)?.TerritoryTypeId;
        }

        private uint GetRandomSameExpacZone() {
            var rows = FindRows<TerritoryType>(x => x.IsInUse && (TerritoryIntendedUse)x.TerritoryIntendedUse.RowId is TerritoryIntendedUse.Overworld && x.ExVersion.RowId == Player.Territory.Value.ExVersion.RowId);
            return rows[new Random().Next(rows.Length)].RowId;
        }
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
