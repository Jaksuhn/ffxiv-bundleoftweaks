using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;
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

    public unsafe IOrderedEnumerable<PublicEvent> AvailableFates => FateToolKit.ApplySortOrder(PublicEvent.Fates.Where(FateConditions), tweak.Config.SortOrder);
    private bool HasTwistOfFate => Player.Status.Any(status => DateWithDestiny.TwistOfFateStatusIDs.Contains(status.StatusId));

    private bool FateConditions(PublicEvent f)
        => f.Duration <= tweak.Config.MaxDuration
        && f.Progress <= tweak.Config.MaxProgress
        && (f.TimeRemaining < 0 || f.TimeRemaining > tweak.Config.MinTimeRemaining)
        && !tweak.IsBlacklisted(f);

    private unsafe GrindState State {
        get {
            if (Svc.Condition[ConditionFlag.Unconscious]) {
                if (PublicEvent.CurrentFate is { Id: var id, Progress: < 100 })
                    ReturnToFateId = id;
                return GrindState.Unconscious;
            }

            if (PublicEvent.CurrentFate is { } current) {
                // treat completed collect fates as done and wait for out of combat/not busy before trying to move away
                if (current is { Rule: PublicEvent.FateRule.Collect, Progress: >= 100, Id: var id } && !Player.IsBusy)
                    return AvailableFates.FirstOrDefault(f => f.Id != id) is { } ? GrindState.BetweenFates : GrindState.WaitingForFates;
                Status = "Engaging";
                return GrindState.Engaging;
            }

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
        BetweenFates,
        Engaging,
        Unconscious,
    }

    private void HandleIntegrations() {
        if (PublicEvent.CurrentFate is { } fate) {
            // only activate for the fate we're pathfinding to (or any if NextFate is null)
            if (NextFate is { } next && fate.Id != next.Id)
                return;

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
            NextFate = null;
            if (Service.BossMod.Get(_presetName) is not null)
                Service.BossMod.ClearActive();
            if (Svc.TextAdvance.IsInExternalControl())
                Svc.TextAdvance.DisableExternalControl(Name);
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
        PublicEvent? nextFate = null;
        if (ReturnToFateId is { } returnFateId) {
            if (PublicEvent.GetFateById(returnFateId) is { Progress: < 100 } returnFate)
                nextFate = returnFate;
            else
                ReturnToFateId = null;
        }

        if (nextFate is null) {
            // If current is a collect at 100% we're leaving it; pick a different fate
            var candidates = PublicEvent.CurrentFate is { Rule: PublicEvent.FateRule.Collect, Progress: >= 100 } ? AvailableFates.Where(f => f.Id != PublicEvent.CurrentFate.Id) : AvailableFates;
            if (candidates.FirstOrDefault() is not { } candidate) return;
            nextFate = candidate;
        }

        NextFate = nextFate;
        //await WaitWhile(() => Player.IsBusy, "WaitingForNotBusy");
        //await WaitWhile(NearbyPendingMobs, "WaitForEngagedMobsToDisappear");

        // TODO: if rnd=msh, retry?
        var rnd = NextFate.Position.RandomPoint(NextFate.Radius * 0.5f);
        var msh = rnd.OnMesh();
        WarningIf(rnd == msh, "Failed to find a random point on mesh. Destination might not land.");
        Log($"[NextFate={NextFate.Position}] -> [rnd={rnd}] -> [mesh={msh}]");

        var lastProgressPosition = Player.Position;
        var lastProgressAt = Environment.TickCount64;
        var retryPos = Vector3.Zero;
        var retriedOnce = false;
        var teleportToFate = false;
        bool Stuck() {
            if (Svc.Navmesh.PathfindInProgress()) {
                lastProgressPosition = Player.Position;
                lastProgressAt = Environment.TickCount64;
                return false;
            }

            if (Vector3.Distance(Player.Position, lastProgressPosition) > 1.5f) {
                lastProgressPosition = Player.Position;
                lastProgressAt = Environment.TickCount64;
                return false;
            }

            if (Environment.TickCount64 - lastProgressAt < 2000)
                return false;

            if (retriedOnce && Vector3.Distance(Player.Position, retryPos) <= 3f) {
                Warning("Stuck again; teleporting instead");
                teleportToFate = true;
                return true;
            }

            Warning("Stuck on the way to fate. Retrying from current position");
            retryPos = Player.Position;
            retriedOnce = true;
            return true;
        }

        bool FateNoLongerValid() {
            if (NextFate is null)
                return true;
            if (PublicEvent.GetFateById(NextFate.Id) is not { } current)
                return true;

            NextFate = current; // keep nextfate fresh in case an unactivated fate disappears while pathing to it
            return ReturnToFateId == current.Id ? current.Progress >= 100 : !FateConditions(current);
        }

        bool ShouldSwitchToNpc() => NextFate?.MotivationNpc is { IsTargetable: true } && NextFate.State == FateState.Preparing;

        await MoveTo(msh, MovementConfig.Everything.WithTolerance(3),
            allowTeleportIfFaster: NextFate is { Progress: > 0 } && !HasTwistOfFate, // in progress = urgent, otherwise I'd rather just waste a few extra seconds. Also never drop the buff
            stopCondition: () => FateNoLongerValid() || ShouldSwitchToNpc() || Stuck(),
            onStopReached: async () => {
                if (ShouldSwitchToNpc())
                    await ActivateFate();
            });

        if (teleportToFate && NextFate is { Id: var fateId } && PublicEvent.GetFateById(fateId) is { } currentFate) {
            NextFate = currentFate;
            Status = "Teleporting to fate";
            await TeleportTo(Player.Territory.RowId, currentFate.Position, allowSameZoneTeleport: true);
            return;
        }

        if (NextFate is { State: FateState.Preparing } && PublicEvent.Fates.Any(f => f.Id == NextFate.Id))
            await ActivateFate();
    }

    private async Task ActivateFate() {
        using var scope = BeginScope(nameof(ActivateFate));
        if (NextFate?.MotivationNpc is not { IsTargetable: true } npc) return;
        await MoveTo(npc.Position, MovementConfig.InteractRange.WithOptions(MovementOptions.GetCurrent()));
        await InteractWith(npc, () => NextFate?.State == FateState.Running, skip: UiSkipOptions.Talk | UiSkipOptions.YesNo);
    }

    private async Task HandleNoFates() {
        if (!HasTwistOfFate && (tweak.HasSelectedSwapZones || tweak.Config.SwapZones)) {
            using var scope = BeginScope("SwapZones");
            var destination = tweak.GetNextSelectedSwapZone(Player.Territory.RowId) ?? GetNextAchievementZone() ?? GetRandomSameExpacZone();
            if (destination == Player.Territory.RowId) {
                Status = "Waiting for fates in selected zones";
                await Mount();
                await NextFrame(60);
                return;
            }

            await TeleportTo(destination, Vector3.Zero);
        }
        else {
            using var scope = BeginScope("WaitForFates");
            Status = HasTwistOfFate ? "Waiting for fates (preserving Twist of Fate)" : "Waiting for fates to spawn";
            await Mount();
            await NextFrame(60);
        }
    }

    // TODO: don't think this really does anything. Need better vbm support
    private unsafe bool NearbyPendingMobs() {
        return Svc.Objects.OfType<IBattleChara>().Where(o => o.BattleChara()->FateId != 0).Any(o => {
            foreach (var effect in o.BattleChara()->ActionEffectHandler.IncomingEffects) {
                if (effect.GlobalSequence != 0 && effect.Source == Svc.Objects.LocalPlayer?.GameObjectId) {
                    return true;
                }
            }
            return false;
        });
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
