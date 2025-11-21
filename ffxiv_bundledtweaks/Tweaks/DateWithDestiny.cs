using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Fates;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using ECommons.GameFunctions;
using ECommons.ImGuiMethods;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.Game.WKS;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;
using static ECommons.GameFunctions.ObjectFunctions;

namespace ComplexTweaks.Tweaks;

public class DateWithDestinyConfiguration
{
    public HashSet<uint> blacklist = [];
    public HashSet<uint> whitelist = [];
    public List<uint> zones = [];
    [BoolConfig] public bool YokaiMode;
    [BoolConfig] public bool StayInMeleeRange;
    [BoolConfig] public bool PrioritizeForlorns = true;
    [BoolConfig] public bool PrioritizeBonusFates = true;
    [BoolConfig] public bool PrioritizeStartedFates;
    [BoolConfig(DependsOn = nameof(PrioritizeBonusFates))] public bool BonusWhenTwist = false;
    [BoolConfig] public bool EquipWatch = true;
    [BoolConfig] public bool SwapMinions = true;
    [BoolConfig] public bool SwapZones = true;

    [BoolConfig] public bool FullAuto = true;
    [BoolConfig(DependsOn = nameof(FullAuto))] public bool AutoMount = true;
    [BoolConfig(DependsOn = nameof(FullAuto))] public bool AutoFly = true;
    [BoolConfig(DependsOn = nameof(FullAuto))] public bool PathToFate = true;
    [BoolConfig(DependsOn = nameof(FullAuto))] public bool AutoSync = true;
    [BoolConfig(DependsOn = nameof(FullAuto))] public bool AutoTarget = true;
    [BoolConfig(DependsOn = nameof(FullAuto))] public bool AutoMoveToMobs = true;
    [IntConfig(DefaultValue = 900)] public int MaxDuration = 900;
    [IntConfig(DefaultValue = 120)] public int MinTimeRemaining = 120;
    [IntConfig(DefaultValue = 90)] public int MaxProgress = 90;

    [BoolConfig] public bool ShowFateTimeRemaining;
    [BoolConfig] public bool ShowFateBonusIndicator;
}

[Tweak]
[Requires(Ipc.Navmesh)]
public class DateWithDestiny : Tweak<DateWithDestinyConfiguration, DateWithDestinyWindow>
{
    public override string Name => "Date with Destiny";
    public override string Description => "Fate tracker and mover. Doesn't handle combat.";

    public bool active = false;
    private static Vector3 TargetPos;
    private readonly Throttle action = new();
    private Random random = null!;

    private enum Z
    {
        MiddleLaNoscea = 134,
        LowerLaNoscea = 135,
        EasternLaNoscea = 137,
        WesternLaNoscea = 138,
        UpperLaNoscea = 139,
        WesternThanalan = 140,
        CentralThanalan = 141,
        EasternThanalan = 145,
        SouthernThanalan = 146,
        NorthernThanalan = 147,
        CentralShroud = 148,
        EastShroud = 152,
        SouthShroud = 153,
        NorthShroud = 154,
        OuterLaNoscea = 180,
        CoerthasWesternHighlands = 397,
        TheDravanianForelands = 398,
        TheDravanianHinterlands = 399,
        TheChurningMists = 400,
        TheSeaofClouds = 401,
        AzysLla = 402,
        TheFringes = 612,
        TheRubySea = 613,
        Yanxia = 614,
        ThePeaks = 620,
        TheLochs = 621,
        TheAzimSteppe = 622,
    }

