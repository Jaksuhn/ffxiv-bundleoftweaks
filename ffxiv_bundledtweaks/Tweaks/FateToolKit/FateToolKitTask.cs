using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using TerritoryIntendedUse = FFXIVClientStructs.FFXIV.Client.Enums.TerritoryIntendedUse;

namespace ComplexTweaks.Tweaks;

internal sealed class FateGrind(FateToolKit tweak) : TaskBase {
    private const string _presetName = "CBT - DwD";
    private const string _presetCompressed = "G7sgAORUXTtl2E+e+WjPVqrAAflbPZILOMWW4oP+/v+fCkIhzvG84ACJWKy03g9QazmlsQdIscatVWu8Jo51H+IdQI2Y/jafAIgSBxI+iCh8ggeIT7FYvBsibX3oyQkfvp0RQITWOWlrLziwHS0ja2s8qV0VFM9HPOG4IxY+fP6kw98KhNFPI1ad8EGkwVOChw66yKpuovBBJAa3PdXiV+P/U9/QTEWMlgExR9Cf8OIautG+4TKoVL4zGi8uI4qkFB6axuHukAPbAVlXOxtgfEw9XpCtINLWwgfxuNcNLyDisW/8CtaMH5RgnRCYeNqpNjsWcKM8fSbI7mCRUV5IK5dOlU3x2ricRQ7tQ5R9bVl0XBvJx6P+2shwJsusDaZtJaYxsIme1BEBeCFKy5r2uezJsB6IcHQjyomPlVWsEYDlMZDsLi1LtpXZASsvGyVssFIWpVQFS1aGWtKdRYp1rMRqWdxblD76YHnNjbtYCR6fykdLqKzS+xY37ADDlfPFNqKC3F7oYl4DtbgPbFNttNtHdtiqFA8WFeuIOIlVVqge1O1LkemZde8EfSiPF1NRhvynbSTDEp7ReBE6plH48Pl9B+SEPY1B0WdEUk/UVIhCs6O6DPsJTQddZeT5LO04YC/tkQYyoQAsMTnWhnwckdPwBnnfaFMzuct8AvA/n/wD";
    private static readonly string _preset = _presetCompressed.FromBase64();

    private int PullSize => Player.ClassJob.Value switch {
        var cj when cj.IsTank => 0, // unlimited
        var cj when cj.IsDps => 3,
        var cj when cj.IsHealer => 5,
        _ => 1,
    };

