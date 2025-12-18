using Dalamud.Bindings.ImGui;
using ECommons.ImGuiMethods;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
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

        if (ImGui.Button("leave"))
            EventFramework.LeaveCurrentContent(true);

        if (ImGui.Button("meld"))
            ActionManager.Instance()->UseAction(ActionType.GeneralAction, 12);

        if (ImGui.Button("dismount")) {
            unsafe {
                var pos = Player.Position;
                GameMain.ExecuteLocationCommand((int)LocationCommandFlag.Dismount, &pos, (int)Svc.Objects.LocalPlayer.PackedRotation);
            }
        }

        if (ImGui.Button("dismount 2"))
            GameMain.ExecuteCommand(CommandFlag.Dismount.Value, 1);

        ImGui.Text($"{ExdModule.GetRoleForClassJobId(Player.ClassJob.RowId)}");
    }
}