    private bool yokaiMode;
    private const uint YokaiWatch = 15222;
    private static readonly uint[] YokaiMinions = [200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 390, 391, 392, 393];
    private static readonly uint[] YokaiLegendaryMedals = [15168, 15169, 15170, 15171, 15172, 15173, 15174, 15175, 15176, 15177, 15178, 15179, 15180, 30805, 30804, 30803, 30806];
    private static readonly uint[] YokaiWeapons = [15210, 15216, 15212, 15217, 15213, 15219, 15218, 15220, 15211, 15221, 15214, 15215, 15209, 30809, 30808, 30807, 30810];
    private static readonly Z[][] YokaiZones =
    [
        [Z.CentralShroud, Z.LowerLaNoscea, Z.CentralThanalan], // Jibanyan
        [Z.EastShroud, Z.WesternLaNoscea, Z.EasternThanalan], // Komasan
        [Z.SouthShroud, Z.UpperLaNoscea, Z.SouthernThanalan], // Whisper
        [Z.NorthShroud, Z.OuterLaNoscea, Z.MiddleLaNoscea], // Blizzaria
        [Z.WesternThanalan, Z.CentralShroud, Z.LowerLaNoscea], // Kyubi
        [Z.CentralThanalan, Z.EastShroud, Z.WesternLaNoscea], // Komajiro
        [Z.EasternThanalan, Z.SouthShroud, Z.UpperLaNoscea], // Manjimutt
        [Z.SouthernThanalan, Z.NorthShroud, Z.OuterLaNoscea], // Noko
        [Z.MiddleLaNoscea, Z.WesternThanalan, Z.CentralShroud], // Venoct
        [Z.LowerLaNoscea, Z.CentralThanalan, Z.EastShroud], // Shogunyan
        [Z.WesternLaNoscea, Z.EasternThanalan, Z.SouthShroud], // Hovernyan
        [Z.UpperLaNoscea, Z.SouthernThanalan, Z.NorthShroud], // Robonyan
        [Z.OuterLaNoscea, Z.MiddleLaNoscea, Z.WesternThanalan], // USApyon
        [Z.TheFringes, Z.TheRubySea, Z.Yanxia, Z.ThePeaks, Z.TheLochs, Z.TheAzimSteppe], // Lord Enma
        [Z.CoerthasWesternHighlands, Z.TheDravanianForelands, Z.TheDravanianHinterlands, Z.TheChurningMists, Z.TheSeaofClouds, Z.AzysLla], // Lord Ananta
        [Z.CoerthasWesternHighlands, Z.TheDravanianForelands, Z.TheDravanianHinterlands, Z.TheChurningMists, Z.TheSeaofClouds, Z.AzysLla], // Zazel
        [Z.TheFringes, Z.TheRubySea, Z.Yanxia, Z.ThePeaks, Z.TheLochs, Z.TheAzimSteppe], // Damona
    ];
    private static readonly List<(uint Minion, uint Medal, uint Weapon, Z[] Zones)> Yokai = [.. YokaiMinions
        .Zip(YokaiLegendaryMedals, (x, y) => (Minion: x, Medal: y))
        .Zip(YokaiWeapons, (xy, z) => (xy.Minion, xy.Medal, Weapon: z))
        .Zip(YokaiZones, (wxy, z) => (wxy.Minion, wxy.Medal, wxy.Weapon, z))];

    internal static readonly uint[] ForlornIDs = [7586, 7587];
    internal static readonly uint[] TwistOfFateStatusIDs = [1288, 1289];

    private ushort nextFateID;
    private byte fateMaxLevel;

    private ushort FateID
    {
        get; set
        {
            if (field != value)
                SyncFate(value);
            field = value;
        }
    }

    public override void DrawConfig()
    {
        DrawCommands();

        ImGui.DrawSection("Configuration");
        ImGui.Checkbox("Yo-Kai Mode (Very Experimental)", ref yokaiMode);
        ImGui.Checkbox("Prioritize targeting Forlorns", ref Config.PrioritizeForlorns);
        ImGui.Checkbox("Prioritize Fates with EXP bonus", ref Config.PrioritizeBonusFates);
        ImGui.Indent();
        using (var _ = ImRaii.Disabled(!Config.PrioritizeBonusFates))
        {
            ImGui.Checkbox("Only with Twist of Fate", ref Config.BonusWhenTwist);
        }
        ImGui.Unindent();
        ImGui.Checkbox("Prioritize fates that have progress already (up to configured limit)", ref Config.PrioritizeStartedFates);
        ImGui.Checkbox("Always close to melee range of target", ref Config.StayInMeleeRange);
        ImGui.Checkbox("Full Auto Mode", ref Config.FullAuto);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip($"All the below options will be treated as true if this is enabled.");
        ImGui.Indent();
        using (var _ = ImRaii.Disabled(Config.FullAuto))
        {
            ImGui.Checkbox("Auto Mount", ref Config.AutoMount);
            ImGui.Checkbox("Auto Fly", ref Config.AutoFly);
            ImGui.Checkbox("Auto Sync", ref Config.AutoSync);
            ImGui.Checkbox("Auto Target Mobs", ref Config.AutoTarget);
            ImGui.Checkbox("Auto Move To Mobs", ref Config.AutoMoveToMobs);
            ImGui.Checkbox("Path To Next Fate", ref Config.PathToFate);
        }
        ImGui.Unindent();

        ImGui.DrawSection("Fate Options");
        ImGui.DragInt("Max Duration (s)", ref Config.MaxDuration);
        ImGui.SameLine();
        ImGui.ResetButton(ref Config.MaxDuration, 900);

        ImGui.DragInt("Min Time Remaining (s)", ref Config.MinTimeRemaining);
        ImGui.SameLine();
        ImGui.ResetButton(ref Config.MinTimeRemaining, 120);

        ImGui.DragInt("Max Progress (%)", ref Config.MaxProgress, 1, 0, 100);
        ImGui.SameLine();
        ImGui.ResetButton(ref Config.MaxProgress, 90);

        ImGui.DrawSection("Fate Window Options");
        ImGui.Checkbox("Show Time Remaining", ref Config.ShowFateTimeRemaining);
        ImGui.Checkbox("Show Bonus Indicator", ref Config.ShowFateBonusIndicator);
    }

