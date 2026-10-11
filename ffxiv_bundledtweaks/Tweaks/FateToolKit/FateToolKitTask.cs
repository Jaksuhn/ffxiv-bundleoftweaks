using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;
using Lumina.Extensions;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using TerritoryIntendedUse = FFXIVClientStructs.FFXIV.Client.Enums.TerritoryIntendedUse;

namespace ComplexTweaks.Tweaks;

internal sealed class FateGrind(FateToolKit tweak) : TaskBase {
    private const string _presetName = "CBT - DwD";
    private const string _presetCompressed = "G4YhAORUXTtl2E+e+WjPVqrAATn5Vo7kAFPsV3yB7j9/70DoI3XCfiCJZdG691lWNtaisUHCTIelGa+JY91FvfGTokqT38YDAFHigYQPIgpreIL4FIvJuwHStv2enPDh2ykBRGidk7b1gp5tZxlZW+NJ7ZqgeD6kxm5NLHz4/EmHvw0QhtcdNjvhg0iDOsGDgy6aqhsrfBCJweWeWvGr+fGpa2gWRYyWALFEsD/hxU24MX3DJbBUvzMaLy4jRnIK+9XK4W6Tnu0BWTc7GWB6THu8IFtBpG2FD6La6BVPIOJuP3kUzIUfkGCeEAyV9lWbtQTsmMdnTLkLi4zyTls59VXZFK/C5SxyaB+i7OtM4nFtDB+N/bUx4UTJrA2mbaWnZWAR1fYIA16M0rLmvpQ9GdYHIg43XEV8rMxih0BYqkDKXVuWbCu/gwy8LJS2YcCbmddO80abdWXwX6BhCs061ASYTLMvK2VRdsKSlaF9e2eRel1WYjXt7gXKYQZgpv3yYHnN/c+yElS13B8toQqb71sixb0MZ3b6NtI2zfZCF/MaKBYWd8821Ua7TWQPS9v5waJiF1FOYpV1xhTv1qXI9Mx67xQglupiGgqQ4K1wXcIzGi9Cx9QJHz71SBvkhHvqguKPgkjaE02IfFICPiE1mt337obevPVU3vksbXfAvbRHOpDJ0GAVk2NteKVQU80CebPSJprOZjwAeIwHDw==";
    private static readonly string _preset = _presetCompressed.FromBase64();

    private int PullSize => IPlayerState.Get().ClassJob.Value switch {
        var cj when cj.IsTank => 0, // unlimited
        var cj when cj.IsDps => 3,
        var cj when cj.IsHealer => 5,
        _ => 1,
    };

    private static IPlayerCharacter? Player => IObjectTable.Get().LocalPlayer;

