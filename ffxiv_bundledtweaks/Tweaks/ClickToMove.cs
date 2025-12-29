using Dalamud.Bindings.ImGui;
using ECommons;
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
    public override string Description => "Like those other games. Supports clicking on the map.";

    private OverrideMovement movement = null!;

    public override void Enable() {
        movement = new();
        Svc.Framework.Update += MoveTo;
        Svc.AddonLifecycle.RegisterListener(AddonEvent.PostReceiveEvent, "AreaMap", HandleMapClick);
    }

    public override void Disable() {
        movement.Dispose();
        Svc.Framework.Update -= MoveTo;
        Svc.AddonLifecycle.UnregisterListener(HandleMapClick);
    }

    private void HandleMapClick(AddonEvent type, AddonArgs args) {
        if (args is AddonReceiveEventArgs { AtkEventType: (byte)AtkEventType.MouseDown } receiveArgs) {
            if (receiveArgs.AtkEventData.As<AtkEventData.AtkMouseData>()->ButtonId != 0) return; // left click only
            if (AgentMap.Instance()->CurrentMapId != AgentMap.Instance()->SelectedMapId) return;
            if (args.GetAddon<AddonAreaMap>()->GetMouseWorldCoords() is { } coords) {
                if (Config.MovementType is MovementType.Pathfind)
                    Svc.Navmesh.PathfindAndMoveTo(coords.OnMesh(), Player.CanFly);
                else {
                    movement.Enabled = true;
                    movement.DesiredPosition = new(coords.X, Player.Position.Y, coords.Y);
                }
            }
        }
    }

    private bool wasPressed = false;
    private void MoveTo(IFramework framework) {
        if (!Player.Available || Player.IsBusy) return;

        if (Config.MovementType != MovementType.Pathfind && Player.Object.FlatDistanceTo(movement.DesiredPosition) < 0.05f) {
            movement.Enabled = false;
        }

        var isPressed = IsKeyPressed(ECommons.Interop.LimitedKeys.LeftMouseButton) && Utils.IsClickingInGameWorld();
        if (!wasPressed && isPressed)
            wasPressed = true;
        else if (wasPressed && !isPressed) {
            wasPressed = false;
            if (!Framework.Instance()->WindowInactive) {
                Svc.GameGui.ScreenToWorld(ImGui.GetIO().MousePos, out var pos, 100000f);
                if (Config.MovementType == MovementType.Pathfind) {
                    if (Service.Navmesh.IsRunning()) Service.Navmesh.Stop();
                    Service.Navmesh.PathfindAndMoveTo(pos, false);
                }
                else {
                    movement.Enabled = true;
                    movement.DesiredPosition = pos;
                }
            }
        }
    }
}
