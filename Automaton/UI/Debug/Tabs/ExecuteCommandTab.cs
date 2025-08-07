using Dalamud.Interface.Utility.Raii;
using ECommons.ImGuiMethods;
using Dalamud.Bindings.ImGui;

namespace Automaton.UI.Debug.Tabs;
internal unsafe class ExecuteCommandTab : DebugTab
{
    private readonly Memory.ExecuteCommands ec = new();
    private ExecuteCommandFlag flag;
    private ExecuteCommandComplexFlag flag2;
    private readonly int[] ecParams = new int[4];
    private readonly int[] eccParams = new int[4];

    public override void Draw()
    {
        ImGuiEx.EnumCombo("ExecuteCommand", ref flag);
        ImGui.InputInt("p1", ref ecParams[0]);
        ImGui.InputInt("p2", ref ecParams[1]);
        ImGui.InputInt("p3", ref ecParams[2]);
        ImGui.InputInt("p4", ref ecParams[3]);
        if (ImGui.Button("execute"))
            ec.ExecuteCommand(flag, ecParams[0], ecParams[1], ecParams[2], ecParams[3]);

        using var id = ImRaii.PushId("complex");
        ImGuiEx.EnumCombo("ExecuteCommandComplex", ref flag2);
        ImGui.InputInt("p1", ref eccParams[0]);
        ImGui.InputInt("p2", ref eccParams[1]);
        ImGui.InputInt("p3", ref eccParams[2]);
        ImGui.InputInt("p4", ref eccParams[3]);
        if (ImGui.Button("execute"))
            ec.ExecuteCommandComplexLocation(flag2, Player.Position, eccParams[0], eccParams[1], eccParams[2], eccParams[3]);
    }
}
