using Dalamud.Bindings.ImGui;
using Dalamud.Utility;
using ECommons;
using ECommons.MathHelpers;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace ComplexTweaks.Tweaks;

public class ClickToMoveConfiguration {
    [EnumConfig] public MovementType MovementType;
}

[Tweak]
public unsafe class ClickToMove : Tweak<ClickToMoveConfiguration> {
    public override string Name => "Click to Move";
    public override string Description => "Like those other games.";

    private OverrideMovement movement = null!;

    public override void Enable() {
        Svc.Framework.Update += MoveTo;
        movement = new();
        //Svc.AddonLifecycle.RegisterListener(AddonEvent.PostReceiveEvent, "AreaMap", Logger);
    }

    private void Logger(AddonEvent type, AddonArgs args) {
        if (args is AddonReceiveEventArgs { AtkEventType: (byte)AtkEventType.MouseDown } receiveArgs) {
            if (receiveArgs.AtkEventData.As<AtkEventData.AtkMouseData>()->ButtonId != 0) return; // left click only
            if (AgentMap.Instance()->CurrentMapId != AgentMap.Instance()->SelectedMapId) return;
            if (args.GetAddon<AddonAreaMap>()->GetMouseWorldCoords() is { } coords)
                Svc.Navmesh.PathfindAndMoveTo(coords.OnMesh(), Player.CanFly);
        }
    }

    public override void Disable() {
        Svc.Framework.Update -= MoveTo;
        movement.Dispose();
        //Svc.AddonLifecycle.UnregisterListener(Logger);
    }

    private bool isPressed = false;
    private Vector3 destination = Vector3.Zero;
    private void MoveTo(IFramework framework) {
        if (!Player.Available || Player.IsBusy) return;
        if (Player.DistanceTo(destination) < 0.0025f) movement.Enabled = false;

        if (IsKeyPressed(ECommons.Interop.LimitedKeys.LeftMouseButton) && Utils.IsClickingInGameWorld()) {
            if (!isPressed)
                isPressed = true;
        }
        else {
            if (isPressed) {
                isPressed = false;
                if (!Framework.Instance()->WindowInactive) {
                    Svc.GameGui.ScreenToWorld(ImGui.GetIO().MousePos, out var pos, 100000f);
                    if (Config.MovementType == MovementType.Pathfind) {
                        if (!Service.Navmesh.IsRunning())
                            Service.Navmesh.PathfindAndMoveTo(pos, false);
                        else {
                            Service.Navmesh.Stop();
                            Service.Navmesh.PathfindAndMoveTo(pos, false);
                        }
                        return;
                    }
                    movement.Enabled = true;
                    movement.DesiredPosition = destination = pos;
                }
            }
        }
    }
}
