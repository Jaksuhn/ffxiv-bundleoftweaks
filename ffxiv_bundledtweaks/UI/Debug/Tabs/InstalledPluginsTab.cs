using ECommons.ImGuiMethods;
using Dalamud.Bindings.ImGui;

namespace ComplexTweaks.UI.Debug.Tabs;

internal class InstalledPluginsTab : DebugTab {
    public override void Draw() {
        foreach (var plugin in Svc.PluginInterface.InstalledPlugins) {
            ImGui.TextUnformatted($"[{plugin.InternalName}] {plugin.Name} <{plugin.Version}>");
            ImGui.SameLine();
            ImGui.TextColored(plugin.IsLoaded ? EzColor.GreenBright : EzColor.RedBright, "Loaded");
        }
    }
}
