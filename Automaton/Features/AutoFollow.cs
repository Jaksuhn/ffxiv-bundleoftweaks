using Automaton.Utilities.Movement;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using ECommons;
using ECommons.GameFunctions;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace Automaton.Features;

public class AutoFollowConfiguration
{
    [EnumConfig] public MovementType MovementType;

    [IntConfig(DefaultValue = 3)] public int DistanceToKeep = 3;
    [IntConfig] public int DisableIfFurtherThan;
    [BoolConfig] public bool OnlyInDuty;
    [BoolConfig] public bool MountAndFly;
    [BoolConfig] public bool ExcludeCombat;
    [StringConfig] public string AutoFollowName = string.Empty;
}

[Tweak]
public unsafe class AutoFollow : Tweak<AutoFollowConfiguration>
{
    public override string Name => "Auto Follow";
    public override string Description
        => "True Auto Follow. Trigger with /autofollow while targeting someone. Use it with no target to wipe the current master.\n" +
        "If multiboxing, you can send \"autofollow\" to chat and anyone in the party with this feature enabled will follow.\n" +
        "You can also add a number argument to specify the distance to keep, or add the off argument to clear the current master.";

    private readonly OverrideMovement movement = new();
    private DGameObject? master;
    private uint? masterObjectID;

    [CommandHandler("/autofollow", "Enable AutoFollow")]
    internal void OnCommand(string command, string arguments)
    {
        if (!arguments.IsNullOrEmpty())
        {
            var obj = Svc.Objects.FirstOrDefault(o => o.Name.TextValue.ToLowerInvariant().Contains(arguments, StringComparison.InvariantCultureIgnoreCase));
            if (obj != null)
            {
                master = obj;
                masterObjectID = obj.EntityId;
                return;
            }
        }
        if (Svc.Targets.Target != null)
            SetMaster();
        else
            ClearMaster();
    }

    public override void Enable()
    {
        Svc.Framework.Update += Follow;
        Svc.Chat.ChatMessage += OnChatMessage;
    }

    public override void Disable()
    {
        Svc.Framework.Update -= Follow;
        Svc.Chat.ChatMessage -= OnChatMessage;
    }

    private void SetMaster()
    {
        try
        {
            master = Svc.Targets.Target;
            masterObjectID = Svc.Targets.Target?.EntityId;
        }
        catch { return; }
    }

    private void ClearMaster()
    {
        master = null;
        masterObjectID = null;
    }

    private void Follow(IFramework framework)
    {
        if (master == null && Config.AutoFollowName.IsNullOrEmpty() || !Player.Available || TaskManager.IsBusy) return;

        master = Svc.Objects.FirstOrDefault(x => x.EntityId == masterObjectID || !Config.AutoFollowName.IsNullOrEmpty() && x.Name.TextValue.Equals(Config.AutoFollowName, StringComparison.InvariantCultureIgnoreCase));

        if (master == null) { movement.Enabled = false; return; }
        if (Config.DisableIfFurtherThan > 0 && Player.DistanceTo(master) >= Config.DisableIfFurtherThan) { movement.Enabled = false; return; }
        if (Config.OnlyInDuty && !Player.IsInDuty) { movement.Enabled = false; return; }
        if (Config.ExcludeCombat && Svc.Condition[ConditionFlag.InCombat]) { movement.Enabled = false; return; }
        if (Svc.Condition[ConditionFlag.InFlight]) { TaskManager.Abort(); }

        if (master.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player)
        {
            // prioritise riding pillion
            //if (Svc.Party.Any(p => p.ObjectId == master.GameObjectId) && GetRow<Mount>(master.Character()->Mount.MountId)?.ExtraSeats > 0)
            //{
            //    if (P.Memory.RidePillion == null) goto Mount;
            //    // ignore DistanceToKeep
            //    if (!Player.IsNear(master))
            //    {
            //        movement.Enabled = true;
            //        movement.DesiredPosition = master.Position;
            //        return;
            //    }
            //    else
            //    {
            //        movement.Enabled = false;
            //        if (Svc.Condition[ConditionFlag.Mounted])
            //        {
            //            ActionManager.Instance()->UseAction(ActionType.GeneralAction, 23);
            //            return;
            //        }
            //        TaskManager.Enqueue(() => Svc.Log.Debug("Detected mounted party member with extra seats, mounting..."));
            //        TaskManager.Enqueue(() => P.Memory.RidePillion(master.BattleChara(), 10));
            //        TaskManager.Enqueue(() => Svc.Condition[ConditionFlag.Mounted]);
            //        return;
            //    }
            //}

            // Mount:
            // mount
            if (master.Character()->IsMounted() && CanMount())
            {
                movement.Enabled = false;
                ActionManager.Instance()->UseAction(ActionType.GeneralAction, 9);
                return;
            }

            // fly
            if (Config.MountAndFly && ((Structs.Character*)master.Address)->IsFlying != 0 && !Svc.Condition[ConditionFlag.InFlight] && Svc.Condition[ConditionFlag.Mounted])
            {
                movement.Enabled = false;
                TaskManager.Enqueue(() => ActionManager.Instance()->UseAction(ActionType.GeneralAction, 2));
                TaskManager.EnqueueDelay(50);
                TaskManager.Enqueue(() => ActionManager.Instance()->UseAction(ActionType.GeneralAction, 2));
                return;
            }

            // dismount
            if (!master.Character()->IsMounted() && Svc.Condition[ConditionFlag.Mounted])
            {
                movement.Enabled = false;
                ActionManager.Instance()->UseAction(ActionType.GeneralAction, 23);
                return;
            }
        }

        if (Player.DistanceTo(master) <= Config.DistanceToKeep) { movement.Enabled = false; return; }

        movement.Enabled = true;
        movement.DesiredPosition = master.Position;
    }

    private static bool CanMount() => !Svc.Condition[ConditionFlag.Mounted] && !Svc.Condition[ConditionFlag.Mounting] && !Svc.Condition[ConditionFlag.InCombat] && !Svc.Condition[ConditionFlag.Casting];

    private void OnChatMessage(XivChatType type, int timestamp, ref SeString sender, ref SeString message, ref bool isHandled)
    {
        if (type != XivChatType.Party) return;
        var player = sender.Payloads.SingleOrDefault(x => x is PlayerPayload) as PlayerPayload;
        if (message.TextValue.ToLowerInvariant().Contains("autofollow"))
        {
            if (int.TryParse(message.TextValue.Split("autofollow")[1], out var distance))
                Config.DistanceToKeep = distance;
            else if (message.TextValue.ToLowerInvariant().Contains("autofollow off"))
                ClearMaster();
            else
            {
                foreach (var actor in Svc.Objects)
                {
                    if (actor == null) continue;
                    if (actor.Name.TextValue.Equals(player?.PlayerName))
                    {
                        Svc.Targets.Target = actor;
                        SetMaster();
                    }
                }
            }
        }
    }
}
