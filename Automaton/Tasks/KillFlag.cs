using Automaton.Features;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.Automation;
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
        await MoveTo(PlayerEx.MapFlag, 5, true, PlayerEx.MapFlag.TerritoryId != 180); // just don't ever fly in outer la noscea until navmesh is better
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
            Svc.Targets.Target = target;
            Service.BossMod.SetActiveList(["VBM Default", "VBM AI"]);
            Status = $"Waiting for {target.Name} to die";
            await TargetDead(target);
            Service.BossMod.ClearActive();
        }
    }

    private async Task TargetDead(DGameObject target)
    {
        using var scope = BeginScope("TargetDead");
        while (target != null && !target.IsDead)
            await NextFrame(30);
    }
}
