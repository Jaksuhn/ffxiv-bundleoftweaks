using ComplexTweaks.UI.Debug.Tabs;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;

namespace ComplexTweaks.UI;

internal class DebugWindow : Window {
    public DebugWindow() : base($"{Name} - Debug v{P.Version.ToString(2)}###{nameof(DebugWindow)}") {
        SizeConstraints = new WindowSizeConstraints {
            MinimumSize = new Vector2(375, 330),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };
        Tabs = [.. typeof(DebugWindow).Assembly.GetTypes()
            .Where(t => typeof(IDebugTab).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface)
            .Select(t => (IDebugTab)Activator.CreateInstance(t)!)];
    }

    private const uint SidebarWidth = 250;
    private readonly IDebugTab[] Tabs;
    private IDrawableTab? SelectedTab;

    //public override bool DrawConditions() => C.ShowDebug;

    public override void Draw() {
        DrawSidebar();
        ImGui.SameLine();
        DrawTab();
    }

    private void DrawSidebar() {
        using var child = ImRaii.Child("Sidebar", new Vector2(SidebarWidth * ImGui.GetIO().FontGlobalScale, -1), true, ImGuiWindowFlags.NoSavedSettings);
        if (!child || !child.Success) return;

        using var table = ImRaii.Table("SidebarTable", 1, ImGuiTableFlags.NoSavedSettings);
        if (!table || !table.Success) return;

        ImGui.TableSetupColumn("Tab Name", ImGuiTableColumnFlags.WidthStretch);

        foreach (var tab in Tabs) {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();

            using var disabled = ImRaii.Disabled(!tab.IsEnabled);
            if (ImGui.Selectable($"{tab.Title}###Selectable_{tab.InternalName}", SelectedTab == tab))
                SelectedTab = tab;
        }
    }

    private void DrawTab() {
        if (SelectedTab == null) {
            ImGui.Dummy(Vector2.Zero);
            return;
        }

        if (SelectedTab.DrawInChild) {
            using var child = ImRaii.Child($"###{SelectedTab.InternalName}_Child", new Vector2(-1), true);
            if (SelectedTab.DrawConditions())
                TryExecute(SelectedTab.Draw);
            return;
        }

        if (SelectedTab.DrawConditions())
            TryExecute(SelectedTab.Draw);
    }
}
