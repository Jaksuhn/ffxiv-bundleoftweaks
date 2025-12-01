using ComplexTweaks.Tasks;
using Dalamud.Bindings.ImGui;
using ECommons.ImGuiMethods;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using System.Text.RegularExpressions;

namespace ComplexTweaks.UI.Debug.Tabs;

internal class QuestsTab : DebugTab {
    private uint _questId;
    public override void Draw() {
        if (ImGuiEx.ExcelSheetCombo("##Foods", out Quest i, _ => $"[{_questId}] {GetRow<Quest>(_questId)?.Name}", x => $"[{x.RowId}] {x.Name}", x => !x.Name.IsEmpty)) {
            _questId = i.RowId;
        }

        if (ImGui.Button("copy uniques")) {
            List<string> strings = [];
            foreach (var q in GetSheet<Quest>().Where(q => !q.Name.IsEmpty))
                foreach (var p in q.QuestParams.Where(p => !p.ScriptInstruction.IsEmpty))
                    strings.Add(Regex.Replace(p.ScriptInstruction.ToString(), "[0-9]", ""));
            ImGui.SetClipboardText($"{string.Join("\n", strings.Distinct().OrderBy(x => x))}");
        }

        if (GetRow<Quest>(_questId) is { } row) {
            if (ImGui.Button("go to quest start"))
                Service.Automation.Start(new DoQuest(row));
            ImGui.Text($"IssuerLocation: {row.IssuerLocation.ToStringExtended()}");
            foreach (var todo in row.TodoParams)
                foreach (var loc in todo.ToDoLocation)
                    if (loc.IsValid)
                        ImGui.Text($"#{todo.ToDoCompleteSeq} [{todo.ToDoQty}/{todo.CountableNum}] {loc.ToStringExtended()}");
        }
    }
}

public static class QuestRowExtensions {
    public static string ToStringExtended(this RowRef<Level> row) => row.IsValid ? $"loc: [{row.Value.Territory.ToStringExtended()}/{row.Value.Map.RowId} {row.Value.X}/{row.Value.Y}/{row.Value.Z}] obj: [#{row.Value.Object.RowId} t={row.Value.Type} e={row.Value.EventId} r={row.Value.Radius}]"
        : "null";
    public static string ToStringExtended(this RowRef<TerritoryType> row) => $"#{row.Value.RowId} {row.Value.PlaceName.Value.Name}";
}
