using clib.Ui;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;

namespace ComplexTweaks.Tweaks;

public partial class MultiboxTools {
    private readonly Dictionary<int, List<GearsetInfo>> _gearsets = [];

    private static string GearsetPopupId(ClientSnapshot peer) => $"##gearsets{peer.SessionId}";

    private void OpenGearsetMenu(ClientSnapshot peer, bool self) {
        if (!self)
            Dispatch(new GearsetRequestMessage(peer.SessionId, _sessionId));
        ImGui.OpenPopup(GearsetPopupId(peer));
    }

    private void DrawGearsetMenu(ClientSnapshot peer, bool self) {
        using var popup = ImRaii.Popup(GearsetPopupId(peer));
        if (!popup)
            return;

        List<GearsetInfo>? gearsets;
        if (self)
            gearsets = ReadGearsets();
        else
            lock (_lock)
                gearsets = _gearsets.GetValueOrDefault(peer.SessionId);

        if (gearsets is null) {
            ImGui.TextColoredWrapped(Colors.Grey, "Loading gearsets...");
            return;
        }

        if (gearsets.Count == 0) {
            ImGui.TextColoredWrapped(Colors.Grey, "No gearsets.");
            return;
        }

        using var table = ImRaii.Table("##gearsetTable", 4, ImGuiTableFlags.SizingFixedFit);
        if (!table)
            return;

        var iconSize = new Vector2(ImGui.GetTextLineHeight());
        foreach (var gearset in gearsets) {
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            if (ImGui.Selectable($"{gearset.Index + 1}##gearset{gearset.Index}", false, ImGuiSelectableFlags.SpanAllColumns))
                Dispatch(new EquipGearsetMessage(peer.SessionId, gearset.Index, gearset.JobId));

            ImGui.TableNextColumn();
            ImGui.Icon(62000u + gearset.JobId, iconSize);

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(gearset.Name);

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(gearset.ItemLevel.ToString());
        }
    }

    private static unsafe List<GearsetInfo> ReadGearsets() {
        var module = RaptureGearsetModule.Instance();
        if (module == null)
            return [];

        var gearsets = new List<GearsetInfo>();
        for (var i = 0; i < 100; i++) {
            if (!module->IsValidGearset(i))
                continue;

            var entry = module->GetGearset(i);
            if (entry == null || !entry->Flags.HasFlag(RaptureGearsetModule.GearsetFlag.Exists))
                continue;

            gearsets.Add(new((byte)i, entry->ClassJob, entry->NameString, entry->ItemLevel));
        }
        return gearsets;
    }

    private unsafe void EquipGearset(byte index, byte jobId) {
        var module = RaptureGearsetModule.Instance();
        if (module == null)
            return;

        var entry = module->IsValidGearset(index) ? module->GetGearset(index) : null;
        if (entry == null || entry->ClassJob != jobId) {
            Warning($"Gearset {index + 1} changed since it was selected, not equipping");
            ModuleMessage($"Gearset {index + 1} has changed, reopen the gearset menu and try again.");
            return;
        }

        module->EquipGearset(index);
        Log($"Equipped gearset {index + 1}");
    }
}
