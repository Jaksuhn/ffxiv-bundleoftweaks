using ComplexTweaks.Features;
using Dalamud.Game.ClientState.Fates;
using ECommons.GameFunctions;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;
using System.Threading.Tasks;
using FateState = Dalamud.Game.ClientState.Fates.FateState;

namespace ComplexTweaks.Tasks;
public sealed class FateGrind(DateWithDestinyConfiguration config) : CommonTasks
{
    // TODO:
    // auto detect yokai event, set yokai mode accordingly
    private unsafe IFate? CurrentFate => Svc.Fates.CreateFateReference((nint)FateManager.Instance()->CurrentFate);
    private IFate? NextFate { get; set; }

    public unsafe IOrderedEnumerable<IFate> AvailableFates => Svc.Fates.Where(FateConditions)
        .OrderByDescending(f => f.HasBonus && Player.Status.FirstOrDefault(x => DateWithDestiny.TwistOfFateStatusIDs.Contains(x.StatusId)) != null)
        .ThenByDescending(f => f.Progress)
        .ThenByDescending(f => f.HasBonus)
        .ThenBy(f => f.TimeRemaining < MinTimeToPrioritise)
        .ThenBy(f => Player.DistanceTo(f.Position));
    //public unsafe IOrderedEnumerable<IFate> AvailableFates => Svc.Fates.Where(FateConditions).OrderBy(f => Player.DistanceTo(f.Position));

    private const int MinTimeToPrioritise = 240; // logic: if there are two fates and the further one would time out before the closer one would finish, prioritise the further one
    private const int TurnInMinimumForGold = 10;
    private const uint ChocoboMinTime = 300; // seconds
    private const uint ChocoboSummonItemId = 4868;
    private unsafe float ChocoboTimeLeft => UIState.Instance()->Buddy.CompanionInfo.TimeLeft;
    private bool CanSummonChocobo
        => !Player.IsBusy
        && Player.TerritoryIntendedUse == ECommons.ExcelServices.TerritoryIntendedUseEnum.Open_World
        && Inventory.HasItem(ChocoboSummonItemId)
        && !Game.IsActionInUse(ActionType.Item, ChocoboSummonItemId);

    protected override async Task Execute()
    {
        while (true)
        {
            var currentState = GetGrindState();
            Status = currentState.ToString();

            switch (currentState)
            {
                case FateGrindState.Unconscious:
                    await Revive();
                    break;

                case FateGrindState.CollectFateTurnIn:
                    await CollectFateTurnIn();
                    break;

                case FateGrindState.ManagingCombat:
                    await ManageCombat();
                    break;

                case FateGrindState.ManagingFate:
                    await ManageFate();
                    break;

                case FateGrindState.MovingToFate:
                    await MoveToFate();
                    break;

                case FateGrindState.WaitingForFates:
                    await WaitForFate();
                    break;

                default:
                    await NextFrame();
                    break;
            }
        }
    }

    private async Task Revive()
    {
        using var scope = BeginScope("WaitingForRevive");
        if (Player.Revivable) // TODO: if in a party, wait for res instead of reviving
            await ResurrectAndReturn();
        else
            await NextFrame();
    }

    private async Task ResurrectAndReturn()
    {
        using var scope = BeginScope($"{nameof(ResurrectAndReturn)}");
        (var lastZone, var lastPos) = (Player.Territory, Player.Position);
        Service.Memory.ExecuteCommand?.Invoke((int)ExecuteCommandFlag.Revive, 8); // TODO: it's either 8 or 5 depending on what GameMain.field_4095 is
        await WaitUntilTerritory(Player.HomeAetheryteTerritory);
        await TeleportTo(lastZone, lastPos);
    }

    private async Task CollectFateTurnIn()
    {
        if (CurrentFate is not { } fate) return;

        if (fate.HandInCount >= TurnInMinimumForGold && fate.Progress == 100)
        {
            // We've already turned in enough for gold, just leave
            await WaitWhile(() => PlayerEx.HatersWithFullAggro > 0, "WaitingForHatersToDie");
            await LeaveFate();
        }
        else
            await TurnIn();
    }

    private async Task LeaveFate()
    {
        using var scope = BeginScope("LeaveFate");
        // since CurrentFate is just an auto field, we need to leave the fate manually. Most natural thing to do would be to mount up and fly in the vague direction of the nearest fate
        if (CurrentFate == null) return;
        Vector3 RandomCoordOutsideFate(IFate? nextFate = null)
        {
            // go in the direction of next fate or your position relative to the current fate just a bit past the fate and a random amount up in the air
            var direction = Vector3.Normalize((nextFate?.Position ?? Player.Position) - CurrentFate.Position);
            return CurrentFate.Position + direction * (CurrentFate.Radius * new Random().NextFloat(1.1f, 1.4f)) + new Vector3(0, new Random().Next(10, 30), 0);
        }
        // TODO: this might generate a point inside a mountain or something
        await MoveTo(RandomCoordOutsideFate(AvailableFates.FirstOrDefault()), MovementConfig.Everything);
    }

