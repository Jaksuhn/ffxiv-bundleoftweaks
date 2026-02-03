using ComplexTweaks.Tweaks;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.Automation;
using Lumina.Excel.Sheets;
using System.Threading.Tasks;

namespace ComplexTweaks.Tasks;

public sealed class KillFlag(string world) : TaskBase {
    private const float HUNT_DETECTION_RADIUS = 15.0f;
    private const float LOS_SEARCH_RADIUS = 5.0f;
    private const int LOS_SEARCH_POSITIONS = 8;
    private const float TARGET_APPROACH_DISTANCE = 3.0f;

    protected override async Task Execute() {
        if (!world.IsNullOrEmpty())
            await HandleWorldTravel();

        await MoveToFlag(
            MovementConfig.Default.WithOptions(MovementOptions.Mount | (Player.MapFlag.TerritoryId != 180 ? MovementOptions.Fly : MovementOptions.None)).WithTolerance(5f),
            stopCondition: () => FindHuntTarget() is not null,
            onStopReached: async () => {
                if (FindHuntTarget() is DGameObject target)
                    await MoveTo(target.Position, MovementConfig.Default.WithTolerance(TARGET_APPROACH_DISTANCE + 2f));
            }
        );
        using var stop = new OnDispose(() => Service.BossMod.ClearActive());
        await Kill();
    }

    private async Task HandleWorldTravel() {
        if (C.EnabledTweaks.Contains(nameof(InstantReturn)) && Player.Territory.RowId != Player.HomeAetheryteTerritory.RowId) {
            Chat.SendMessage("/return");
            await WaitUntilTerritory(Player.HomeAetheryteTerritory.RowId);
        }
        Service.Lifestream.ExecuteCommand(world);
        await WaitUntilThenFalse(() => Service.Lifestream.IsBusy(), "LifestreamWaitForFinish");
        await WaitUntil(() => !Player.IsBusy, "WaitForAvailable");
    }

    private async Task Kill() {
        using var scope = BeginScope("Kill");
        if (FindHuntTarget() is { } target) {
            await Dismount();
            await MoveIfNoLoS(target);
            Svc.Targets.Target = target;
            Service.BossMod.SetActiveList(["VBM Default", "VBM AI"]);
            Status = $"Waiting for {target.Name} to die";
            await TargetDead(target);
            Service.BossMod.ClearActive();
        }
        else
            Log("No hunt found.");
    }

    private IGameObject? FindHuntTarget() => Svc.Objects
        .Where(o => Player.DistanceTo(o) < HUNT_DETECTION_RADIUS && o is IBattleNpc { NameId: > 0 })
        .Select(o => new { Object = o, Row = FindRow<NotoriousMonster>(x => o.BaseId == x.BNpcBase.RowId) })
        .Where(x => x.Row.HasValue)
        .OrderByDescending(x => x.Row?.Rank)
        .Select(x => x.Object)
        .FirstOrDefault();

    private async Task MoveIfNoLoS(DGameObject target) {
        if (!Player.Object.IsInLineOfSight(target.Position)) {
            Log($"No line of sight to {target.Name}, moving...");
            var validPosition = Service.Navmesh.PointOnFloor(target.Position, false, 5);
            if (validPosition.HasValue) {
                try {
                    await MoveTo(validPosition.Value, MovementConfig.Default);
                    return;
                }
                catch (Exception ex) {
                    Log($"Failed to move to navmesh point: {ex.Message}");
                }
            }

            // try spots in a circle around target if above fails
            for (var i = 0; i < LOS_SEARCH_POSITIONS; i++) {
                var angle = (float)(i * 2 * Math.PI / LOS_SEARCH_POSITIONS);
                var searchPos = new Vector3(
                    target.Position.X + LOS_SEARCH_RADIUS * (float)Math.Cos(angle),
                    target.Position.Y,
                    target.Position.Z + LOS_SEARCH_RADIUS * (float)Math.Sin(angle)
                );

                if (Service.Navmesh.PointOnFloor(searchPos, false, 1) is { } point && target.IsInLineOfSight(point)) {
                    try {
                        await MoveTo(point, MovementConfig.Default);
                        return;
                    }
                    catch (Exception ex) {
                        Log($"Failed to move to search position {i}: {ex.Message}");
                    }
                }
            }

            // just move straight at this point and hope
            Log("Falling back to direct movement...");
            await MoveToDirectly(target.Position, TARGET_APPROACH_DISTANCE);
        }
    }

    private async Task TargetDead(DGameObject target) {
        using var scope = BeginScope("TargetDead");
        while (target != null && !target.IsDead)
            await NextFrame(30);
    }
}