    public override void Enable()
    {
        random = new();
        Svc.Framework.Update += OnUpdate;
    }

    public override void Disable()
    {
        Svc.Framework.Update -= OnUpdate;
    }

    [CommandHandler("/vfate", "Opens the FATE tracker")]
    private void OnCommand(string command, string arguments) => Window<DateWithDestinyWindow>()?.Toggle();

    private unsafe void OnUpdate(IFramework framework)
    {
        if (!active || Svc.Fates.Count == 0 || Svc.Condition[ConditionFlag.MountOrOrnamentTransition] || Svc.Condition[ConditionFlag.Casting]) return;

        // Update target position continually so we don't pingpong
        if (Svc.Targets.Target != null)
        {
            var target = Svc.Targets.Target;
            TargetPos = target.Position;
            if ((Config.FullAuto || Config.AutoMoveToMobs) && !IsInMeleeRange(target.HitboxRadius + (Config.StayInMeleeRange ? 0 : 15)))
            {
                TargetAndMoveToEnemy(target);
                return;
            }
        }

        if (Service.Navmesh.IsRunning())
        {
            if (DistanceToTarget() < 2 || Svc.Targets.Target != null && DistanceToHitboxEdge(Svc.Targets.Target.HitboxRadius) <= (Config.StayInMeleeRange ? 0 : 15))
                Service.Navmesh.Stop();
            else
                return;
        }

        var cf = FateManager.Instance()->CurrentFate;
        if (cf is not null)
        {
            fateMaxLevel = cf->MaxLevel;
            FateID = cf->FateId;
            if (Svc.Condition[ConditionFlag.Mounted])
                ExecuteDismount();

            var target = GetFateMob();
            if (target != null) TargetAndMoveToEnemy(target);
        }
        else
            FateID = 0;

        if (cf is null)
        {
            if (Svc.Condition[ConditionFlag.InCombat])
            {
                if (Svc.Condition[ConditionFlag.Mounted]) ExecuteDismount();
                var target = GetMobTargetingPlayer();
                if (target != null) TargetAndMoveToEnemy(target);
            }

            if (Config.YokaiMode)
            {
                if (YokaiMinions.Contains(CurrentCompanion))
                {
                    if (Config.EquipWatch && HaveYokaiMinionsMissing() && !HasWatchEquipped() && GetItemCount(YokaiWatch) > 0)
                        PlayerEx.Equip(15222);

                    var medal = Yokai.FirstOrDefault(x => x.Minion == CurrentCompanion).Medal;
                    if (GetItemCount(medal) >= 10)
                    {
                        Debug("Have 10 of the relevant Legendary Medal. Swapping minions");
                        var minion = Yokai.FirstOrDefault(x => CompanionUnlocked(x.Minion) && GetItemCount(x.Medal) < 10 && GetItemCount(x.Weapon) < 1).Minion;
                        if (Config.SwapMinions && minion != default)
                        {
                            ECommons.Automation.Chat.SendMessage($"/minion {GetRow<Companion>(minion)?.Singular}");
                            return;
                        }
                    }

                    var zones = Yokai.FirstOrDefault(x => x.Minion == CurrentCompanion).Zones;
                    if (Config.SwapZones && !zones.Contains((Z)Svc.ClientState.TerritoryType))
                    {
                        Debug("Have Yokai minion equipped but not in appropiate zone. Teleporting");
                        if (!Svc.Condition[ConditionFlag.Casting])
                            Telepo.Instance()->Teleport((uint)Coords.GetPrimaryAetheryte((uint)zones.First())!, 0);
                        return;
                    }
                }
            }

            if ((Config.FullAuto || Config.AutoMount) && !Svc.Condition[ConditionFlag.Mounted] && !Svc.Condition[ConditionFlag.Casting])
            {
                ExecuteMount();
                return;
            }

            if ((Config.FullAuto || Config.AutoFly) && Svc.Condition[ConditionFlag.Mounted] && !Svc.Condition[ConditionFlag.InFlight] && Player.InFlightAllowedTerritory)
            {
                ExecuteJump();
                return;
            }

            var nextFate = GetFates().FirstOrDefault();
            if ((Config.FullAuto || Config.PathToFate) && nextFate is not null && Svc.Condition[ConditionFlag.InFlight] && !Service.Navmesh.PathfindInProgress())
            {
                Debug("Finding path to fate");
                nextFateID = nextFate.FateId;
                TargetPos = GetRandomPointInFate(nextFateID);
                Service.Navmesh.PathfindAndMoveTo(TargetPos, true);
            }
        }
    }