    private unsafe DGameObject? FateTurnInNpc => Svc.Objects.FirstOrDefault(o => o.Struct()->NamePlateIconId == 60732);
    private async Task TurnIn()
    {
        using var scope = BeginScope("TurnIn");
        if (FateTurnInNpc is { } npc)
        {
            await WaitWhile(() => PlayerEx.HatersWithFullAggro > 0, "WaitingForHatersToDie");
            Service.BossMod.ClearActive();
            await MoveTo(npc.Position, MovementConfig.InteractRange);
            if (PlayerEx.HatersWithFullAggro > 0)
            {
                Service.BossMod.SetActive("AI");
                Service.BossMod.AddTransientStrategy("AI", BossModIPC.Modules.AutoFarm, "General", "FightBack");
            }
            Status = "Waiting for Haters to die";
            await WaitWhile(() => PlayerEx.HatersWithFullAggro > 0, "WaitingForHatersToDie");
            Status = "Turning in EventItems";
            await InteractWith(npc, () => CurrentFate!.EventItemInventoryCount() == 0, null, UiSkipOptions.Talk | UiSkipOptions.Request);
            await WaitUntilSkipping(() => !Player.IsBusy, "WaitingForExitNpc", UiSkipOptions.Talk); // might need to rethink. This is a general combat check vs hater check
            Service.BossMod.ClearTransientPresetStrategies("AI");
        }
    }

    private async Task ManageCombat()
    {
        Status = "Waiting for combat to end";

        // Remove when vbm module supports it
        if (PlayerEx.Haters.Length < 3)
        {
            Log($"Not enough haters [{PlayerEx.Haters.Length}], setting AI to aggressive");
            Service.BossMod.AddTransientStrategy("AI", BossModIPC.Modules.AutoFarm, "General", "Aggressive");
        }
        else
        {
            Log($"Too many haters [{PlayerEx.Haters.Length}], resetting AI aggression");
            Service.BossMod.ClearTransientStrategy("AI", BossModIPC.Modules.AutoFarm, "General");
        }

        await NextFrame(30);
    }

    private async Task ManageFate()
    {
        if (CanSummonChocobo && ChocoboTimeLeft <= ChocoboMinTime)
            await SummonChocobo();

        if (NextFate is { State: FateState.Preparation } && Player.DistanceTo(NextFate.Position) < NextFate.Radius)
            await ActivateFate();

        if (CurrentFate is { } fate)
        {
            await WaitWhile(() => Player.IsBusy, "WaitingForNotBusy");
            if (Player.Level > fate.MaxLevel)
            {
                Status = "Syncing to Fate";
                Service.Memory.ExecuteCommand?.Invoke((int)ExecuteCommandFlag.FateLevelSync, fate.FateId, 1);
                Service.BossMod.SetActive("AI");
            }
        }
        else
        {
            // Don't clear preset immediately in case we're still in combat after fate ends
            await WaitWhile(() => Player.IsBusy, "WaitingForNotBusy");
            Service.BossMod.ClearActive();
        }
    }

    private async Task SummonChocobo()
    {
        using var scope = BeginScope("SummonChocobo");
        Game.UseItem(ChocoboSummonItemId);
        await WaitUntil(() => ChocoboTimeLeft > ChocoboMinTime, "WaitingForChocobo");
    }

    private unsafe DGameObject? FateActivationNpc => Svc.Objects.FirstOrDefault(o => o.Struct()->NamePlateIconId == 60093);
    private async Task ActivateFate()
    {
        using var scope = BeginScope("ActivateFate");
        if (FateActivationNpc is { } npc)
        {
            await MoveTo(npc.Position, MovementConfig.GroundMove.WithTolerance(3));
            await InteractWith(npc, () => NextFate!.State == FateState.Running, null, UiSkipOptions.Talk | UiSkipOptions.YesNo);
        }
    }

    private async Task MoveToFate()
    {
        if (AvailableFates.FirstOrDefault() is not { } nextFate) return;
        NextFate = nextFate;
        await WaitWhile(() => Player.IsBusy, "WaitingForNotBusy");
        await MoveTo(GetRandomPointInFate(NextFate), MovementConfig.Everything);
    }

    private async Task WaitForFate()
    {
        //if (config.SwapZones)
        //    await SwapZones();
        //else
        Status = "Waiting for fates to spawn";
        await NextFrame(60);
    }

