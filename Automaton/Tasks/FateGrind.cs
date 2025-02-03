using Automaton.Features;
using Dalamud.Game.ClientState.Fates;
using ECommons.Automation;
using ECommons.GameFunctions;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;
using System.Threading.Tasks;
using FateState = Dalamud.Game.ClientState.Fates.FateState;

namespace Automaton.Tasks;
public sealed class FateGrind(DateWithDestinyConfiguration config) : CommonTasks
{
    // TODO:
    // auto detect yokai event, set yokai mode accordingly
    private unsafe IFate? CurrentFate => Svc.Fates.CreateFateReference((nint)FateManager.Instance()->CurrentFate);
    private IFate? NextFate { get; set; }

    //public unsafe IOrderedEnumerable<IFate> AvailableFates => Svc.Fates.Where(FateConditions).OrderByDescending(f => f.Progress).ThenByDescending(f => f.HasBonus).ThenBy(f => Player.DistanceTo(f.Position));
    public unsafe IOrderedEnumerable<IFate> AvailableFates => Svc.Fates.Where(FateConditions).OrderBy(f => Player.DistanceTo(f.Position));

    private const int TurnInMinimumForGold = 10;
    private const uint ChocoboMinTime = 300; // seconds
    private const uint ChocoboSummonItemId = 4868;
    private unsafe float ChocoboTimeLeft => UIState.Instance()->Buddy.CompanionInfo.TimeLeft;
    private bool CanSummonChocobo
        => !Player.IsBusy
        && Player.TerritoryIntendedUse == ECommons.ExcelServices.TerritoryIntendedUseEnum.Open_World
        && Inventory.HasItem(ChocoboSummonItemId)
        && !Game.IsActionInUse(ActionType.Item, ChocoboSummonItemId);

    private int _turnInCount = 0;

    private ushort FateID
    {
        get; set
        {
            if (field != value)
            {
                SyncFate(value);
                _turnInCount = 0;
            }
            field = value;
        }
    }

    protected override async Task Execute()
    {
        while (true)
        {
        start:
            if (Svc.Condition[ConditionFlag.Unconscious])
            {
                if (Player.Revivable) // TODO: if in a party, wait for res instead of reviving
                    await Resurrect();
                else
                    await NextFrame();
                goto start;
            }

            if (CurrentFate is { GameData.Value.Rule: (byte)FateRule.Collect })
            {
                if (_turnInCount >= TurnInMinimumForGold && CurrentFate is { Progress: 100 }) // we've already turned in enough for gold, just leave
                {
                    await WaitWhile(HasHaters, "WaitingForHatersToDie");
                    await LeaveFate();
                }
                // turn in whenever we can get gold or if we're running out of time and have enough items to get gold
                if (CurrentFate.EventItemInventoryCount() >= TurnInMinimumForGold || CurrentFate is { Progress: 100 } or { TimeRemaining: < 60 } && CurrentFate.EventItemInventoryCount() + _turnInCount >= TurnInMinimumForGold)
                {
                    await TurnIn();
                    goto start;
                }
            }

            if (Svc.Condition[ConditionFlag.InCombat])
            {
                Status = "Waiting for combat to end";
                await NextFrame(30);
                goto start;
            }

            if (CanSummonChocobo && ChocoboTimeLeft <= ChocoboMinTime)
                await SummonChocobo();

            if (NextFate is { State: FateState.Preparation } && Player.DistanceTo(NextFate.Position) < NextFate.Radius)
                await ActivateFate();

            if (CurrentFate is { } fate)
            {
                await WaitWhile(() => Player.IsBusy, "WaitingForNotBusy"); // confirm this check works. For collect fates, you're in a state where you can't call lsync immediately after talking
                Status = "Syncing to Fate";
                FateID = fate.FateId;
                Service.BossMod.SetActive("AI");
            }
            else
            {
                FateID = 0;
                // don't clear preset immediately in case we're still in combat after fate ends
                await WaitWhile(() => Player.IsBusy, "WaitingForNotBusy");
                Service.BossMod.ClearActive();
            }

            if (CurrentFate is not { } && AvailableFates.FirstOrDefault() is { } nextFate)
            {
                NextFate = nextFate;
                await WaitWhile(() => Player.IsBusy, "WaitingForNotBusy");
                await MoveTo(GetRandomPointInFate(NextFate.FateId), 5, true, true);
            }

            if (!AvailableFates.Any())
            {
                //if (config.SwapZones)
                //    await SwapZones();
                //else
                Status = "Waiting for fates to spawn";
                await NextFrame();
            }

            await NextFrame();
        }
    }

    private async Task SummonChocobo()
    {
        Status = "Summoning Chocobo";
        Game.UseItem(ChocoboSummonItemId);
        await WaitUntil(() => ChocoboTimeLeft > ChocoboMinTime, "WaitingForChocobo");
    }

