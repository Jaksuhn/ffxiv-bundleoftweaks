using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Dalamud.Bindings.ImGui;
using Lumina.Excel.Sheets;

namespace ComplexTweaks.UI.Debug.Tabs;
public unsafe class RaptureGearsetModuleTab : DebugTab
{
    public override void Draw()
    {
        var gsm = RaptureGearsetModule.Instance();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        for (var i = 0; i < 100; i++)
        {
            if (!gsm->IsValidGearset(i)) continue;

            var gearset = gsm->GetGearset(i);

            using var titleColor = ImRaii.PushColor(ImGuiCol.Text, 0xFF00FFFF);
            using var node = ImRaii.TreeNode($"##Gearset{i}", ImGuiTreeNodeFlags.SpanAvailWidth);

            ImGui.SameLine(ImGui.GetStyle().FramePadding.X * 3f + ImGui.GetFontSize(), 0);
            ImGui.TextUnformatted($"Gearset #{i}: {gearset->NameString} [{GetRow<ClassJob>(gearset->ClassJob)?.Abbreviation} | {gearset->GlamourSetLink} | {gearset->ItemLevel} | {gearset->BannerIndex}]");
            ImGui.SameLine(0, ImGui.GetStyle().FramePadding.X * 3);

            ImGui.NewLine();

            if (!node) continue;
            titleColor?.Dispose();

            if (ImGui.SmallButton($"Equip##G{i}"))
                gsm->EquipGearset(i);

            using var table = ImRaii.Table("RaptureHotbarModuleTable", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.NoSavedSettings);
            if (!table) return;

            ImGui.TableSetupColumn("Index", ImGuiTableColumnFlags.WidthFixed, 60);
            ImGui.TableSetupColumn("Id", ImGuiTableColumnFlags.WidthFixed, 60);
            ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch, 60);
            ImGui.TableSetupScrollFreeze(3, 1);
            ImGui.TableHeadersRow();

            for (var j = 0; j < gearset->Items.Length; j++)
            {
                var item = gearset->GetItem((RaptureGearsetModule.GearsetItemIndex)j);
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(j.ToString());

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(item.ItemId.ToString());

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(GetRow<Item>(item.ItemId)?.Name.ToString());
            }
        }
    }
}