    protected override async Task Execute() {
        using var stop = new OnDispose(() => Svc.TextAdvance.DisableExternalControl(Name));
        try {
            while (!CancelToken.IsCancellationRequested && tweak.Running) {
                tweak.StopIfNoRemaining();
                if (!tweak.Running)
                    break;

                var state = State;
                tweak.CurrentState = state.ToString();

                HandleIntegrations();

                switch (state) {
                    case GrindState.Unconscious:
                        await Revive();
                        break;
                    case GrindState.WaitingForFollowUp:
                        await NextFrame(100);
                        break;
                    case GrindState.BetweenFates:
                        await MoveToFate();
                        break;
                    case GrindState.WaitingForFates:
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

    public PublicEvent? NextFate { get; set; }
    private uint? ReturnToFateId { get; set; } // when we die, if the fate we were in progressed enough to not qualify, we want to return to it anyway
    private uint? LastStuckFateId { get; set; }
    private int ConsecutiveStuckRetries { get; set; }
    private uint? FollowUpFateId { get; set; } // id to store to check if NextFate is a follow up to this
    private long FollowUpWatchUntilMs { get; set; }

    public IOrderedEnumerable<PublicEvent> AvailableFates => FateToolKit.ApplySortOrder(PublicEvent.Fates.Where(FateConditions), tweak.Config.SortOrder);
    private bool HasTwistOfFate => Player.Status.Any(status => DateWithDestiny.TwistOfFateStatusIDs.Contains(status.StatusId));

    private bool FateConditions(PublicEvent f)
        => f.Duration <= tweak.Config.MaxDuration
        && f.Progress <= tweak.Config.MaxProgress
        && (f.TimeRemaining < 0 || f.TimeRemaining > tweak.Config.MinTimeRemaining)
        && !tweak.IsBlacklisted(f);

    private GrindState State {
        get {
            if (Svc.Condition[ConditionFlag.Unconscious]) {
                if (PublicEvent.CurrentFate is { Id: var id, Progress: < 100 })
                    ReturnToFateId = id;
                FollowUpFateId = null;
                return GrindState.Unconscious;
            }

            if (PublicEvent.CurrentFate is { } current) {
                if (current.Progress >= 100)
                    StartFollowUpWatch(current.Id);
                else if (FollowUpFateId == current.Id)
                    FollowUpFateId = null;

                // treat completed collect fates as done and wait for out of combat/not busy before trying to move away
                if (current is { Rule: PublicEvent.FateRule.Collect, Progress: >= 100, Id: var id } && !Player.IsBusy)
                    return AvailableFates.FirstOrDefault(f => f.Id != id) is { } ? GrindState.BetweenFates : GrindState.WaitingForFates;
                Status = "Engaging";
                return GrindState.Engaging;
            }

            if (ShouldWaitForFollowUp())
                return GrindState.WaitingForFollowUp;

            if (AvailableFates.FirstOrDefault() is { })
                return GrindState.BetweenFates;

            if (!AvailableFates.Any())
                return GrindState.WaitingForFates;

            return GrindState.Idle;
        }
    }
    private enum GrindState {
        Idle,
        WaitingForFates,
        WaitingForFollowUp,
        BetweenFates,
        Engaging,
        Unconscious,
    }

    private enum MoveStopReason {
        None,
        FateInvalid,
        HigherPriority,
        NpcLoaded,
        StuckRetry,
        StuckTeleport,
    }

    private sealed class MoveTracker(Vector3 initialPosition, long initialTick) {
        private Vector3 LastProgressPosition { get; set; } = initialPosition;
        private long LastProgressAt { get; set; } = initialTick;
        private long LastPathActivityAt { get; set; } = initialTick;
        private Vector3 RetryPosition { get; set; }
        private bool RetriedOnce { get; set; }
        private bool WasRunning { get; set; }

        public MoveStopReason CheckStuck(Vector3 currentPosition) {
            var now = Environment.TickCount64;
            var isRunning = Svc.Navmesh.IsRunning();
            var isPathfinding = Svc.Navmesh.PathfindInProgress();

            if (isRunning || isPathfinding)
                LastPathActivityAt = now;

            if (!isRunning) {
                WasRunning = false;
                LastProgressPosition = currentPosition;
                LastProgressAt = now;

                // if vnav hard fails then it'll go back to being idle while MoveTo is waiting for it
                if (!isPathfinding && now - LastPathActivityAt >= 1500) {
                    if (RetriedOnce && Vector3.Distance(currentPosition, RetryPosition) <= 3f)
                        return MoveStopReason.StuckTeleport;

                    RetryPosition = currentPosition;
                    RetriedOnce = true;
                    return MoveStopReason.StuckRetry;
                }

                return MoveStopReason.None;
            }

            if (!WasRunning) {
                WasRunning = true;
                LastProgressPosition = currentPosition;
                LastProgressAt = now;
                return MoveStopReason.None;
            }

            if (Vector3.Distance(currentPosition, LastProgressPosition) > 1.5f) {
                LastProgressPosition = currentPosition;
                LastProgressAt = now;
                return MoveStopReason.None;
            }

            if (now - LastProgressAt < 2000)
                return MoveStopReason.None;

            if (RetriedOnce && Vector3.Distance(currentPosition, RetryPosition) <= 3f)
                return MoveStopReason.StuckTeleport;

            RetryPosition = currentPosition;
            RetriedOnce = true;
            return MoveStopReason.StuckRetry;
        }
    }

    private async Task Revive() {
        using var scope = BeginScope(nameof(Revive));
        await WaitUntil(() => Player.Revivable, "WaitForRevivable");
        (var lastZone, var lastPos) = (Player.Territory, Player.Position);
        if (Svc.Party.Length is 0) {
            Status = "Reviving";
            GameMain.ExecuteCommand(CommandFlag.Revive.Value, AgentReviveOp.Return.Value);
        }
        else {
            Status = "Waiting For Raise";
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

        IEnumerable<PublicEvent> GetAvailableFates() {
            // If current is a collect at 100% we're leaving it; pick a different fate.
            if (PublicEvent.CurrentFate is { Rule: PublicEvent.FateRule.Collect, Progress: >= 100, Id: var currentId })
                return AvailableFates.Where(f => f.Id != currentId);
            return AvailableFates;
        }

        bool TrySelectNextFate(out PublicEvent selected) {
            if (ReturnToFateId is { } returnFateId) {
                if (PublicEvent.GetFateById(returnFateId) is { Progress: < 100 } returnFate) {
                    selected = returnFate;
                    return true;
                }

                ReturnToFateId = null;
            }

            if (GetAvailableFates().FirstOrDefault() is { } candidate) {
                selected = candidate;
                return true;
            }

            selected = null!;
            return false;
        }

        if (!TrySelectNextFate(out var nextFate))
            return;

        NextFate = nextFate;
        // TODO: if rnd=msh, retry?
        var rnd = NextFate.Position.RandomPoint(NextFate.Radius * 0.5f);
        var msh = rnd.OnMesh();
        WarningIf(rnd == msh, "Failed to find a random point on mesh. Destination might not land.");
        Log($"[NextFate={NextFate.Position}] -> [rnd={rnd}] -> [mesh={msh}]");

        var progress = new MoveTracker(Player.Position, Environment.TickCount64);
        var stopReason = MoveStopReason.None;

        bool IsCurrentFateInvalid() {
            if (NextFate is null)
                return true;
            if (PublicEvent.GetFateById(NextFate.Id) is not { } current)
                return true;

            NextFate = current; // keep nextfate fresh in case an unactivated fate disappears while pathing to it
            return ReturnToFateId == current.Id ? current.Progress >= 100 : !FateConditions(current);
        }

        bool TrySwitchToHigherPriorityFate() {
            // don't check if we're returning to a previous fate
            if (ReturnToFateId is not null || NextFate is null)
                return false;

            if (GetAvailableFates().FirstOrDefault() is not { } higherPrio || higherPrio.Id == NextFate.Id)
                return false;

            Log($"Switching target fate {NextFate.Id} -> {higherPrio.Id} (higher priority)");
            NextFate = higherPrio;
            return true;
        }

        bool ShouldSwitchToNpc() => NextFate is { State: FateState.Preparing } fate && TryGetValidMotivationNpc(fate, out _);

        bool ShouldStopMove() {
            // preserve the first reason so it can't be overwritten by a later check.
            if (stopReason != MoveStopReason.None)
                return true;

            stopReason = MoveStopReason.None;

            if (IsCurrentFateInvalid()) {
                stopReason = MoveStopReason.FateInvalid;
                return true;
            }

            if (TrySwitchToHigherPriorityFate()) {
                stopReason = MoveStopReason.HigherPriority;
                return true;
            }

            if (ShouldSwitchToNpc()) {
                stopReason = MoveStopReason.NpcLoaded;
                return true;
            }

            if (progress.CheckStuck(Player.Position) is not MoveStopReason.None and var reason) {
                if (reason == MoveStopReason.StuckTeleport)
                    Warning("Stuck again; teleporting instead");
                else
                    Warning("Stuck on the way to fate. Retrying from current position");

                stopReason = reason;
                return true;
            }

            return false;
        }

        await MoveTo(msh, MovementConfig.Everything.WithTolerance(3),
            allowTeleportIfFaster: NextFate is { Progress: > 0 } && !HasTwistOfFate, // in progress = urgent, otherwise I'd rather just waste a few extra seconds. Also never drop the buff
            stopCondition: ShouldStopMove,
            onStopReached: async () => {
                if (stopReason == MoveStopReason.NpcLoaded)
                    await ActivateFate();
            });

        Log($"{nameof(MoveToFate)} finished with stopReason={stopReason} fate={NextFate?.Id}");

        if (stopReason == MoveStopReason.StuckRetry && NextFate is { Id: var stuckFateId }) {
            if (LastStuckFateId == stuckFateId)
                ConsecutiveStuckRetries++;
            else {
                LastStuckFateId = stuckFateId;
                ConsecutiveStuckRetries = 1;
            }

            if (ConsecutiveStuckRetries >= 2) {
                Warning($"Escalating repeated stuck retries to teleport for fate {stuckFateId}");
                stopReason = MoveStopReason.StuckTeleport;
            }
        }
        else if (stopReason != MoveStopReason.StuckTeleport) {
            LastStuckFateId = null;
            ConsecutiveStuckRetries = 0;
        }

        if (stopReason == MoveStopReason.HigherPriority)
            return;

        if (stopReason == MoveStopReason.StuckTeleport && NextFate is { Id: var fateId } && PublicEvent.GetFateById(fateId) is { } currentFate) {
            NextFate = currentFate;
            LastStuckFateId = null;
            ConsecutiveStuckRetries = 0;
            Status = "Teleporting to fate";
            await Mount();
            await TeleportTo(Player.Territory.RowId, currentFate.Position, allowSameZoneTeleport: true);
            return;
        }

        // only activate after a normal arrival; if we explicitly stopped (e.g. npcloaded), let the loop re-handle
        if (stopReason == MoveStopReason.None && NextFate is { State: FateState.Preparing } && PublicEvent.Fates.Any(f => f.Id == NextFate.Id))
            await ActivateFate();
    }

    private async Task ActivateFate() {
        using var scope = BeginScope(nameof(ActivateFate));
        if (NextFate is not { } fate)
            return;

        // sometimes fates are in prep for a very long time before they're on the map. Wait until the npc is actually ready before returning/attempting anything
        await WaitUntil(() => TryGetValidMotivationNpc(fate, out _), "");

        if (TryGetValidMotivationNpc(fate, out var npc)) {
            Log($"ActivateFate start: fate={NextFate.Id} npc={npc.EntityId} npcPos={npc.Position} playerPos={Player.Position} dist={Player.DistanceTo(npc.Position):F2} inRange={npc.IsInInteractRange()}");
            await MoveTo(npc.Position, MovementConfig.InteractRange.WithOptions(MovementOptions.GetCurrent()));
            Log($"ActivateFate after MoveTo: npc={npc.EntityId} playerPos={Player.Position} dist={Player.DistanceTo(npc.Position):F2} inRange={npc.IsInInteractRange()}");
            try {
                await InteractWith(npc, () => NextFate?.State == FateState.Running, skip: UiSkipOptions.Talk | UiSkipOptions.YesNo);
            }
            catch (Exception ex) {
                // will crash if we don't catch and it's fine if interact fails because the npc/fate disappeared before we could start
                if (NextFate is null || !TryGetValidMotivationNpc(NextFate, out _) || NextFate.State != FateState.Preparing) {
                    Warning($"Skipping fate activation: npc/fate vanished before interact ({ex.Message})");
                    return;
                }
                throw;
            }
        }
        else
            Error($"Something weird happened with the activation npc [{fate}]");
    }

    private async Task HandleNoFates() {
        var hasEffectiveZones = tweak.GetEffectiveSwapZones() is { Count: > 0 } || tweak.HasSelectedSwapZones;
        if (!HasTwistOfFate && (hasEffectiveZones || tweak.Config.SwapZones)) {
            using var scope = BeginScope("SwapZones");
            var destination = tweak.GetNextPreferredSwapZone(Player.Territory.RowId) ?? GetNextAchievementZone() ?? GetRandomSameExpacZone();
            if (destination == Player.Territory.RowId) {
                Status = "Waiting for fates in selected zones";
                await Mount();
                await NextFrame(60);
                return;
            }

            var fromTerritoryId = Player.Territory.RowId;
            await Mount();
            await TeleportTo(destination, Vector3.Zero);
            await tweak.GetCurrentMode().OnSwapZone(fromTerritoryId, destination, CancelToken);
        }
        else {
            using var scope = BeginScope("WaitForFates");
            Status = HasTwistOfFate ? "Waiting for fates (preserving Twist of Fate)" : "Waiting for fates to spawn";
            await Mount();
            await NextFrame(60);
        }
    }

    private void HandleIntegrations() {
        if (PublicEvent.CurrentFate is { } fate) {
            // when we leave collect fates early, it's still CurrentFate, so we need to ignore that and deactivate anyway
            if (fate is { Rule: PublicEvent.FateRule.Collect, Progress: >= 100 } && (NextFate is null || NextFate.Id != fate.Id)) {
                DeactivateIntegrations(clearNextFate: false);
                return;
            }

            // only activate for the fate we're pathfinding to (or any if NextFate is null)
            if (NextFate is { } next && fate.Id != next.Id) {
                DeactivateIntegrations(clearNextFate: false);
                return;
            }

            if (Service.BossMod.GetActive() != _presetName) {
                if (Service.BossMod.Get(_presetName) is null)
                    Service.BossMod.Create(_preset, true);
                else
                    Service.BossMod.SetActive(_presetName);
            }
            Svc.BossMod.AddTransientStrategy(_presetName, "BossMod.Autorotation.MiscAI.AutoTarget", "MaxTargets", PullSize.ToString());

            if (PublicEvent.CurrentFate is { Rule: PublicEvent.FateRule.Collect } && !Svc.TextAdvance.IsInExternalControl())
                Svc.TextAdvance.EnableExternalControl(Name, new() { EnableTalkSkip = true, EnableRequestFill = true, EnableRequestHandin = true });
        }
        else {
            // Fate ended; clear NextFate so routing is correct. Only turn off combat preset once out of combat,
            // so we don't get stuck if a non-fate mob is still aggroed when the fate completes.
            NextFate = null;
            if (!Svc.Condition[ConditionFlag.InCombat])
                DeactivateIntegrations(clearNextFate: false);
        }
    }

    private void DeactivateIntegrations(bool clearNextFate) {
        if (clearNextFate)
            NextFate = null;

        Service.BossMod.ClearActive();
        Svc.Targets.Target = null; // avoid preset trying to go to the mob and interfering with casts
        if (Svc.TextAdvance.IsInExternalControl())
            Svc.TextAdvance.DisableExternalControl(Name);
    }

    private bool TryGetValidMotivationNpc(PublicEvent fate, [NotNullWhen(true)] out IGameObject? npc) {
        npc = null;
        if (Player.DistanceTo(fate.Position) > 50) // half the object table range
            return false;

        if (fate.MotivationNpc is not { IsTargetable: true } target)
            return false;

        // TODO: see if this is still needed after objectkind change
        if (target.Position == Player.Position) {
            Warning($"[{fate.Id}] npc {target} [{target.Position}] has same position as player");
            return false;
        }

        if (Vector3.Distance(target.Position, fate.Position) > Math.Max(fate.Radius + 20f, 40f)) {
            Warning($"[{fate.Id}] npc {target} [{target.Position}] way outside the fate [{fate.Position}]");
            return false;
        }

        npc = target;
        return true;
    }

    // TODO: find better shit for this
    private const int FollowUpWaitLimit = 15_000;
    private void StartFollowUpWatch(uint completedFateId) {
        if (!Fate.GetRow(completedFateId).HasFollowUp)
            return;

        if (FollowUpFateId != completedFateId)
            Log($"Watching for follow-up fate after {completedFateId} for {FollowUpWaitLimit / 1000}s");

        FollowUpFateId = completedFateId;
        FollowUpWatchUntilMs = Environment.TickCount64 + FollowUpWaitLimit;
    }

    private bool ShouldWaitForFollowUp() {
        if (FollowUpFateId is not { } fateId)
            return false;

        var row = Fate.GetRow(fateId);
        if (PublicEvent.Fates.Any(f => f.Id > fateId && Fate.GetRow(f.Id).Location == row.Location)) {
            Log($"Detected follow-up fate for {fateId}, resuming routing");
            FollowUpFateId = null;
            return false;
        }

        if (Environment.TickCount64 >= FollowUpWatchUntilMs) {
            FollowUpFateId = null;
            return false;
        }

        Status = $"Waiting for follow-up fate ({(FollowUpWatchUntilMs - Environment.TickCount64) / 1000 + 1}s)";
        return true;
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
        var rows = TerritoryType.Where(x => x.IsInUse && x.TerritoryIntendedUse.Value.StructsEnum is TerritoryIntendedUse.Overworld && x.ExVersion.RowId == Player.Territory.Value.ExVersion.RowId && !x.IsPvpZone);
        return rows[new Random().Next(rows.Length)].RowId;
    }
}
