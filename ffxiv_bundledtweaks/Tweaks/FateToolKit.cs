using ECommons;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
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

    public string DisplayNameFormat = "[{Level}] {Name}";
    public Vector4 BarColour = new(0.404f, 0.259f, 0.541f, 1f);
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
[Requires(Ipc.Navmesh | Ipc.BossMod | Ipc.TextAdvance)]
public partial class FateToolKit : Tweak<FateToolKitConfig, FateToolKitWindow> {
    public override string Name => "Fate Tool Kit (Date With Destiny)";
    public override string Description => "Fate tracker with additional fate automations. This is a WIP v3 of Date With Destiny.";

    private const string _presetName = "CBT - DwD";
    private const string _presetCompressed = "G7sgAORUXTtl2E+e+WjPVqrAAflbPZILOMWW4oP+/v+fCkIhzvG84ACJWKy03g9QazmlsQdIscatVWu8Jo51H+IdQI2Y/jafAIgSBxI+iCh8ggeIT7FYvBsibX3oyQkfvp0RQITWOWlrLziwHS0ja2s8qV0VFM9HPOG4IxY+fP6kw98KhNFPI1ad8EGkwVOChw66yKpuovBBJAa3PdXiV+P/U9/QTEWMlgExR9Cf8OIautG+4TKoVL4zGi8uI4qkFB6axuHukAPbAVlXOxtgfEw9XpCtINLWwgfxuNcNLyDisW/8CtaMH5RgnRCYeNqpNjsWcKM8fSbI7mCRUV5IK5dOlU3x2ricRQ7tQ5R9bVl0XBvJx6P+2shwJsusDaZtJaYxsIme1BEBeCFKy5r2uezJsB6IcHQjyomPlVWsEYDlMZDsLi1LtpXZASsvGyVssFIWpVQFS1aGWtKdRYp1rMRqWdxblD76YHnNjbtYCR6fykdLqKzS+xY37ADDlfPFNqKC3F7oYl4DtbgPbFNttNtHdtiqFA8WFeuIOIlVVqge1O1LkemZde8EfSiPF1NRhvynbSTDEp7ReBE6plH48Pl9B+SEPY1B0WdEUk/UVIhCs6O6DPsJTQddZeT5LO04YC/tkQYyoQAsMTnWhnwckdPwBnnfaFMzuct8AvA/n/wD";
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
    public string CurrentState { get; internal set; } = "Idle";

    public bool Running {
        get;
        private set {
            field = value;
            if (value) {
                Service.Automation.Start(new FateGrind(this));
            }
            else {
                _nextFateId = null;
                Service.BossMod.ClearActive();
                Svc.Automation.Stop();
            }
        }
    }

    public void ToggleRunning() => Running ^= true;
    [CommandHandler(["/dwd", "/vfate"], "Opens the FATE tracker")]
    private void OnCommand(string _, string __) => Window<FateToolKitWindow>()?.Toggle();

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
        private PublicEvent? _lastCurrentFate;

        private int PullSize => Player.ClassJob.Value switch {
            var cj when cj.IsTank => 0, // unlimited
            var cj when cj.IsDps => 3,
            var cj when cj.IsHealer => 5,
            _ => 1,
        };

        protected override async Task Execute() {
            using var stop = new OnDispose(() => Svc.TextAdvance.DisableExternalControl(Plugin.Name));
            try {
                while (!CancelToken.IsCancellationRequested && tweak.Running) {
                    var state = State;
                    tweak.CurrentState = state.ToString();

                    switch (state) {
                        case FateState.FateEntered:
                            OnFateEntered();
                            break;
                        case FateState.FateLeft:
                            OnFateLeft();
                            break;
                        case FateState.Unconscious:
                            await Revive();
                            break;
                        case FateState.Moving:
                            await MoveToFate();
                            break;
                        case FateState.WaitingForFates:
                            await HandleNoFates();
                            break;
                        default:
                            await NextFrame();
                            break;
                    }
                }
            }
            catch (OperationCanceledException) {
                throw; // expected, don't log
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

        private unsafe FateState State {
            get {
                if (Svc.Condition[ConditionFlag.Unconscious])
                    return FateState.Unconscious;

                var currentFate = PublicEvent.CurrentFate;

                if (currentFate is { } && _lastCurrentFate is null) {
                    _lastCurrentFate = currentFate;
                    return FateState.FateEntered;
                }

                if (currentFate is null && _lastCurrentFate is { }) {
                    _lastCurrentFate = null;
                    return FateState.FateLeft;
                }

                _lastCurrentFate = currentFate;

                // Always engage current fate, even if it doesn't meet conditions anymore (e.g., raised and fate progressed while dead)
                if (currentFate is { })
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
            FateEntered,
            FateLeft,
        }

        private void OnFateEntered() {
            var fate = PublicEvent.CurrentFate;
            if (fate is null)
                return;

            if (tweak._nextFateId.HasValue && fate.Id != tweak._nextFateId.Value)
                return;

            if (Service.BossMod.GetActive() != _presetName) {
                if (Service.BossMod.Get(_presetName) is null)
                    Service.BossMod.Create(_preset, true);
                else
                    Service.BossMod.SetActive(_presetName);
            }

            Svc.BossMod.AddTransientStrategy(_presetName, "BossMod.Autorotation.MiscAI.AutoTarget", "MaxTargets", PullSize.ToString());

            if (PublicEvent.CurrentFate is { Rule: PublicEvent.FateRule.Collect })
                Svc.TextAdvance.EnableExternalControl(Plugin.Name, new() { EnableTalkSkip = true, EnableRequestFill = true, EnableRequestHandin = true });
        }

        private void OnFateLeft() {
            tweak._nextFateId = null;
            if (Service.BossMod.Get(_presetName) is not null)
                Service.BossMod.ClearActive();
            if (Svc.TextAdvance.IsInExternalControl())
                Svc.TextAdvance.DisableExternalControl(Plugin.Name);
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

            bool FateNoLongerValid() => NextFate is null || !FateConditions(NextFate);
            bool ShouldSwitchToNpc() => NextFate?.MotivationNpc is { } && NextFate.State == FFXIVClientStructs.FFXIV.Client.Game.Fate.FateState.Preparing;

            await MoveTo(msh, MovementConfig.Everything.WithTolerance(3),
                stopCondition: () => FateNoLongerValid() || ShouldSwitchToNpc(),
                onStopReached: async () => {
                    if (ShouldSwitchToNpc())
                        await ActivateFate();
                });

            if (NextFate is { State: FFXIVClientStructs.FFXIV.Client.Game.Fate.FateState.Preparing })
                await ActivateFate();
        }

        private async Task ActivateFate() {
            using var scope = BeginScope(nameof(ActivateFate));
            if (NextFate?.MotivationNpc is not { } npc) return;
            await MoveTo(npc.Position, MovementConfig.InteractRange.WithOptions(MovementOptions.GetCurrent()));
            await InteractWith(npc, () => NextFate?.State == FFXIVClientStructs.FFXIV.Client.Game.Fate.FateState.Running, skip: UiSkipOptions.Talk | UiSkipOptions.YesNo);
        }

        private async Task HandleNoFates() {
            if (tweak.Config.SwapZones) {
                using var scope = BeginScope("SwapZones");
                await TeleportTo(GetNextAchievementZone() ?? GetRandomSameExpacZone(), Vector3.Zero);
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
