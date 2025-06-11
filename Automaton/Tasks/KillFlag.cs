using Automaton.Features;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.Automation;
using FFXIVClientStructs.FFXIV.Common.Component.BGCollision;
using Lumina.Excel.Sheets;
using System.Threading.Tasks;

namespace Automaton.Tasks;
public sealed class KillFlag(string world) : CommonTasks
{
    private const float HUNT_DETECTION_RADIUS = 15.0f;
    private const float LOS_SEARCH_RADIUS = 5.0f;
    private const int LOS_SEARCH_POSITIONS = 8;
    private const float TARGET_APPROACH_DISTANCE = 3.0f;
    private readonly Vector3 losOffset = new(0, 2, 0);

    protected override async Task Execute()
    {
        if (!world.IsNullOrEmpty())
            await HandleWorldTravel();

        var flagWorldPos = Coords.FlagToWorld(PlayerEx.MapFlag);
        await TeleportTo(PlayerEx.MapFlag.TerritoryId, flagWorldPos);
        await MoveTo(PlayerEx.MapFlag, new MovementConfig { Mount = true, Fly = PlayerEx.MapFlag.TerritoryId != 180 }, true);
        using var stop = new OnDispose(() => Service.BossMod.ClearActive());
        await Kill();
    }

    private async Task HandleWorldTravel()
    {
        if (C.EnabledTweaks.Contains(nameof(InstantReturn)) && Player.Territory != Player.HomeAetheryteTerritory)
        {
            Chat.SendMessage("/return");
            await WaitUntilTerritory(Player.HomeAetheryteTerritory);
        }
        Service.Lifestream.ExecuteCommand(world);
        await WaitUntilThenFalse(() => Service.Lifestream.IsBusy(), "LifestreamWaitForFinish");
        await WaitUntil(() => !Player.IsBusy, "WaitForAvailable");
    }

    private async Task Kill()
    {
        using var scope = BeginScope("Kill");
        if (FindHuntTarget() is { } target)
        {
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
        .Select(o => new { Object = o, Row = FindRow<NotoriousMonster>(x => o.DataId == x.BNpcBase.RowId) })
        .Where(x => x.Row.HasValue)
        .OrderByDescending(x => x.Row?.Rank)
        .Select(x => x.Object)
        .FirstOrDefault();

    private async Task MoveIfNoLoS(DGameObject target)
    {
        if (!IsInLineOfSight(Player.Position, target.Position))
        {
            Log($"No line of sight to {target.Name}, moving...");
            var validPosition = Service.Navmesh.PointOnFloor(target.Position, false, 5);
            if (validPosition.HasValue)
            {
                try
                {
                    await MoveTo(validPosition.Value, MovementConfig.Default);
                    return;
                }
                catch (Exception ex)
                {
                    Log($"Failed to move to navmesh point: {ex.Message}");
                }
            }

            // try spots in a circle around target if above fails
            for (var i = 0; i < LOS_SEARCH_POSITIONS; i++)
            {
                var angle = (float)(i * 2 * Math.PI / LOS_SEARCH_POSITIONS);
                var searchPos = new Vector3(
                    target.Position.X + LOS_SEARCH_RADIUS * (float)Math.Cos(angle),
                    target.Position.Y,
                    target.Position.Z + LOS_SEARCH_RADIUS * (float)Math.Sin(angle)
                );

                if (Service.Navmesh.PointOnFloor(searchPos, false, 1) is { } point && IsInLineOfSight(point, target.Position))
                {
                    try
                    {
                        await MoveTo(point, MovementConfig.Default);
                        return;
                    }
                    catch (Exception ex)
                    {
                        Log($"Failed to move to search position {i}: {ex.Message}");
                    }
                }
            }

            // just move straight at this point and hope
            Log("Falling back to direct movement...");
            await MoveToDirectly(target.Position, TARGET_APPROACH_DISTANCE);
        }
    }

    private bool IsInLineOfSight(Vector3 source, Vector3 target)
        => !BGCollisionModule.RaycastMaterialFilter(
            source + losOffset,
            Vector3.Normalize((target + losOffset) - (source + losOffset)),
            out _,
            Vector3.Distance(source + losOffset, target + losOffset)
        );

    private async Task TargetDead(DGameObject target)
    {
        using var scope = BeginScope("TargetDead");
        while (target != null && !target.IsDead)
            await NextFrame(30);
    }
}