    private unsafe DGameObject? FateActivationNpc => Svc.Objects.FirstOrDefault(o => o.Struct()->NamePlateIconId == 60093);
    private async Task ActivateFate()
    {
        if (FateActivationNpc is { } npc)
        {
            Status = "Activating fate";
            await MoveTo(npc.Position, 3, dismount: true);
            await InteractWith(npc, () => NextFate!.State == FateState.Running, skipTalk: true, skipYesNo: true);
        }
    }

    private unsafe DGameObject? FateTurnInNpc => Svc.Objects.FirstOrDefault(o => o.Struct()->NamePlateIconId == 60732);
    private async Task TurnIn()
    {
        if (FateTurnInNpc is { } npc)
        {
            await WaitWhile(HasHaters, "WaitingForHatersToDie");
            Service.BossMod.ClearActive();
            await MoveTo(npc.Position, 3);
            if (HasHaters())
            {
                Service.BossMod.SetActive("AI");
                Service.BossMod.AddTransientStrategy("AI", "Misc AI: Automatic Farming", "General", "FightBack");
            }
            Status = "Waiting for Haters to die";
            await WaitWhile(HasHaters, "WaitingForHatersToDie");
            _turnInCount += CurrentFate!.EventItemInventoryCount();
            Status = "Turning in EventItems";
            await InteractWith(npc, () => CurrentFate!.EventItemInventoryCount() == 0, skipTalk: true, skipRequest: true);
            await WaitUntilSkipping(() => !Player.IsBusy, "WaitingForExitNpc", skipTalk: true); // might need to rethink. This is a general combat check vs hater check
            Service.BossMod.ClearTransientPresetStrategies("AI");
        }
    }

    private async Task LeaveFate()
    {
        // since CurrentFate is just an auto field, we need to physically leave the fate manually. Most natural thing to do would be to mount up and fly in the vague direction of the nearest fate
        Status = "Leaving Fate";
        if (CurrentFate == null) return;
        Vector3 RandomCoordOutsideFate(IFate? nextFate = null)
        {
            // go in the direction of next fate or your position relative to the current fate just a bit past the fate and a random amount up in the air
            var direction = Vector3.Normalize((nextFate?.Position ?? Player.Position) - CurrentFate.Position);
            return CurrentFate.Position + direction * (CurrentFate.Radius * new Random().NextFloat(1.1f, 1.4f)) + new Vector3(0, new Random().Next(10, 30), 0);
        }
        // TODO: this might generate a point inside a mountain or something
        await MoveTo(RandomCoordOutsideFate(AvailableFates.FirstOrDefault()), 5, true, true);
    }

    private async Task Resurrect()
    {
        Status = "Reviving";
        (var lastZone, var lastPos) = (Player.Territory, Player.Position);
        Service.Memory.ExecuteCommand?.Invoke((int)ExecuteCommandFlag.Revive, 8); // TODO: it's either 8 or 5 depending on what GameMain.field_4095 is
        await WaitWhile(() => Player.Territory != Player.HomeAetheryteTerritory || Player.IsBusy, "WaitingForRevive");
        await TeleportTo(lastZone, lastPos);
    }

    private async Task SwapZones()
    {
        // if we're achievement farming, find the next zone where the achievement isn't completed, otherwise, pick a random zone within the same expac
        // if we're yokai farming, find the next zone where the yokai isn't completed
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

    private bool FateConditions(IFate f)
        => f.Duration <= config.MaxDuration
        && f.Progress <= config.MaxProgress
        && (f.TimeRemaining < 0 || f.TimeRemaining > config.MinTimeRemaining)
        && !config.blacklist.Contains(f.FateId);

    public IOrderedEnumerable<IFate> GetFates() => Svc.Fates.Where(FateConditions)
        .OrderByDescending(x => config.PrioritizeBonusFates && x.HasBonus && (!config.BonusWhenTwist || Player.Status.FirstOrDefault(x => DateWithDestiny.TwistOfFateStatusIDs.Contains(x.StatusId)) != null))
        .ThenByDescending(x => config.PrioritizeStartedFates && x.Progress > 0)
        .ThenBy(f => Vector3.Distance(PlayerEx.Position, f.Position));

    private unsafe Vector3 GetRandomPointInFate(ushort fateID)
    {
        var fate = FateManager.Instance()->GetFateById(fateID);
        var angle = new Random().NextDouble() * 2 * Math.PI;
        // Get a random point in a circle within half its radius
        var randomPoint = new Vector3((float)(fate->Location.X + fate->Radius / 2 * Math.Cos(angle)), fate->Location.Y, (float)(fate->Location.Z + fate->Radius / 2 * Math.Sin(angle)));
        var point = Service.Navmesh.NearestPoint(randomPoint, 5, 5);
        return (Vector3)(point == null ? fate->Location : point);
    }

    private static unsafe bool HasHaters()
    {
        for (var h = 0; h < UIState.Instance()->Hater.HaterCount; h++)
            if (UIState.Instance()->Hater.Haters[h].Enmity == 100)
                return true;
        return false;
    }

    private unsafe void SyncFate(ushort value)
    {
        if (value != 0 && Player.Level > CurrentFate?.MaxLevel)
            Chat.Instance.SendMessage("/lsync");
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
