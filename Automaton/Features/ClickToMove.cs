using Automaton.Utilities.Movement;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using Dalamud.Bindings.ImGui;

namespace Automaton.Features;

public class ClickToMoveConfiguration
{
    [EnumConfig] public MovementType MovementType;
}

[Tweak]
public unsafe class ClickToMove : Tweak<ClickToMoveConfiguration>
{
    public override string Name => "Click to Move";
    public override string Description => "Like those other games.";

    private readonly OverrideMovement movement = new();

    public override void Enable() => Svc.Framework.Update += MoveTo;
    public override void Disable() => Svc.Framework.Update -= MoveTo;

    private bool isPressed = false;
    private Vector3 destination = Vector3.Zero;
    private void MoveTo(IFramework framework)
    {
        if (!Player.Available || Player.IsBusy) return;
        if (Player.DistanceTo(destination) < 0.0025f) movement.Enabled = false;

        if (IsKeyPressed(ECommons.Interop.LimitedKeys.LeftMouseButton) && Utils.IsClickingInGameWorld())
        {
            if (!isPressed)
                isPressed = true;
        }
        else
        {
            if (isPressed)
            {
                isPressed = false;
                if (!Framework.Instance()->WindowInactive)
                {
                    Svc.GameGui.ScreenToWorld(ImGui.GetIO().MousePos, out var pos, 100000f);
                    if (Config.MovementType == MovementType.Pathfind)
                    {
                        if (!Service.Navmesh.IsRunning())
                            Service.Navmesh.PathfindAndMoveTo(pos, false);
                        else
                        {
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
