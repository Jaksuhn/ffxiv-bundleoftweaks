using Dalamud.Bindings.ImGui;

namespace ComplexTweaks.UI.Debug.Tabs;

internal unsafe class PlayerExTab : DebugTab {
    public override void Draw() {
        //var pi = typeof(PlayerEx).GetProperties();
        //foreach (var p in pi)
        //{
        //    try
        //    {
        //        ImGui.TextColored(new Vector4(0.2f, 0.6f, 0.4f, 1), $"{p.Name}: ");
        //        ImGui.SameLine();
        //        ImGui.TextDisabled($"{p.GetValue(typeof(PlayerEx))}");
        //    }
        //    catch (Exception e)
        //    {
        //        ImGui.TextColored(new Vector4(1, 0, 0, 1), $"[ERROR] {e.Message}");
        //    }
        //}

        var playerExType = typeof(PlayerExtensions);
        var properties = playerExType.GetProperties();

        for (var i = 0; i < properties.Length; i++) {
            var p = properties[i];
            var getMethod = p.GetGetMethod();

            try {
                ImGui.TextColored(new Vector4(0.2f, 0.6f, 0.4f, 1), $"{p.Name}: ");
                ImGui.SameLine();
                ImGui.TextDisabled($"{getMethod?.Invoke(null, null)}");
            }
            catch (Exception e) {
                ImGui.TextColored(new Vector4(1, 0, 0, 1), $"[ERROR] {e.Message}");
            }
        }
    }
}
