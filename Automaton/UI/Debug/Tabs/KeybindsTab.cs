using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using Dalamud.Bindings.ImGui;
using Lumina.Excel.Sheets;

namespace Automaton.UI.Debug.Tabs;
internal unsafe class KeybindsTab : DebugTab
{
    private IEnumerable<ConfigKey>? _configKeys;
    public override void Draw()
    {
        using var table = ImRaii.Table("Keybinds", 4, ImGuiTableFlags.SizingFixedFit);
        if (!table) return;

        _configKeys ??= GetSheet<ConfigKey>().Where(r => !r.Label.IsEmpty);

        ImGui.TableNextColumn();
        ImGui.TextUnformatted("Keybind");
        ImGui.TableNextColumn();
        ImGui.TextUnformatted("Text Code");
        ImGui.TableNextColumn();
        ImGui.TextUnformatted("VirtualKey");
        ImGui.TableNextColumn();
        ImGui.TextUnformatted("Active");

        foreach (var row in _configKeys)
        {
            var keybind = new UIInputData.Keybind();
            var keyName = Utf8String.FromString(row.Label.ToString());
            var inputData = UIInputData.Instance();
            inputData->GetKeybind(keyName, &keybind);
            List<List<nint>?> availableKeys = [Utils.GetKeysToPress(keybind.Key, keybind.Modifier), Utils.GetKeysToPress(keybind.AltKey, keybind.AltModifier)];
            var realKeys = availableKeys.Where(x => x != null).Select(x => x!).MinBy(x => x.Count);

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(row.Text.ExtractText());
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(row.Label.ExtractText());
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(realKeys == null ? "None" : string.Join(", ", realKeys));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted($"{(realKeys == null ? string.Empty : string.Join(", ", realKeys.Where(i => Svc.KeyState.IsVirtualKeyValid((int)i)).Select(i => Svc.KeyState.GetRawValue((int)i))))}");
        }
    }
}
