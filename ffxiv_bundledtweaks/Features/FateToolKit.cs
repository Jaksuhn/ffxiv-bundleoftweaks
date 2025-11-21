using ComplexTweaks.Features;
using ComplexTweaks.FeaturesSetup.Events;
using ComplexTweaks.Tasks;
using Dalamud.Game.ClientState.Fates;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using System.Threading.Tasks;

namespace Automaton.Features;

public class FateToolKitConfig
{
    [IntConfig(DefaultValue = 900)] public int MaxDuration = 900;
    [IntConfig(DefaultValue = 120)] public int MinTimeRemaining = 120;
    [IntConfig(DefaultValue = 90)] public int MaxProgress = 90;

    public HashSet<uint> blacklist = [];
    public HashSet<uint> whitelist = [];
    public List<uint> zones = [];
}

[Tweak]
[Requires(Ipc.Navmesh | Ipc.BossMod)]
public class FateToolKit : Tweak<FateToolKitConfig>
{
    public override string Name => "Fate Tool Kit (Date With Destiny)";
    public override string Description => "Fate tracker with additional fate automations.";

    [CommandHandler(["/dwd", "/vfate"], "Opens the FATE tracker")]
    private void OnCommand(string command, string arguments) => Window<DateWithDestinyWindow>()?.Toggle();

    [TweakEvent(TweakEvent.FateJoined, AutoEnable = false)]
    private void OnFateJoined(Type type, EventArgs args)
    {
        // return if fateargs fateid is not next fate
        if (args is FateEventArgs { FateId: var id } && Service.Automation.CurrentTask is FateGrind task && id != task.NextFate?.FateId) return;

        if (Service.BossMod.GetActive() != "")
        {
            if (Service.BossMod.Get("") is null)
                Service.BossMod.Create("", true);
            else
                Service.BossMod.SetActive("");
        }

        // todo: pull size based on role
    }

    [TweakEvent(TweakEvent.FateLeft, AutoEnable = false)]
    private void OnFateLeft(Type type, EventArgs args)
    {
        Service.BossMod.ClearActive();
        Service.Automation.Start(new FateGrind(Config));
    }

    [TweakEvent(TweakEvent.Died, AutoEnable = false)]
    private void OnDeath(Type type, EventArgs args)
    {
        Service.Automation.Start(new FateGrind(Config));
    }

    private sealed class FateGrind(FateToolKitConfig config) : CommonTasks
    {
        protected override async Task Execute()
        {
            await (State switch
            {
                FateState.Unconscious => Revive(),
                FateState.Moving => MoveToFate(),
                FateState.WaitingForFates => WaitForFate(),
                _ => NextFrame(),
            });
        }

        public unsafe IFate? CurrentFate => Svc.Fates.CreateFateReference((nint)FateManager.Instance()->CurrentFate);
        public IFate? NextFate { get; set; }

        private const int MinTimeToPrioritise = 240; // logic: if there are two fates and the further one would time out before the closer one would finish, prioritise the further one
        public unsafe IOrderedEnumerable<IFate> AvailableFates => Svc.Fates.Where(FateConditions)
            .OrderByDescending(f => f.HasBonus && Player.Status.FirstOrDefault(x => DateWithDestiny.TwistOfFateStatusIDs.Contains(x.StatusId)) != null)
            .ThenByDescending(f => f.Progress)
            .ThenByDescending(f => f.HasBonus)
            .ThenBy(f => f.TimeRemaining < MinTimeToPrioritise)
            .ThenBy(f => Player.DistanceTo(f.Position));

        private bool FateConditions(IFate f)
            => f.Duration <= config.MaxDuration
            && f.Progress <= config.MaxProgress
            && (f.TimeRemaining < 0 || f.TimeRemaining > config.MinTimeRemaining)
            && !config.blacklist.Contains(f.FateId);

        private FateState State
        {
            get
            {
                if (Svc.Condition[ConditionFlag.Unconscious])
                    return FateState.Unconscious;

                if (CurrentFate is { })
                    return FateState.Engaging;

                if (CurrentFate is not { } && AvailableFates.FirstOrDefault() is { })
                    return FateState.Moving;

                if (!AvailableFates.Any())
                    return FateState.WaitingForFates;

                return FateState.Idle;
            }
        }

        private enum FateState
        {
            Idle,
            WaitingForFates,
            Moving,
            Engaging,
            Unconscious,
        }

        private async Task Revive()
        {
            using var scope = BeginScope("WaitingForRevive");
            if (Player.Revivable)
            {
                (var lastZone, var lastPos) = (Player.Territory, Player.Position);
                Service.Memory.ExecuteCommand?.Invoke((int)ExecuteCommandFlag.Revive, 8);
                await WaitUntilTerritory(Player.HomeAetheryteTerritory);
                await TeleportTo(lastZone, lastPos);
            }
            else await NextFrame();
        }

        private async Task MoveToFate()
        {
            if (AvailableFates.FirstOrDefault() is not { } nextFate) return;
            NextFate = nextFate;
            await WaitWhile(() => Player.IsBusy, "WaitingForNotBusy");
            await MoveTo(NextFate.Position.RandomPoint(NextFate.Radius * 0.5f).OnMesh(), MovementConfig.Everything);
        }

        private async Task WaitForFate()
        {
            Status = "Waiting for fates to spawn";
            await NextFrame(60);
        }
    }
}
