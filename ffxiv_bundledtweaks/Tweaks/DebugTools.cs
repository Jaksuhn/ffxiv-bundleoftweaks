using ComplexTweaks.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Game.Gui.Toast;
using ECommons;
using ECommons.Interop;
using ECommons.SimpleGui;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.Interop;
using Lumina.Excel.Sheets;

namespace ComplexTweaks.Tweaks;

public class DebugToolsConfiguration {
    [BoolConfig] public bool AutoVoidIslandRest = false;
    [BoolConfig] public bool EnableTPClick = false;
    [BoolConfig] public bool EnableNoClip = false;

    [FloatConfig(DependsOn = nameof(EnableNoClip), DefaultValue = 0.05f)]
    public float NoClipSpeed = 0.05f;

    [BoolConfig] public bool EnableMoveSpeed = false;
    [BoolConfig] public bool EnableDirectActions = false;
    [BoolConfig] public bool EnableTPMarker = false;
    [BoolConfig] public bool EnableTPOffset = false;
    [BoolConfig] public bool EnableTPAbsolute = false;
}

[Tweak(true)]
public class DebugTools : Tweak<DebugToolsConfiguration> {
    public override string Name => "Debug Tools";
    public override string Description => "Debug tools for use in hyperborea/firewall";

    public override void Enable() {
        _keys = GetSheet<ConfigKey>().Where(x => x.RowId is >= 12 and <= 18).ToDictionary(x => x.Label.ToString(), x => x);
        Svc.Framework.Update += OnUpdate;
        Svc.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "MJICraftSchedule", OnSetup);
        Svc.ClientState.EnterPvP += OnEnterPvP;
    }

    public override void Disable() {
        Svc.Framework.Update -= OnUpdate;
        Svc.AddonLifecycle.UnregisterListener(OnSetup);
        Svc.ClientState.EnterPvP -= OnEnterPvP;
    }

    private unsafe void OnSetup(AddonEvent type, AddonArgs args) {
        if (!Config.AutoVoidIslandRest) return;
        if (AgentMJICraftSchedule.Instance()->Data->RestCycles.ToHex() != 8321u) {
            Svc.Log.Debug($"Setting rest: {8321u:X}");
            AgentMJICraftSchedule.Instance()->Data->NewRestCycles = 8321u;
            var eventData = stackalloc int[] { 0, 0, 0 };
            var atkvalues = new Span<AtkValue>([new() { Type = AtkValueType.Int, Int = 0 }]);
            AgentMJICraftSchedule.Instance()->AgentInterface.ReceiveEvent((AtkValue*)eventData, atkvalues.GetPointer(0), (uint)atkvalues.Length, 5); // 5 = eventKind
        }
    }

    [CommandHandler("/tpclick", "Teleport to your mouse location on click while CTRL is held.", nameof(Config.EnableTPClick))]
    private void OnTeleportClick(string command, string arguments) {
        tpActive ^= true;
        if (tpActive)
            EzConfigGui.WindowSystem.AddWindow(new MousePositionOverlay());
        else
            EzConfigGui.RemoveWindow<MousePositionOverlay>();
        Svc.Toasts.ShowNormal($"TPClick {(tpActive ? "Enabled" : "Disabled")}", new ToastOptions() { Speed = ToastSpeed.Fast });
    }

    [CommandHandler("/noclip", "Enable NoClip", nameof(Config.EnableNoClip))]
    private void OnNoClip(string command, string arguments) {
        if (Player.IsInPvP) return;
        ncActive ^= true;
        Config.NoClipSpeed = float.TryParse(arguments, out var speed) ? speed : Config.NoClipSpeed;
    }

    [CommandHandler(["/move", "/speed"], "Modify your movement speed", nameof(Config.EnableMoveSpeed))]
    private void OnMoveSpeed(string command, string arguments) {
        if (Player.IsInPvP) return;
        Player.Speed = float.TryParse(arguments, out var speed) ? speed : 1.0f;
    }

    // prevent entering pvp with debug options enabled
    private void OnEnterPvP() {
        Player.Speed = 1.0f;
        tpActive = false;
        ncActive = false;
    }

    class MovementKeys {
        public const string Forward = "MOVE_FORE";
        public const string Backward = "MOVE_BACK";
        public const string Left = "MOVE_LEFT";
        public const string Right = "MOVE_RIGHT";
        public const string Strife_L = "MOVE_STRIFE_L";
        public const string Strife_R = "MOVE_STRIFE_R";
        public const string Jump = "JUMP";
        public static ConfigKey JumpKey => (ConfigKey)GetRow<ConfigKey>(18)!;
        public static ConfigKey ForwardKey => (ConfigKey)GetRow<ConfigKey>(12)!;
    }

    public static bool ShowMouseOverlay;
    private bool IsLButtonPressed;
    private bool tpActive;
    private bool ncActive;
    private Dictionary<string, ConfigKey> _keys = null!;
    private unsafe void OnUpdate(IFramework framework) {
        if (!Player.Available || IsOccupied()) return;

        ShowMouseOverlay = false;
        if (Config.EnableTPClick && tpActive) {
            if (!Framework.Instance()->WindowInactive && IsKeyPressed([LimitedKeys.LeftControlKey, LimitedKeys.RightControlKey]) && Utils.IsClickingInGameWorld()) {
                ShowMouseOverlay = true;
                var pos = ImGui.GetMousePos();
                if (Svc.GameGui.ScreenToWorld(pos, out var res)) {
                    if (IsKeyPressed(LimitedKeys.LeftMouseButton)) {
                        if (!IsLButtonPressed)
                            PlayerEx.Position = res;
                        IsLButtonPressed = true;
                    }
                    else
                        IsLButtonPressed = false;
                }
            }
        }

        if (Config.EnableNoClip && ncActive && !Framework.Instance()->WindowInactive) {
            if (_keys["JUMP"].IsHeldRaw())
                PlayerEx.Position = (Player.Object.Position.X, Player.Object.Position.Y + Config.NoClipSpeed, Player.Object.Position.Z).ToVector3();
            if (Svc.KeyState.GetRawValue(VirtualKey.LSHIFT) != 0 || IsKeyPressed(LimitedKeys.LeftShiftKey))
                PlayerEx.Position = (Player.Object.Position.X, Player.Object.Position.Y - Config.NoClipSpeed, Player.Object.Position.Z).ToVector3();
            if (_keys["MOVE_FORE"].IsHeldRaw())
                PlayerEx.Position = Utils.RotatePoint(Player.Object.Position.X, Player.Object.Position.Z, MathF.PI - Player.Camera->DirH, Player.Object.Position + new Vector3(0, 0, Config.NoClipSpeed));
            if (_keys["MOVE_BACK"].IsHeldRaw())
                PlayerEx.Position = Utils.RotatePoint(Player.Object.Position.X, Player.Object.Position.Z, MathF.PI - Player.Camera->DirH, Player.Object.Position + new Vector3(0, 0, -Config.NoClipSpeed));
            if (_keys["MOVE_LEFT"].IsHeldRaw() || _keys["MOVE_STRIFE_L"].IsHeldRaw())
                PlayerEx.Position = Utils.RotatePoint(Player.Object.Position.X, Player.Object.Position.Z, MathF.PI - Player.Camera->DirH, Player.Object.Position + new Vector3(Config.NoClipSpeed, 0, 0));
            if (_keys["MOVE_RIGHT"].IsHeldRaw() || _keys["MOVE_STRIFE_R"].IsHeldRaw())
                PlayerEx.Position = Utils.RotatePoint(Player.Object.Position.X, Player.Object.Position.Z, MathF.PI - Player.Camera->DirH, Player.Object.Position + new Vector3(-Config.NoClipSpeed, 0, 0));
        }
    }

    [CommandHandler("/ada", "Call actions directly.", nameof(Config.EnableDirectActions))]
    private unsafe void OnDirectAction(string command, string arguments) {
        if (Player.IsInPvP) return;
        try {
            var args = arguments.Split(' ');
            var actionType = ParseActionType(args[0]);
            var actionID = uint.Parse(args[1]);
            ActionManager.Instance()->UseActionLocation(actionType, actionID);
        }
        catch (Exception e) { e.Log(); }
    }

    private static ActionType ParseActionType(string input) {
        if (Enum.TryParse(input, true, out ActionType result))
            return result;

        if (byte.TryParse(input, out var intValue))
            if (Enum.IsDefined(typeof(ActionType), intValue))
                return (ActionType)intValue;

        throw new ArgumentException("Invalid ActionType", nameof(input));
    }

    [CommandHandler("/tpmarker", "Teleport to a given marker", nameof(Config.EnableTPMarker))]
    private unsafe void OnTeleportMarker(string command, string arguments) {
        if (Player.IsInPvP) return;
        if (int.TryParse(arguments, out var i)) {
            var m = MarkingController.Instance()->FieldMarkers[i];
            Vector3? pos = m.Active ? new(m.X / 1000.0f, m.Y / 1000.0f, m.Z / 1000.0f) : null;
            if (pos != null)
                PlayerEx.Position = (Vector3)pos;
        }
    }

    [CommandHandler("/tpoff", "Teleport from your current position, offset by arguments", nameof(Config.EnableTPOffset))]
    private unsafe void OnTeleportOffset(string command, string arguments) {
        if (Player.IsInPvP) return;
        if (arguments.TryParseVector3(out var v))
            PlayerEx.Position += v;
    }

    [CommandHandler("/tpabs", "Teleport to a given absolute position", nameof(Config.EnableTPAbsolute))]
    private unsafe void OnTeleportAbsolute(string command, string arguments) {
        if (Player.IsInPvP) return;
        if (arguments.TryParseVector3(out var v))
            PlayerEx.Position = v;
    }
}
