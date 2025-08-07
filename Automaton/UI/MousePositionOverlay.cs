using Automaton.Features;
using Dalamud.Interface.Windowing;
using ECommons.ImGuiMethods;
using ECommons.SimpleGui;
using Dalamud.Bindings.ImGui;

namespace Automaton.UI;
public class MousePositionOverlay : Window
{
    public MousePositionOverlay() : base("Hyperborea Overlay", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.AlwaysUseWindowPadding, true)
    {
        Position = Vector2.Zero;
        PositionCondition = ImGuiCond.Always;
        Size = ImGuiHelpers.MainViewport.Size;
        SizeCondition = ImGuiCond.Always;
        EzConfigGui.WindowSystem.AddWindow(this);
        IsOpen = true;
        RespectCloseHotkey = false;
    }

    public override void PreDraw() => ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
    public override void PostDraw() => ImGui.PopStyleVar();
    public override bool DrawConditions() => DebugTools.ShowMouseOverlay;

    public override void Draw()
    {
        var pos = ImGui.GetMousePos();
        if (Svc.GameGui.ScreenToWorld(pos, out var res))
        {
            var col = GradientColor.Get(EzColor.RedBright, EzColor.YellowBright);
            DrawRingWorld(res, 0.5f, col.ToUint(), 2f);
            var l = MathF.Sqrt(2f) / 2f * 0.5f;
            DrawLineWorld(res + new Vector3(-l, 0, -l), res + new Vector3(l, 0, l), col.ToUint(), 2f);
            DrawLineWorld(res + new Vector3(l, 0, -l), res + new Vector3(-l, 0, l), col.ToUint(), 2f);
        }
    }

    void DrawLineWorld(Vector3 a, Vector3 b, uint color, float thickness)
    {
        var result = GetAdjustedLine(a, b);
        if (result.posA is null || result.posB is null) return;
        ImGui.GetWindowDrawList().PathLineTo(new Vector2(result.posA.Value.X, result.posA.Value.Y));
        ImGui.GetWindowDrawList().PathLineTo(new Vector2(result.posB.Value.X, result.posB.Value.Y));
        ImGui.GetWindowDrawList().PathStroke(color, ImDrawFlags.None, thickness);
    }

    (Vector2? posA, Vector2? posB) GetAdjustedLine(Vector3 pointA, Vector3 pointB)
    {
        _ = Svc.GameGui.WorldToScreen(pointA, out var posA);
        _ = Svc.GameGui.WorldToScreen(pointB, out var posB);
        //if (!resultA || !resultB) return default;
        return (posA, posB);
    }

    public void DrawRingWorld(Vector3 position, float radius, uint color, float thickness)
    {
        var segments = 50;
        var seg = segments / 2;
        var elements = new Vector2?[segments];
        for (var i = 0; i < segments; i++)
        {
            Svc.GameGui.WorldToScreen(
                new Vector3(position.X + radius * (float)Math.Sin(Math.PI / seg * i),
                position.Y,
                position.Z + radius * (float)Math.Cos(Math.PI / seg * i)
                ),
                out var pos);
            elements[i] = new Vector2(pos.X, pos.Y);
        }
        foreach (var pos in elements)
        {
            if (pos == null) continue;
            ImGui.GetWindowDrawList().PathLineTo(pos.Value);
        }
        ImGui.GetWindowDrawList().PathStroke(color, ImDrawFlags.Closed, thickness);
    }
}
