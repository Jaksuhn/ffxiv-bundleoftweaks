using Automaton.UI;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Game.Gui.Toast;
using ECommons;
using ECommons.Interop;
using ECommons.SimpleGui;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using Dalamud.Bindings.ImGui;

namespace Automaton.Features;

public class DebugToolsConfiguration
{
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
public class DebugTools : Tweak<DebugToolsConfiguration>
{
    public override string Name => "Debug Tools";
    public override string Description => "Debug tools for use in hyperborea/firewall";

    public override void Enable()
    {
        Svc.Framework.Update += OnUpdate;
        Svc.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "MJICraftSchedule", OnSetup);
        Events.EnteredPvPInstance += OnEnterPvP;
    }

    public override void Disable()
    {
        Svc.Framework.Update -= OnUpdate;
        Svc.AddonLifecycle.UnregisterListener(OnSetup);
        Events.EnteredPvPInstance -= OnEnterPvP;
    }

    private unsafe void OnSetup(AddonEvent type, AddonArgs args)
    {
        if (!Config.AutoVoidIslandRest) return;
        if (Utils.AgentData->RestCycles.ToHex() != 8321u)
            Utils.SetRestCycles(8321u);
    }

    [CommandHandler("/tpclick", "Teleport to your mouse location on click while CTRL is held.", nameof(Config.EnableTPClick))]
    private void OnTeleportClick(string command, string arguments)
    {
        tpActive ^= true;
        if (tpActive)
            EzConfigGui.WindowSystem.AddWindow(new MousePositionOverlay());
        else
            EzConfigGui.RemoveWindow<MousePositionOverlay>();
        Svc.Toasts.ShowNormal($"TPClick {(tpActive ? "Enabled" : "Disabled")}", new ToastOptions() { Speed = ToastSpeed.Fast });
    }

    [CommandHandler("/noclip", "Enable NoClip", nameof(Config.EnableNoClip))]
    private void OnNoClip(string command, string arguments)
    {
        if (Player.IsInPvP) return;
        ncActive ^= true;
        Config.NoClipSpeed = float.TryParse(arguments, out var speed) ? speed : Config.NoClipSpeed;
    }

    [CommandHandler(["/move", "/speed"], "Modify your movement speed", nameof(Config.EnableMoveSpeed))]
    private void OnMoveSpeed(string command, string arguments)
    {
        if (Player.IsInPvP) return;
        PlayerEx.Speed = float.TryParse(arguments, out var speed) ? speed : 1.0f;
    }

    // prevent entering pvp with debug options enabled
    private void OnEnterPvP()
    {
        PlayerEx.Speed = 1.0f;
        tpActive = false;
        ncActive = false;
    }

    class MovementKeys
    {
        public const string Forward = "MOVE_FORE";
        public const string Backward = "MOVE_BACK";
        public const string Left = "MOVE_LEFT";
        public const string Right = "MOVE_RIGHT";
        public const string Strife_L = "MOVE_STRIFE_L";
        public const string Strife_R = "MOVE_STRIFE_R";
        public const string Jump = "JUMP";
    }

