using Dalamud.Bindings.ImGui;
using ECommons.ImGuiMethods;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using FFXIVClientStructs.FFXIV.Component.Exd;

namespace ComplexTweaks.UI.Debug.Tabs;

internal unsafe class TestTab : DebugTab {
    public override void Draw() {
        var x = *((byte*)InfoProxyNoviceNetwork.Instance() + 0x18);
        ImGuiEx.Text($"nn: {x} {x:B8}");
        ImGuiEx.Text($"{Svc.PluginInterface.InternalName}: {Svc.PluginInterface.GetPluginConfigDirectory()}");

        if (ImGui.Button($"compress"))
            ImGui.SetClipboardText(ImGui.GetClipboardText().ToBase64());
        if (ImGui.Button($"decompress"))
            ImGui.SetClipboardText(ImGui.GetClipboardText().FromBase64());

        if (ImGui.Button("logout"))
            AgentLobby.Instance()->HandleLogout(false, 60);

        if (ImGui.Button("meld"))
            ActionManager.Instance()->UseAction(ActionType.GeneralAction, 12);

        ImGui.Text($"{ExdModule.GetRoleForClassJobId(Player.JobId)}");
    }
}