    private void TargetAndMoveToEnemy(IGameObject target)
    {
        if (Svc.Condition[ConditionFlag.Mounted]) ExecuteDismount();
        TargetPos = target.Position;
        if ((Config.FullAuto || Config.AutoTarget) && Svc.Targets.Target?.GameObjectId != target.GameObjectId)
        {
            Svc.Targets.Target = target;
        }
        if ((Config.FullAuto || Config.AutoMoveToMobs) && !Service.Navmesh.PathfindInProgress() && !IsInMeleeRange(target.HitboxRadius + (Config.StayInMeleeRange ? 0 : 15)))
        {
            Service.Navmesh.PathfindAndMoveTo(TargetPos, false);
        }
    }

    private unsafe void ExecuteActionSafe(ActionType type, uint id) => action.Exec(() => ActionManager.Instance()->UseAction(type, id));
    private void ExecuteMount() => ExecuteActionSafe(ActionType.GeneralAction, 24); // flying mount roulette
    private void ExecuteDismount() => ExecuteActionSafe(ActionType.GeneralAction, 23);
    private void ExecuteJump() => ExecuteActionSafe(ActionType.GeneralAction, 2);

    internal IOrderedEnumerable<IFate> GetFates() => Svc.Fates.Where(FateConditions)
        .OrderByDescending(x =>
        Config.PrioritizeBonusFates
        && x.HasBonus
        && (
        !Config.BonusWhenTwist
        || Player.Status.FirstOrDefault(x => TwistOfFateStatusIDs.Contains(x.StatusId)) != null)
        )
        .ThenByDescending(x => Config.PrioritizeStartedFates && x.Progress > 0)
        .ThenBy(f => Vector3.Distance(PlayerEx.Position, f.Position));
    public bool FateConditions(IFate f) => f.GameData.Value.Rule == 1 && f.State != Dalamud.Game.ClientState.Fates.FateState.Preparation && f.Duration <= Config.MaxDuration && f.Progress <= Config.MaxProgress && f.TimeRemaining > Config.MinTimeRemaining && !Config.blacklist.Contains(f.FateId);

    private unsafe DGameObject? GetMobTargetingPlayer()
        => Svc.Objects
        .FirstOrDefault(x => x is ICharacter { MaxHp: > 0 }
        && !x.IsDead
        && x.IsTargetable
        && x.IsHostile()
        && x.ObjectKind == ObjectKind.BattleNpc
        && x.SubKind == (byte)BattleNpcSubKind.Enemy
        && x.IsTargetingPlayer());

