using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using Lumina.Excel.Sheets;
using System.Threading.Tasks;

namespace ComplexTweaks.Tweaks;

public partial class CommandsConfiguration {
    [BoolConfig(Label = "/killflag")]
    public bool EnableKillFlag = false;
}

public partial class Commands : Tweak<CommandsConfiguration> {
    [Requires(Ipc.BossMod | Ipc.Navmesh)]
    [CommandHandler(["/killflag", "/kf"], "Goes to flag, kills hunt mob at destination.", nameof(Config.EnableKillFlag))]
    internal void OnCommandKillFlag(string _, string arguments) => Svc.Automation.Start(new KillFlag(arguments));
}

public sealed class KillFlag(string world) : TaskBase {
    private const float HUNT_DETECTION_RADIUS = 25.0f;
    private const float LOS_SEARCH_RADIUS = 5.0f;
    private const int LOS_SEARCH_POSITIONS = 8;
    private const float TARGET_APPROACH_DISTANCE = 3.0f;

    protected override async Task Execute() {
        using var scope = BeginScope(nameof(KillFlag));
        if (!world.IsEmpty && IDataManager.Get().FindRow<World>(r => r.Name.ToString().Contains(world, StringComparison.OrdinalIgnoreCase)) is { RowId: var id })
            await HandleWorldTravel((ushort)id);

        await MoveToFlag(MovementConfig.Default.WithOptions(MovementOptions.Mount | (IPlayerState.Get().MapFlag.TerritoryId != 180 ? MovementOptions.Fly : MovementOptions.None)).WithTolerance(5f));
        using var stop = new OnDispose(() => BossModIPC.Get().ClearActive());
        await Kill();
    }

    private async Task HandleWorldTravel(ushort worldId) {
        using var scope = BeginScope(nameof(HandleWorldTravel));
        if (TweakService.Get().IsEnabled("InstantReturn") && IPlayerState.Get().Territory.RowId != IPlayerState.Get().HomeAetheryte.Value.Territory.RowId)
            await Return();
        unsafe {
            AgentWorldTravel.Instance()->Travel(worldId);
        }
        await WaitUntil(() => IPlayerState.Get().CurrentWorld.RowId == worldId && IObjectTable.Get().LocalPlayer.Interactable, "WaitForWorldTravel");
    }

    private async Task Return() {
        using var scope = BeginScope(nameof(Return));
        if (InfoProxyCrossRealm.IsLocalPlayerInParty()) {
            if (InfoProxyCrossRealm.IsLocalPlayerPartyLeader())
                await WaitUntil(IPartyList.Get().DisbandParty, "WaitForDisband");
            else
                await WaitUntil(IPartyList.Get().LeaveParty, "WaitForLeave");
        }

        GameMain.ExecuteCommand(CommandFlag.InstantReturn.Value);
        await WaitUntilTerritory(IPlayerState.Get().HomeAetheryte.Value.Territory.RowId);
    }

    private async Task Kill() {
        using var scope = BeginScope(nameof(Kill));
        var target = FindHuntTarget();
        if (target is { }) {
            await MoveTo(target.Position, MovementConfig.Default.WithTolerance(TARGET_APPROACH_DISTANCE + 2f).WithOptions(MovementOptions.Dismount));
            await MoveIfNoLoS(target);
            ITargetManager.Get().Target = target;
            BossModIPC.Get().SetActiveList(["VBM Default", "VBM AI"]);
            Status = $"Waiting for {target.Name} to die";
            await TargetDead(target);
            BossModIPC.Get().ClearActive();
        }
        else {
            Log("No hunt found.");
        }
    }

    private IGameObject? FindHuntTarget()
        => NavmeshIPC.Get().FlagToPoint() is not { } fp ? null
            : IObjectTable.Get().Where(o => o is IBattleNpc { NameId: > 0 } && Vector3.Distance(o.Position, fp) <= HUNT_DETECTION_RADIUS)
            .Select(o => (Object: o, Distance: Vector3.Distance(o.Position, fp), Row: NotoriousMonster.FirstOrNull(r => o.BaseId == r.BNpcBase.RowId)))
            .Where(t => t.Row.HasValue)
            .OrderBy(t => (t.Distance, -t.Row!.Value.Rank))
            .Select(t => t.Object)
            .FirstOrDefault();

    private async Task MoveIfNoLoS(DGameObject target) {
        if (target.IsInLineOfSight()) return;

        using var scope = BeginScope(nameof(MoveIfNoLoS));
        Log($"No line of sight to {target.Name}, moving...");
        var validPosition = NavmeshIPC.Get().PointOnFloor(target.Position, false, 5);
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

            if (NavmeshIPC.Get().PointOnFloor(searchPos, false, 1) is { } point && target.IsInLineOfSight(point)) {
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

    private async Task TargetDead(DGameObject target) {
        using var scope = BeginScope(nameof(TargetDead));
        while (target != null && !target.IsDead)
            await NextFrame(30);
    }
}

