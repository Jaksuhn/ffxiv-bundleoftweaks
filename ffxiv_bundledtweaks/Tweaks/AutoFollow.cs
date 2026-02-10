using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using ECommons;
using ECommons.EzHookManager;
using ECommons.GameFunctions;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.MJI;
using System.Diagnostics.CodeAnalysis;

namespace ComplexTweaks.Tweaks;

public class AutoFollowConfiguration {
    [IntConfig(DefaultValue = 3)] public int DistanceToKeep = 3;
    [IntConfig] public int DisableIfFurtherThan;
    [BoolConfig] public bool OnlyInDuty;
    [BoolConfig] public bool ExcludeCombat;
    [StringConfig] public string AutoFollowName = string.Empty;
}

[Tweak]
public unsafe class AutoFollow : Tweak<AutoFollowConfiguration> {
    public override string Name => "Auto Follow";
    public override string Description
        => "True Auto Follow. Trigger with command while targeting someone. Use it with no target to wipe the current master.\n" +
        "If multiboxing, you can send \"autofollow\" to chat and anyone in the party with this feature enabled will follow.\n" +
        "You can also add a number argument to specify the distance to keep, or add the off argument to clear the current master.";

    private OverrideMovement movement = null!;
    private MasterRef _master;
    private delegate void FlyDelegate(nint gameObject);
    private readonly FlyDelegate Fly = EzDelegate.Get<FlyDelegate>("E8 ?? ?? ?? ?? 40 84 F6 74 ?? 8D 43"); // 7.41hf1 incase I take three years to get back to this

    [CommandHandler("/autofollow", "Enable AutoFollow")]
    internal void OnCommand(string command, string arguments) {
        if (!arguments.IsNullOrEmpty()) {
            if (Svc.Objects.FirstOrDefault(o => o.Name.TextValue.ToLowerInvariant().Contains(arguments, StringComparison.InvariantCultureIgnoreCase)) is { } obj) {
                _master = MasterRef.FromObject(obj);
                Svc.Toasts.ShowNormal($"Auto following {obj.Name}");
                return;
            }
            else {
                _master = new MasterRef(null, arguments);
                return;
            }
        }
        if (Svc.Targets.Target != null)
            SetMaster();
        else
            ClearMaster();
    }

    public override void Enable() {
        Svc.Framework.Update += Follow;
        Svc.Chat.ChatMessage += OnChatMessage;
        movement = new();
    }

    public override void Disable() {
        Svc.Framework.Update -= Follow;
        Svc.Chat.ChatMessage -= OnChatMessage;
        movement.Dispose();
    }

    private void SetMaster() {
        try {
            if (Svc.Targets.Target is { Name.TextValue: var name } target) {
                _master = MasterRef.FromObject(target);
                Svc.Toasts.ShowNormal($"Auto following {name}");
            }
            else {
                _master = default;
                Svc.Toasts.ShowNormal("Auto following off");
            }
        }
        catch { }
    }

    private void ClearMaster() {
        _master = default;
        movement.Enabled = false;
        Svc.Toasts.ShowNormal("Auto following off");
    }

    private void Follow(IFramework framework) {
        if (!Player.Available) return;
        if (!Svc.Condition[ConditionFlag.InFlight] && TaskManager.IsBusy) return; // want to abort, not return, if in flight
        if (_master.IsEmpty && Config.AutoFollowName.IsNullOrEmpty()) return;

        if (!TryGetMaster(out var master)) {
            movement.Enabled = false;
            return;
        }

        if (ShouldStopForConfig(master)) {
            movement.Enabled = false;
            return;
        }

        if (Svc.Condition[ConditionFlag.InFlight]) {
            TaskManager.Abort();
        }

        if (Svc.Condition[ConditionFlag.RidingPillion]) return;

        if (master.ObjectKind == ObjectKind.Player) {
            if (TrySprint(master)) return;
            if (TryPillion(master)) return;
            if (TryMount(master)) return;
            if (TryFly(master)) return;
            if (TryDismount(master)) return;
        }

        if (Player.DistanceTo(master) <= Config.DistanceToKeep) {
            movement.Enabled = false;
            return;
        }

        movement.Enabled = true;
        movement.DesiredPosition = master.Position;
    }

    private bool TryGetMaster([NotNullWhen(true)] out IGameObject? master) {
        master = Svc.Objects.FirstOrDefault(x => !_master.IsEmpty && _master.Matches(x)
            || !Config.AutoFollowName.IsNullOrEmpty() && x.Name.TextValue.EqualsIgnoreCase(Config.AutoFollowName));
        return master != null;
    }