    private unsafe DGameObject? GetFateMob()
        => Svc.Objects
        .Where(x => x is ICharacter { MaxHp: > 0 }
        && !x.IsDead
        && x.IsTargetable
        && x.IsHostile()
        && x.ObjectKind == ObjectKind.BattleNpc
        && x.SubKind == (byte)BattleNpcSubKind.Enemy
        && x.Struct() != null && x.Struct()->FateId == FateID && Math.Sqrt(Math.Pow(x.Position.X - CurrentFate->Location.X, 2) + Math.Pow(x.Position.Z - CurrentFate->Location.Z, 2)) < CurrentFate->Radius)
        // Prioritize Forlorns if configured
        .OrderByDescending(x => Config.PrioritizeForlorns && ForlornIDs.Contains(x.BaseId))
        // Prioritize enemies targeting us
        .ThenByDescending(x => x.IsTargetingPlayer())
        // Deprioritize mobs in combat with other players (hopefully avoid botlike pingpong behavior in trash fates)
        .ThenBy(x => x.GetNameplateKind() == NameplateKind.HostileEngagedOther && !x.IsTargetingPlayer())
        // Prioritize closest enemy
        .ThenBy(x => Math.Floor(Vector3.Distance(PlayerEx.Position, x.Position)))
        // Prioritize lowest HP enemy
        .ThenBy(x => (x as ICharacter)?.CurrentHp)
        .FirstOrDefault();

    private unsafe uint CurrentCompanion => Svc.ClientState.LocalPlayer!.Character()->CompanionObject->Character.GameObject.BaseId;
    private unsafe bool CompanionUnlocked(uint id) => UIState.Instance()->IsCompanionUnlocked(id);
    private unsafe bool HasWatchEquipped() => InventoryManager.Instance()->GetInventoryContainer(InventoryType.EquippedItems)->GetInventorySlot(10)->ItemId == YokaiWatch;
    private unsafe bool HaveYokaiMinionsMissing() => Yokai.Any(x => CompanionUnlocked(x.Minion));
    private unsafe int GetItemCount(uint itemID) => InventoryManager.Instance()->GetInventoryItemCount(itemID);

    private unsafe FateContext* CurrentFate => FateManager.Instance()->GetFateById(nextFateID);

    private unsafe float DistanceToFate() => Vector3.Distance(CurrentFate->Location, Svc.ClientState.LocalPlayer!.Position);
    private unsafe float DistanceToTarget() => Vector3.Distance(TargetPos, Svc.ClientState.LocalPlayer!.Position);

    //Will be negative if inside hitbox
    private unsafe float DistanceToHitboxEdge(float hitboxRadius) => DistanceToTarget() - hitboxRadius;
    private unsafe bool IsInMeleeRange(float hitboxRadius)
        => DistanceToHitboxEdge(hitboxRadius) < 2;
    public unsafe Vector3 GetRandomPointInFate(ushort fateID)
    {
        var fate = FateManager.Instance()->GetFateById(fateID);
        var angle = random.NextDouble() * 2 * Math.PI;
        var randomPoint = new Vector3((float)(fate->Location.X + fate->Radius / 2 * Math.Cos(angle)), fate->Location.Y, (float)(fate->Location.Z + fate->Radius / 2 * Math.Sin(angle)));
        var point = Service.Navmesh.NearestPoint(randomPoint, 5, 5);
        return (Vector3)(point == null ? fate->Location : point);
    }

    private unsafe void SyncFate(ushort value)
    {
        if (value != 0 && !PlayerState.Instance()->IsLevelSynced)
        {
            if (Player.Level > fateMaxLevel)
                ECommons.Automation.Chat.SendMessage("/lsync");
        }
    }
}

public class DateWithDestinyWindow(DateWithDestiny tweak) : Window($"Fate Tracker##{nameof(DateWithDestinyWindow)}")
{
    internal uint SelectedTerritory = 0;

    public override bool DrawConditions() => Player.Available;

