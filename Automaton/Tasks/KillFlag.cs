using Automaton.Features;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.Automation;
using FFXIVClientStructs.FFXIV.Common.Component.BGCollision;
using Lumina.Excel.Sheets;
using System.Threading.Tasks;

namespace Automaton.Tasks;
public sealed class KillFlag(string world) : CommonTasks
{
    protected override async Task Execute()
    {
        if (!world.IsNullOrEmpty())
        {
            if (C.EnabledTweaks.Contains(nameof(InstantReturn)) && Player.Territory != Player.HomeAetheryteTerritory)
            {
                Chat.Instance.SendMessage("/return");
                await WaitUntilTerritory(Player.HomeAetheryteTerritory);
            }
            Service.Lifestream.ExecuteCommand($"{world}");
            await WaitUntilThenFalse(() => Service.Lifestream.IsBusy(), "LifestreamWaitForFinish");
            await WaitUntil(() => !Player.IsBusy, "WaitForAvailable");
        }
        await TeleportTo(PlayerEx.MapFlag.TerritoryId, Coords.FlagToWorld(PlayerEx.MapFlag));
        await MoveTo(PlayerEx.MapFlag, new MovementConfig { Mount = true, Fly = PlayerEx.MapFlag.TerritoryId != 180 }); // just don't ever fly in outer la noscea until navmesh is better
        using var stop = new OnDispose(() => Service.BossMod.ClearActive());
        await Kill();
    }

    private async Task Kill()
    {
        using var scope = BeginScope("Kill");
        IGameObject? GetHunt() => Svc.Objects
            .Where(o => o is IBattleNpc { NameId: > 0 })
            .Select(o => new { Object = o, Row = FindRow<NotoriousMonster>(x => o.DataId == x.BNpcBase.RowId) })
            .Where(x => x.Row.HasValue)
            .OrderByDescending(x => x.Row?.Rank)
            .Select(x => x.Object)
            .FirstOrDefault();
        if (GetHunt() is { } target)
        {
            await Dismount();
            await MoveIfNoLoS(target);
            Svc.Targets.Target = target;
            Service.BossMod.SetActiveList(["VBM Default", "VBM AI"]);
            Status = $"Waiting for {target.Name} to die";
            await TargetDead(target);
            Service.BossMod.ClearActive();
        }
    }

    private async Task MoveIfNoLoS(DGameObject target)
    {
        if (!IsInLineOfSight(Player.Position, target.Position))
        {
            // Try positions in a circle around the target
            const float searchRadius = 5.0f;
            const int numPositions = 8;

            for (var i = 0; i < numPositions; i++)
            {
                var angle = (float)(i * 2 * Math.PI / numPositions);
                var searchPos = new Vector3(target.Position.X + searchRadius * (float)Math.Cos(angle), target.Position.Y, target.Position.Z + searchRadius * (float)Math.Sin(angle));
                if (Service.Navmesh.PointOnFloor(searchPos, false, 1) is { } point && IsInLineOfSight(point, target.Position))
                {
                    await MoveTo(searchPos, MovementConfig.Default);
                    return;
                }
            }
        }
    }

    private Vector3 losOffset = new(0, 2, 0);
    private bool IsInLineOfSight(Vector3 source, Vector3 target)
        => !BGCollisionModule.RaycastMaterialFilter(source + losOffset, Vector3.Normalize((target + losOffset) - (source + losOffset)), out _, Vector3.Distance(source + losOffset, target + losOffset));

    private async Task TargetDead(DGameObject target)
    {
        using var scope = BeginScope("TargetDead");
        while (target != null && !target.IsDead)
            await NextFrame(30);
    }
}