    private bool ShouldStopForConfig(IGameObject master) {
        if (Config.DisableIfFurtherThan > 0 && Player.DistanceTo(master) >= Config.DisableIfFurtherThan)
            return true;

        if (Config.OnlyInDuty && !Player.IsInDuty)
            return true;

        if (Config.ExcludeCombat && Svc.Condition[ConditionFlag.InCombat])
            return true;

        return false;
    }

    private bool TrySprint(DGameObject master) {
        if (master is IBattleChara { StatusList: var status } && status.Any(s => s.StatusId is 50)) {
            if (MJIManager.Instance()->IsPlayerInSanctuary && Player.Status.None(s => s.StatusId is 50)) {
                return ActionManager.Instance()->UseAction(ActionType.Action, 31314);
            }
            else {
                if (Player.Status.None(s => s.StatusId is 50))
                    return ActionManager.Instance()->UseAction(ActionType.GeneralAction, 4);
            }
        }
        return false;
    }

    private bool TryPillion(IGameObject master) {
        if (!Svc.Party.Any(p => p.EntityId == master.GameObjectId) || !master.CanRidePillion())
            return false;

        if (Player.DistanceTo(master) > 3) {
            movement.Enabled = true;
            movement.DesiredPosition = master.Position;
            return true;
        }

        movement.Enabled = false;
        if (Svc.Condition[ConditionFlag.Mounted]) {
            ActionManager.Instance()->UseAction(ActionType.GeneralAction, 23);
            return true;
        }

        TaskManager.Enqueue(() => {
            Svc.Log.Debug("Detected mounted party member with extra seats, mounting...");
            GameMain.ExecuteCommand(CommandFlag.RidePillion.Value, (int)master.EntityId, 10);
        });
        TaskManager.Enqueue(() => Svc.Condition[ConditionFlag.Mounted]);
        return true;
    }

    private bool TryMount(IGameObject master) {
        if (!master.Character()->IsMounted() || !CanMount())
            return false;

        movement.Enabled = false;
        ActionManager.Instance()->UseAction(ActionType.GeneralAction, 9);
        return true;
    }

    private bool TryFly(IGameObject master) {
        if (master.Character()->MovementState is not MovementStateOptions.Flying || !CanFly())
            return false;

        movement.Enabled = false;
        TaskManager.Enqueue(() => ActionManager.Instance()->UseAction(ActionType.GeneralAction, 2));
        TaskManager.EnqueueDelay(50);
        TaskManager.Enqueue(() => ActionManager.Instance()->UseAction(ActionType.GeneralAction, 2));

        // TODO: find a way to incorporate this. Need to jump and trigger at the apex or something
        //Fly((nint)Player.GameObject);
        return true;
    }

    private bool TryDismount(IGameObject master) {
        if (master.Character()->IsMounted() || !Svc.Condition[ConditionFlag.Mounted])
            return false;

        movement.Enabled = false;
        ActionManager.Instance()->UseAction(ActionType.GeneralAction, 23);
        return true;
    }

    private static bool CanMount() => !Svc.Condition[ConditionFlag.Mounted] && !Svc.Condition[ConditionFlag.Mounting] && !Svc.Condition[ConditionFlag.InCombat] && !Svc.Condition[ConditionFlag.Casting];
    private static bool CanFly() => Control.CanFly && !Svc.Condition[ConditionFlag.InFlight];

    private readonly record struct MasterRef(uint? Id, string? Name) {
        public bool IsEmpty => Id is null && string.IsNullOrEmpty(Name);

        public static MasterRef FromObject(IGameObject obj)
            => new(obj.EntityId, obj.Name.TextValue);

        public bool Matches(IGameObject obj)
            => Id is not null && obj.EntityId == Id || !string.IsNullOrEmpty(Name) && obj.Name.TextValue.EqualsIgnoreCase(Name);
    }

    private void OnChatMessage(XivChatType type, int timestamp, ref SeString sender, ref SeString message, ref bool isHandled) {
        if (type != XivChatType.Party) return;
        var player = sender.Payloads.SingleOrDefault(x => x is PlayerPayload) as PlayerPayload;
        if (message.TextValue.ContainsIgnoreCase("autofollow")) {
            if (int.TryParse(message.TextValue.Split("autofollow")[1], out var distance))
                Config.DistanceToKeep = distance;
            else if (message.TextValue.ContainsIgnoreCase("autofollow off"))
                ClearMaster();
            else {
                if (Svc.Objects.FirstOrDefault(o => o.Name.TextValue.Equals(player?.PlayerName)) is { } actor) {
                    Svc.Targets.Target = actor;
                    SetMaster();
                }
            }
        }
    }
}