    public override void Draw()
    {
        ImGui.TextUnformatted($"Status: {(tweak.active ? "on" : "off")} (Yo-Kai: {(tweak.Config.YokaiMode ? "on" : "off")})");
        if (ImGuiComponents.IconButton(!tweak.active ? FontAwesomeIcon.Play : FontAwesomeIcon.Stop))
        {
            tweak.active ^= true;
            Service.Navmesh.Stop();
        }

        using var table = ImRaii.Table("Fates", 2, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.ScrollX | ImGuiTableFlags.NoHostExtendX);
        if (!table)
            return;

        foreach (var fate in Svc.Fates.OrderBy(x => Vector3.Distance(x.Position, Player.Position)))
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();

            if (ImGuiComponents.IconButton($"###Pathfind{fate.FateId}", FontAwesomeIcon.Map))
            {
                if (!Service.Navmesh.IsRunning())
                    Service.Navmesh.PathfindAndMoveTo(tweak.GetRandomPointInFate(fate.FateId), Svc.Condition[ConditionFlag.InFlight]);
                else
                    Service.Navmesh.Stop();
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip($"Pathfind to {fate.Position}");

            ImGui.SameLine();

            if (ImGuiComponents.IconButton($"###Flag{fate.FateId}", FontAwesomeIcon.Flag))
            {
                unsafe { AgentMap.Instance()->SetFlagMapMarker(Svc.ClientState.TerritoryType, Svc.ClientState.MapId, fate.Position); }
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip($"Set map flag to {fate.Position}");

            ImGui.SameLine();

            if (tweak.Config.ShowFateBonusIndicator && fate.HasBonus)
            {
                ImGui.Image(Svc.Texture.GetFromGameIcon(new Dalamud.Interface.Textures.GameIconLookup(65001)).GetWrapOrEmpty().Handle, new Vector2(ImGui.IconUnitHeight()));

                ImGui.SameLine();
            }

            var nameColour = tweak.FateConditions(fate) ? new Vector4(1, 1, 1, 1) : tweak.Config.blacklist.Contains(fate.FateId) ? new Vector4(1, 0, 0, 0.5f) : new Vector4(1, 1, 1, 0.5f);
            ImGuiEx.TextV(nameColour, $"[{fate.Level}] {fate.Name} {(tweak.Config.ShowFateTimeRemaining && fate.TimeRemaining >= 0 ? new TimeSpan(0, 0, (int)fate.TimeRemaining) : string.Empty)}");
            if (ImGui.IsItemHovered()) ImGui.SetTooltip($"{fate.Stringify()}\nDistance to: {Player.DistanceTo(fate.Position)}\nFate {(tweak.FateConditions(fate) ? "meets" : "doesn't meet")} conditions and {(tweak.FateConditions(fate) ? "will" : "won't")} be pathed to in auto mode.");

            ImGui.TableNextColumn();

            ImGui.DrawProgressBar(fate.Progress, 100, new Vector4(0.404f, 0.259f, 0.541f, 1));

            ImGui.SameLine();

            ImGui.SetCursorPosX(ImGui.GetContentRegionMax().X - ImGui.IconUnitWidth() - ImGui.GetStyle().WindowPadding.X);
            if (ImGuiComponents.IconButton($"###Blacklist{fate.FateId}", FontAwesomeIcon.Ban))
            {
                tweak.Config.blacklist.Add(fate.FateId);
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip($"Add to blacklist. Right click to remove.");
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                tweak.Config.blacklist.Remove(fate.FateId);
            }
        }
    }

    private unsafe List<FateWrapper> GetFates()
    {
        return Player.Territory switch
        {
            1237 => [.. WKSManager.Instance()->MechaEventModule->Events.ToArray().Select(x => new FateWrapper()
            {
                FateType = FateType.MechaEvent,
                Id = x.WKSMechaEventDataRowId,
                Progress = x.EventProgress,
                StartTime = x.EventStartTimestamp,
                Duration = x.EventEndTimestamp - x.EventStartTimestamp,
            })],
            1252 => [.. DynamicEventContainer.GetInstance()->Events.ToArray().Select(x => new FateWrapper()
            {
                FateType = FateType.DynamicEvent,
                Id = x.DynamicEventId,
                StartTime = x.StartTimestamp,
                Duration = (int)x.SecondsDuration,
                TimeLeft = x.SecondsLeft,
                Progress = x.Progress,
            })],
            _ => [.. Svc.Fates.OrderBy(x => Vector3.Distance(Player.Position, x.Position))
                .Select(x => new FateWrapper()
                {
                    FateType = FateType.Normal,
                    Id = x.FateId,
                    Location = x.Position,
                    StartTime = x.StartTimeEpoch,
                    Duration = x.Duration,
                    TimeLeft = x.TimeRemaining,
                    Progress = x.Progress
                })],
        };
    }

    private class FateWrapper
    {
        public FateType FateType { get; set; }
        public uint Id { get; set; }
        public Vector3 Location { get; set; }
        public int StartTime { get; set; }
        public int Duration { get; set; }
        public float TimeLeft { get; set; }
        public int Progress { get; set; }
    }

    private enum FateType
    {
        Normal,
        MechaEvent, // cosmic exploration
        DynamicEvent, // occult crescent
    }
}