    public static bool ShowMouseOverlay;
    private bool IsLButtonPressed;
    private bool tpActive;
    private bool ncActive;
    private unsafe void OnUpdate(IFramework framework)
    {
        if (!Player.Available || IsOccupied()) return;

        ShowMouseOverlay = false;
        if (Config.EnableTPClick && tpActive)
        {
            if (!Framework.Instance()->WindowInactive && IsKeyPressed([LimitedKeys.LeftControlKey, LimitedKeys.RightControlKey]) && Utils.IsClickingInGameWorld())
            {
                ShowMouseOverlay = true;
                var pos = ImGui.GetMousePos();
                if (Svc.GameGui.ScreenToWorld(pos, out var res))
                {
                    if (IsKeyPressed(LimitedKeys.LeftMouseButton))
                    {
                        if (!IsLButtonPressed)
                            PlayerEx.Position = res;
                        IsLButtonPressed = true;
                    }
                    else
                        IsLButtonPressed = false;
                }
            }
        }

        if (Config.EnableNoClip && ncActive && !Framework.Instance()->WindowInactive)
        {
            if (Utils.KeybindIsPressed(MovementKeys.Jump))
            {
                Utils.ResetKeybind(MovementKeys.Jump);
                PlayerEx.Position = (Player.Object.Position.X, Player.Object.Position.Y + Config.NoClipSpeed, Player.Object.Position.Z).ToVector3();
            }
            if (Svc.KeyState.GetRawValue(VirtualKey.LSHIFT) != 0 || IsKeyPressed(LimitedKeys.LeftShiftKey))
            {
                Svc.KeyState.SetRawValue(VirtualKey.LSHIFT, 0);
                PlayerEx.Position = (Player.Object.Position.X, Player.Object.Position.Y - Config.NoClipSpeed, Player.Object.Position.Z).ToVector3();
            }
            if (Utils.KeybindIsPressed(MovementKeys.Forward))
            {
                var newPoint = Utils.RotatePoint(Player.Object.Position.X, Player.Object.Position.Z, MathF.PI - PlayerEx.CameraEx->DirH, Player.Object.Position + new Vector3(0, 0, Config.NoClipSpeed));
                Utils.ResetKeybind(MovementKeys.Forward);
                PlayerEx.Position = newPoint;
            }
            if (Utils.KeybindIsPressed(MovementKeys.Backward))
            {
                var newPoint = Utils.RotatePoint(Player.Object.Position.X, Player.Object.Position.Z, MathF.PI - PlayerEx.CameraEx->DirH, Player.Object.Position + new Vector3(0, 0, -Config.NoClipSpeed));
                Utils.ResetKeybind(MovementKeys.Backward);
                PlayerEx.Position = newPoint;
            }
            if (Utils.KeybindIsPressed(MovementKeys.Left) || Utils.KeybindIsPressed(MovementKeys.Strife_L))
            {
                var newPoint = Utils.RotatePoint(Player.Object.Position.X, Player.Object.Position.Z, MathF.PI - PlayerEx.CameraEx->DirH, Player.Object.Position + new Vector3(Config.NoClipSpeed, 0, 0));
                Utils.ResetKeybind(MovementKeys.Left);
                Utils.ResetKeybind(MovementKeys.Strife_L);
                PlayerEx.Position = newPoint;
            }
            if (Utils.KeybindIsPressed(MovementKeys.Right) || Utils.KeybindIsPressed(MovementKeys.Strife_R))
            {
                var newPoint = Utils.RotatePoint(Player.Object.Position.X, Player.Object.Position.Z, MathF.PI - PlayerEx.CameraEx->DirH, Player.Object.Position + new Vector3(-Config.NoClipSpeed, 0, 0));
                Utils.ResetKeybind(MovementKeys.Right);
                Utils.ResetKeybind(MovementKeys.Strife_R);
                PlayerEx.Position = newPoint;
            }
        }
    }

    [CommandHandler("/ada", "Call actions directly.", nameof(Config.EnableDirectActions))]
    private unsafe void OnDirectAction(string command, string arguments)
    {
        if (Player.IsInPvP) return;
        try
        {
            var args = arguments.Split(' ');
            var actionType = ParseActionType(args[0]);
            var actionID = uint.Parse(args[1]);
            ActionManager.Instance()->UseActionLocation(actionType, actionID);
        }
        catch (Exception e) { e.Log(); }
    }

    private static ActionType ParseActionType(string input)
    {
        if (Enum.TryParse(input, true, out ActionType result))
            return result;

        if (byte.TryParse(input, out var intValue))
            if (Enum.IsDefined(typeof(ActionType), intValue))
                return (ActionType)intValue;

        throw new ArgumentException("Invalid ActionType", nameof(input));
    }

    [CommandHandler("/tpmarker", "Teleport to a given marker", nameof(Config.EnableTPMarker))]
    private unsafe void OnTeleportMarker(string command, string arguments)
    {
        if (Player.IsInPvP) return;
        if (int.TryParse(arguments, out var i))
        {
            var m = MarkingController.Instance()->FieldMarkers[i];
            Vector3? pos = m.Active ? new(m.X / 1000.0f, m.Y / 1000.0f, m.Z / 1000.0f) : null;
            if (pos != null)
                PlayerEx.Position = (Vector3)pos;
        }
    }

    [CommandHandler("/tpoff", "Teleport from your current position, offset by arguments", nameof(Config.EnableTPOffset))]
    private unsafe void OnTeleportOffset(string command, string arguments)
    {
        if (Player.IsInPvP) return;
        if (arguments.TryParseVector3(out var v))
            PlayerEx.Position += v;
    }

    [CommandHandler("/tpabs", "Teleport to a given absolute position", nameof(Config.EnableTPAbsolute))]
    private unsafe void OnTeleportAbsolute(string command, string arguments)
    {
        if (Player.IsInPvP) return;
        if (arguments.TryParseVector3(out var v))
            PlayerEx.Position = v;
    }
}