    private async Task SwapZones()
    {
        // if we're achievement farming, find the next zone where the achievement isn't completed, otherwise, pick a random zone within the same expac
        // if we're yokai farming, find the next zone where the yokai isn't completed
        using var scope = BeginScope("SwapZones");
        var zoneId = GetNextAchievementZone() is { } zone ? zone : GetRandomSameExpacZone();
        await TeleportTo(zoneId, default);
    }

    private unsafe uint? GetNextAchievementZone()
    {
        var agent = AgentFateProgress.Instance();
        if (agent == null) return null;
        // prioritise zones in the same expac as current area
        var currentTabIndex = Array.FindIndex(agent->Tabs.ToArray(), tab => tab.Zones.ToArray().Any(zone => Player.Territory == zone.TerritoryTypeId));

        if (currentTabIndex != -1 && currentTabIndex < agent->Tabs.Length - 1)
        {
            // get zone in expac that needs fates
            var nullableZone = agent->Tabs[currentTabIndex].Zones.ToArray().FirstOrNull(zone => zone.NeededFates - zone.FateProgress > 0);
            return nullableZone is { } zone ? zone.TerritoryTypeId : null;
        }
        else
        {
            // get zone from any shared fate expac that needs fates
            var nullableZone = agent->Tabs.ToArray().SelectMany(tab => tab.Zones.ToArray()).FirstOrNull(zone => zone.NeededFates - zone.FateProgress > 0);
            return nullableZone is { } zone ? zone.TerritoryTypeId : null;
        }
    }

    private uint GetRandomSameExpacZone()
    {
        var rows = FindRows<TerritoryType>(x => x.ExVersion.RowId == GetRow<TerritoryType>(Player.Territory)!.Value.ExVersion.RowId);
        return rows[new Random().Next(rows.Length)].RowId;
    }

    private FateGrindState GetGrindState()
    {
        if (Svc.Condition[ConditionFlag.Unconscious])
            return FateGrindState.Unconscious;

        if (CurrentFate is { GameData.Value.Rule: (byte)FateRule.Collect } && ShouldHandInItems())
            return FateGrindState.CollectFateTurnIn;

        if (Svc.Condition[ConditionFlag.InCombat])
            return FateGrindState.ManagingCombat;

        if (CurrentFate is { } || NextFate is { State: FateState.Preparation } && Player.DistanceTo(NextFate.Position) < NextFate.Radius)
            return FateGrindState.ManagingFate;

        if (CurrentFate is not { } && AvailableFates.FirstOrDefault() is { })
            return FateGrindState.MovingToFate;

        if (!AvailableFates.Any())
            return FateGrindState.WaitingForFates;

        return FateGrindState.Idle;
    }

    private bool ShouldHandInItems()
    {
        if (CurrentFate is not { } fate) return false;

        // We've already turned in enough for gold, just leave
        if (fate.HandInCount >= TurnInMinimumForGold && fate.Progress == 100)
            return true;

        // Turn in whenever we can get gold or if we're running out of time and have enough items to get gold
        return fate.EventItemInventoryCount() >= TurnInMinimumForGold || (fate.Progress == 100 || fate.TimeRemaining < 60) && fate.EventItemInventoryCount() + fate.HandInCount >= TurnInMinimumForGold;
    }

    private bool FateConditions(IFate f)
        => f.Duration <= config.MaxDuration
        && f.Progress <= config.MaxProgress
        && (f.TimeRemaining < 0 || f.TimeRemaining > config.MinTimeRemaining)
        && !config.blacklist.Contains(f.FateId);

    private unsafe Vector3 GetRandomPointInFate(IFate fate)
    {
        var randomPoint = Utils.RandomPointInCircle(fate.Position, fate.Radius, 0.5f);
        var point = Service.Navmesh.NearestPoint(randomPoint, 5, 5);
        return (Vector3)(point == null ? fate.Position : point);
    }

    private enum FateGrindState
    {
        Idle,
        Unconscious,
        CollectFateTurnIn,
        ManagingCombat,
        ManagingFate,
        MovingToFate,
        WaitingForFates
    }

    private enum FateRule : byte
    {
        None = 0,
        Normal = 1, // trash fates or boss fates
        Collect = 2, // pick up EventObjects or get them from killing mobs
        Escort = 3, // guide some npc to the finish line
        Defend = 4, // defend objectives like crates from being destroyed
        EventFate = 5, // used for seasonal event fates, like Little Ladies Day, Hatching Tide
        Chase = 6, // that one special fate in The Peaks
        ConcertedWorks = 7, // rebuilding the firmament fates
        Fete = 8, // firmament fates
    }
}