    protected override async Task Execute() {
        using var stop = new OnDispose(() => TextAdvanceIpc.Get().DisableExternalControl(Name));
        if (BossModIPC.Get().Get(_presetName) is not null) // one time overwrite in case I update the preset
            BossModIPC.Get().Create(_preset, true);
        try {
            while (!CancelToken.IsCancellationRequested && tweak.Running) {
                tweak.StopIfNoRemaining();
                if (tweak.PendingStopWhenSafe && PublicEvent.CurrentFate is null && !ICondition.Get()[ConditionFlag.InCombat]) {
                    tweak.PendingStopWhenSafe = false;
                    tweak.Running = false;
                }
                if (!tweak.Running)
                    break;

                var state = State;
                tweak.CurrentState = state.ToString();

                HandleIntegrations();

                switch (state) {
                    case GrindState.Unconscious:
                        await Revive();
                        break;
                    case GrindState.Engaging:
                        if (Player.Mounted) // if the destination was in the ground and we got 'stuck' before it but in the fate, you'd be left mounted
                            await Dismount();
                        // this should only ever happen when hot reloading vbm during a fate
                        if (PublicEvent.CurrentFate is { IsOnMap: true } current && !BossModIPC.Get().HasTempMap())
                            await GenerateObstacleMap(current);
                        await NextFrame();
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
                    case GrindState.SwapZones:
                        await SwapNewItemTarget();
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
    private readonly HashSet<uint> _skippedFateIds = []; // deferred until another fate completes, or all skips are exhausted
    private uint? _lastEngagedFateId;
    private uint? FollowUpFateId { get; set; } // id to store to check if NextFate is a follow up to this
    private long FollowUpWatchUntilMs { get; set; }
    private uint? WaitForExpiryFateId { get; set; } // id for when we leave a collect fate. Stay in zone until fate is null

    public IOrderedEnumerable<PublicEvent> AvailableFates => FateToolKit.ApplySortOrder(PublicEvent.Fates.Where(tweak.FateConditions), tweak.Config.SortOrder);
    private IEnumerable<PublicEvent> SelectableFates {
        get {
            var fates = AvailableFates.Where(f => !_skippedFateIds.Contains(f.Id));
            if (PublicEvent.CurrentFate is { Rule: PublicEvent.FateRule.Collect, Progress: >= 100, Id: var currentId })
                fates = fates.Where(f => f.Id != currentId);
            return fates;
        }
    }
    private bool HasSkippedFatesOnly => AvailableFates.Any() && !SelectableFates.Any();
    private bool HasTwistOfFate => IObjectTable.Get().LocalPlayer?.StatusList.HasTwistOfFate() ?? false;

    private GrindState State {
        get {
            if (!IObjectTable.Get().LocalPlayer.Available) return GrindState.SwapZones;

            if (WaitForExpiryFateId is { } waitId && PublicEvent.GetFateById(waitId) is null)
                WaitForExpiryFateId = null;

            if (ICondition.Get()[ConditionFlag.Unconscious]) {
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
                if (current is { Rule: PublicEvent.FateRule.Collect, Progress: >= 100, Id: var id } && !IObjectTable.Get().LocalPlayer.IsBusy) {
                    WaitForExpiryFateId = id;
                    return SelectableFates.FirstOrDefault() is { } ? GrindState.BetweenFates : GrindState.WaitingForFates;
                }
                Status = "Engaging";
                return GrindState.Engaging;
            }

            if (ShouldWaitForFollowUp())
                return GrindState.WaitingForFollowUp;

            if (!HasTwistOfFate && !ICondition.Get()[ConditionFlag.InCombat] && tweak.IsZoneItemTargetComplete(IPlayerState.Get().Territory.RowId, out _))
                return GrindState.SwapZones;

            if (SelectableFates.FirstOrDefault() is { })
                return GrindState.BetweenFates;

            return GrindState.WaitingForFates;
        }
    }
    private enum GrindState {
        Idle,
        WaitingForFates,
        WaitingForFollowUp,
        BetweenFates,
        SwapZones,
        Engaging,
        Unconscious,
    }

    private enum MoveStopReason {
        None,
        FateInvalid,
        FatePending,
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
            var isRunning = NavmeshIPC.Get().IsRunning();
            var isPathfinding = NavmeshIPC.Get().PathfindInProgress;

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
        if (Player is null) return;

        await WaitUntil(() => Player.IsRevivable, "WaitForRevivable");
        (var lastZone, var lastPos) = (IPlayerState.Get().Territory, Player.Position);
        if (IPartyList.Get().Length is 0) {
            Status = "Reviving";
            GameMain.ExecuteCommand(CommandFlag.Revive.Value, AgentReviveOp.Return.Value);
        }
        else {
            Status = "Waiting For Raise";
            await WaitUntil(() => Player.ReviveState is ReviveState.Revivable, "WaitingForRaise"); // 1 = return, 2 = raise
            GameMain.ExecuteCommand(CommandFlag.Revive.Value, AgentReviveOp.AcceptRevive.Value); // a1=5 for raises
        }
        await WaitWhile(() => ICondition.Get()[ConditionFlag.Unconscious], "WaitForAlive");

        // if the zone we were in was an instanced zone, we might end up in a different one when tp'ing back
        // if the way back involves taking a city route, we don't be near an aetheryte to swap instances
        // TODO: figure out instance swapping, and bypass city routes and go directly back to zone
        if (Player.Territory.RowId != lastZone.RowId) {
            await TeleportTo(lastZone.RowId, lastPos);
            await UseAethernet(lastZone.RowId, lastPos);
        }
    }

    private async Task MoveToFate() {
        using var scope = BeginScope(nameof(MoveToFate));
        if (Player is null) return;

        IEnumerable<PublicEvent> GetAvailableFates() => SelectableFates;

        void SkipFate(uint fateId, string reason) {
            Warning($"Skipping fate {fateId}: {reason}");
            _skippedFateIds.Add(fateId);
            if (ReturnToFateId == fateId)
                ReturnToFateId = null;
            NextFate = null;
        }

        bool TrySelectNextFate(out PublicEvent selected) {
            if (ReturnToFateId is { } returnFateId) {
                if (PublicEvent.GetFateById(returnFateId) is { Progress: < 100 } returnFate) {
                    selected = returnFate;
                    return true;
                }

                ReturnToFateId = null;
            }

            if (FollowUpFateId is { } parentId && Environment.TickCount64 < FollowUpWatchUntilMs) {
                var parent = Fate.GetRow(parentId);
                // allow even if pending
                if (GetAvailableFates().Where(f => f.Id > parentId && Fate.GetRow(f.Id).Location == parent.Location).OrderBy(f => Player!.DistanceTo(f.Position)).FirstOrDefault() is { } followUp) {
                    selected = followUp;
                    return true;
                }
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
        if (!NextFate.IsOnMap) {
            Status = "Waiting for fate to appear";
            await Mount();
            await NextFrame(30);
            return;
        }

        // TODO: if rnd=msh, retry?
        var rnd = NextFate.Position.RandomPoint(NextFate.Radius * 0.5f);
        var msh = rnd.OnMesh();
        WarningIf(rnd == msh, "Failed to find a random point on mesh. Destination might not land.");
        Log($"[NextFate={Player.Territory.RowId}-{NextFate.Position}] -> [rnd={rnd}] -> [mesh={msh}]");

        var progress = new MoveTracker(Player.Position, Environment.TickCount64);
        var stopReason = MoveStopReason.None;

        bool IsCurrentFateInvalid() {
            if (NextFate is null)
                return true;
            if (PublicEvent.GetFateById(NextFate.Id) is not { } current)
                return true;

            NextFate = current; // keep nextfate fresh in case an unactivated fate disappears while pathing to it
            if (!current.IsOnMap)
                return false;

            return ReturnToFateId == current.Id ? current.Progress >= 100 : !tweak.FateConditions(current);
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

            if (NextFate is { IsOnMap: false }) {
                stopReason = MoveStopReason.FatePending;
                return true;
            }

            if (ShouldSwitchToNpc()) {
                stopReason = MoveStopReason.NpcLoaded;
                return true;
            }

            if (Player is { Position: var pos } && progress.CheckStuck(pos) is not MoveStopReason.None and var reason) {
                if (reason == MoveStopReason.StuckTeleport)
                    Warning("Stuck again; teleporting instead");
                else
                    Warning("Stuck on the way to fate. Retrying from current position");

                stopReason = reason;
                return true;
            }

            return false;
        }

        await GenerateObstacleMap(nextFate);
        const float moveTolerance = 3f;
        if (!await TryMoveTo(msh, MovementConfig.Everything.WithTolerance(moveTolerance),
            // in progress = urgent, otherwise I don't think teleporting all the time is necessary
            // also prohibit when you have the xp buff or when waiting for collect fate rewards
            allowTeleportIfFaster: NextFate is { Progress: > 0 } && !HasTwistOfFate && WaitForExpiryFateId is null,
            stopCondition: ShouldStopMove,
            onStopReached: async () => {
                if (stopReason == MoveStopReason.NpcLoaded)
                    await ActivateFate();
            })) {
            SkipFate(nextFate.Id, "failed to start pathfinding");
            return;
        }

        Log($"{nameof(MoveToFate)} finished with stopReason={stopReason} fate={NextFate?.Id}");

        // destination can be off-mesh / underground while we're already inside the fate circle
        bool IsAlreadyAtFate(PublicEvent fate)
            => PublicEvent.CurrentFate?.Id == fate.Id || Player.FlatDistanceTo(fate.Position) <= fate.Radius;

        async Task JoinFate() {
            if (NextFate is { State: FateState.Preparing, MotivationNpcId: not 0xE0000000 } && PublicEvent.Fates.Any(f => f.Id == NextFate.Id))
                await ActivateFate();
            else if (Player.Mounted)
                await Dismount();
        }

        if (stopReason == MoveStopReason.StuckRetry && NextFate is { Id: var stuckFateId }) {
            if (IsAlreadyAtFate(NextFate)) {
                Log($"Stuck near fate {stuckFateId} but already inside its area; treating as arrived");
                await JoinFate();
                return;
            }

            SkipFate(stuckFateId, "stuck while pathfinding");
            return;
        }

        if (stopReason == MoveStopReason.HigherPriority)
            return;

        if (stopReason == MoveStopReason.FatePending) {
            Status = "Waiting for fate to appear";
            await Mount();
            await NextFrame(30);
            return;
        }

        if (stopReason == MoveStopReason.StuckTeleport && WaitForExpiryFateId is null && NextFate is { Id: var fateId }) {
            if (IsAlreadyAtFate(NextFate)) {
                Log($"Stuck teleport triggered for fate {fateId} but already inside its area; treating as arrived");
                await JoinFate();
                return;
            }

            if (PublicEvent.GetFateById(fateId) is { } currentFate) {
                NextFate = currentFate;
                Status = "Teleporting to fate";
                var fateTerritoryId = Player.Territory.RowId;
                await TeleportTo(fateTerritoryId, currentFate.Position, allowSameZoneTeleport: true);
                await UseAethernet(fateTerritoryId, currentFate.Position);
            }
            return;
        }

        if (stopReason == MoveStopReason.None && NextFate is { Id: var unreachedFateId } && !Player.WithinRange(msh, moveTolerance)) {
            if (IsAlreadyAtFate(NextFate)) {
                Log($"Pathfinding stopped short of mesh point for fate {unreachedFateId} but already inside its area; treating as arrived");
                await JoinFate();
                return;
            }

            SkipFate(unreachedFateId, "pathfinding stopped before reaching destination");
            return;
        }

        // only activate after a normal arrival; if we explicitly stopped (e.g. npcloaded), let the loop re-handle
        if (stopReason == MoveStopReason.None && NextFate is { State: FateState.Preparing, MotivationNpcId: not 0xE0000000 } && PublicEvent.Fates.Any(f => f.Id == NextFate.Id))
            await ActivateFate();
    }

    // some are just so bad it's not worth it having them. I don't really have a better solution than this.
    private readonly List<uint> _obstacleMapBlacklist = [1831, 1832, 1914, 1915];
    private async Task GenerateObstacleMap(PublicEvent evt) {
        if (_obstacleMapBlacklist.Contains(evt.Id)) {
            return;
        }

        using var scope = BeginScope(nameof(GenerateObstacleMap));

        // bitmap is built via vnav and doesn't await the mesh still being built
        if (!NavmeshIPC.Get().IsReady) {
            Status = "Waiting for Navmesh";
            await WaitUntil(() => NavmeshIPC.Get().IsReady || NavmeshIPC.Get().BuildProgress >= 0, "WaitForBuildStart");
            if (NavmeshIPC.Get().BuildProgress >= 0)
                await WaitWhile(() => NavmeshIPC.Get().BuildProgress >= 0, "BuildMesh");
            if (!NavmeshIPC.Get().IsReady) {
                Warning($"Navmesh not ready; skipping obstacle map for fate {evt.Id}");
                return;
            }
        }

        if (PublicEvent.GetFateById(evt.Id) is not { Position: var position and not { X: 0, Y: 0, Z: 0 } } current) {
            Warning($"Fate {evt.Id} no longer available; skipping obstacle map");
            return;
        }
        evt = current;

        // sometimes the center of a fate is unreachable (tower fate in amh araeng), so generate from a reachable point then compensate for being off center
        var safe = NavmeshIPC.Get().NearestPointReachable(position, 5, 5);
        float? margin = safe is { } ? Vector3.Distance(position, safe.Value) : null;
        try {
            if (!BossModIPC.Get().Generate(safe ?? position, evt.Radius + margin ?? 10, false)) {
                Warning($"Obstacle map generation failed to start for fate {evt.Id}");
                _obstacleMapBlacklist.Add(evt.Id);
                return;
            }
        }
        catch (Exception ex) {
            // shouldn't happen since we wait for the mesh above but just in case
            Warning($"Obstacle map generation failed to start for fate {evt.Id}: {ex.Message}");
            _obstacleMapBlacklist.Add(evt.Id);
            return;
        }

        var generationFailed = false;
        await WaitUntil(() => {
            var status = BossModIPC.Get().GetGenerationStatus();
            if (status is TaskStatus.RanToCompletion) {
                Log($"Obstacle map generated for fate {evt.Id}");
                return true;
            }
            if (status is TaskStatus.Faulted) {
                Warning($"Obstacle map generation failed for fate {evt.Id}");
                generationFailed = true;
                return true; // allow moving without the map rather than getting stuck in an infinite wait
            }
            return false;
        }, "WaitForObstacleMap");

        if (generationFailed) {
            _obstacleMapBlacklist.Add(evt.Id);
            return;
        }

        if (BossModIPC.Get().EvaluateTempMapQuality() is { } quality) {
            Log($"Generated obstacle map quality for fate {evt.Id}: {quality}");
            if (quality.IsBad) {
                Log($"Obstacle map quality too poor. Clearing obstacle map. BossMod won't navigate in case of obstacles. Consider blacklisting this fate if it's problematic.");
                _obstacleMapBlacklist.Add(evt.Id);
                BossModIPC.Get().ClearTempMap();
            }
        }
    }

    private async Task ActivateFate() {
        using var scope = BeginScope(nameof(ActivateFate));
        if (Player is null) return;

        if (NextFate is not { } fate)
            return;
        if (!fate.IsOnMap)
            return;

        // sometimes fates are in prep for a very long time before they're on the map. Wait until the npc is actually ready before returning/attempting anything
        await WaitUntil(() => TryGetValidMotivationNpc(fate, out _) || fate.State is FateState.Running, "WaitForNpcSpawn");

        if (fate.State is FateState.Running) return; // someone beat us to activating

        if (TryGetValidMotivationNpc(fate, out var npc)) {
            Log($"ActivateFate start: fate={NextFate.Id} npc={npc.EntityId} npcPos={npc.Position} playerPos={Player.Position} dist={Player.DistanceTo(npc.Position):F2} inRange={npc.IsInInteractRange()}");
            await MoveTo(npc.Position, MovementConfig.InteractRange.WithOptions(MovementOptions.Current));
            Log($"ActivateFate after MoveTo: npc={npc.EntityId} playerPos={Player.Position} dist={Player.DistanceTo(npc.Position):F2} inRange={npc.IsInInteractRange()}");
            try {
                await InteractWith(npc, () => NextFate?.State == FateState.Running || !TryGetValidMotivationNpc(fate, out _), skip: UiSkipOptions.Talk | UiSkipOptions.YesNo);
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
            Error($"Something weird happened with the npc activation [{fate}]");
    }

    private async Task HandleNoFates() {
        if (WaitForExpiryFateId is not null) {
            using var scope = BeginScope("WaitForFateRewards");
            Status = "Waiting for fate rewards";
            await Mount();
            await NextFrame(60);
            return;
        }

        var hasEffectiveZones = tweak.GetEffectiveSwapZones() is { Count: > 0 } || tweak.HasSelectedSwapZones;
        if (!HasTwistOfFate && (hasEffectiveZones || tweak.Config.SwapZones)) {
            using var scope = BeginScope("SwapZones");
            var destination = tweak.GetNextPreferredSwapZone(Player.Territory.RowId) ?? GetNextAchievementZone() ?? GetRandomSameExpacZone();
            if (destination == Player.Territory.RowId) {
                Status = HasSkippedFatesOnly ? "Waiting for another fate (current target unreachable)" : "Waiting for fates in selected zones";
                await Mount();
                await NextFrame(60);
                return;
            }

            var fromTerritoryId = Player.Territory.RowId;
            await Mount();
            await TeleportTo(destination, Vector3.Zero);
            await ApplySwapZoneActions(fromTerritoryId, destination);
        }
        else {
            using var scope = BeginScope("WaitForFates");
            Status = HasSkippedFatesOnly ? "Waiting for another fate (current target unreachable)" : HasTwistOfFate ? "Waiting for fates (preserving Twist of Fate)" : "Waiting for fates to spawn";
            await Mount();
            await NextFrame(60);
        }
    }

    private async Task SwapNewItemTarget() {
        if (!tweak.IsZoneItemTargetComplete(Player.Territory.RowId, out var destination))
            return;
        using var scope = BeginScope(nameof(SwapNewItemTarget));
        var fromTerritoryId = Player.Territory.RowId;
        if (destination != fromTerritoryId) {
            Status = "ZoneItemTarget complete. Swapping zones.";
            await Mount();
            await TeleportTo(destination, Vector3.Zero);
        }
        else
            Status = "ZoneItemTarget complete. Switching target.";
        await ApplySwapZoneActions(fromTerritoryId, destination);
    }

    private async Task ApplySwapZoneActions(uint fromTerritoryId, uint toTerritoryId) {
        if (tweak.GetCurrentMode().GetSwapZoneActions(fromTerritoryId, toTerritoryId) is not { } actions)
            return;

        using var scope = BeginScope(nameof(ApplySwapZoneActions));

        if (actions.EquipItemId is { } itemId) {
            var item = new ItemHandle(itemId);
            await WaitWhileBusy();
            Log("Equipping the watch");
            await TryUntil(item.Equip, () => item.IsEquipped, "EquipItem", timeoutSeconds: 5);
        }

        if (actions.TargetCompanion is not { IsValid: true, RowId: var id })
            return;

        if (ICondition.Get()[ConditionFlag.Mounted])
            await Dismount();

        await WaitWhileBusy();
        Log($"Summoning {actions.TargetCompanion.Value.Singular}");
        await TryUntil(() => { unsafe { ActionManager.Instance()->UseAction(ActionType.Companion, id); } }, () => IPlayerState.Get().Minion.RowId == id, "SummonMinion", timeoutSeconds: 5);
    }

    private void HandleIntegrations() {
        if (PublicEvent.CurrentFate is { } fate) {
            _lastEngagedFateId = fate.Id;
            // when we leave collect fates early, it's still CurrentFate, so we need to ignore that and deactivate anyway
            if (fate is { Rule: PublicEvent.FateRule.Collect, Progress: >= 100 } && (NextFate is null || NextFate.Id != fate.Id)) {
                // don't deactivate before we're out of combat
                if (ICondition.Get()[ConditionFlag.InCombat])
                    return;
                DeactivateIntegrations(clearNextFate: false);
                return;
            }

            // only activate for the fate we're pathfinding to (or any if NextFate is null)
            if (NextFate is { } next && fate.Id != next.Id
                && !(fate is { Rule: PublicEvent.FateRule.Collect, Progress: >= 100 } && ICondition.Get()[ConditionFlag.InCombat])) {
                DeactivateIntegrations(clearNextFate: false);
                return;
            }

            if (Player.Mounted) {
                DeactivateIntegrations(clearNextFate: false);
                return;
            }

            if (BossModIPC.Get().GetActive() != _presetName) {
                if (BossModIPC.Get().Get(_presetName) is null)
                    BossModIPC.Get().Create(_preset, true);
                else
                    BossModIPC.Get().SetActive(_presetName);
            }
            BossModIPC.Get().AddTransientStrategy(_presetName, "BossMod.Autorotation.MiscAI.AutoTarget", "MaxTargets", PullSize.ToString());

            if (PublicEvent.CurrentFate is { Rule: PublicEvent.FateRule.Collect } && !TextAdvanceIpc.Get().IsInExternalControl())
                TextAdvanceIpc.Get().EnableExternalControl(Name, new() { EnableTalkSkip = true, EnableRequestFill = true, EnableRequestHandin = true });
        }
        else {
            if (_lastEngagedFateId is { }) {
                // allow skipped fates to retry
                _skippedFateIds.Clear();
                _lastEngagedFateId = null;
            }
            // Fate ended; clear NextFate so routing is correct. Only turn off combat preset once out of combat,
            // so we don't get stuck if a non-fate mob is still aggroed when the fate completes.
            NextFate = null;
            if (!ICondition.Get()[ConditionFlag.InCombat])
                DeactivateIntegrations(clearNextFate: false);
        }
    }

    private void DeactivateIntegrations(bool clearNextFate) {
        if (clearNextFate)
            NextFate = null;

        BossModIPC.Get().ClearActive();
        ITargetManager.Get().Target = null; // avoid preset trying to go to the mob and interfering with casts
        if (TextAdvanceIpc.Get().IsInExternalControl())
            TextAdvanceIpc.Get().DisableExternalControl(Name);
    }

    private bool TryGetValidMotivationNpc(PublicEvent fate, [NotNullWhen(true)] out IGameObject? npc) {
        npc = null;
        if (Player?.DistanceTo(fate.Position) > 50) // half the object table range
            return false;

        if (fate.MotivationNpc is not { IsTargetable: true } target)
            return false;

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
