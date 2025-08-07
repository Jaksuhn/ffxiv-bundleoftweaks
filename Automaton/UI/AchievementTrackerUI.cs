using Automaton.Features;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using ECommons.ImGuiMethods;
using Dalamud.Bindings.ImGui;
using SheetAchievement = Lumina.Excel.Sheets.Achievement;

namespace Automaton.UI;
public unsafe class AchievementTrackerUI(AchievementTracker tweak) : Window($"Achievement Tracker##{nameof(AchievementTrackerUI)}")
{
    private readonly AchievementTracker _tweak = tweak;
    private SheetAchievement? selectedAchievement;
    internal static string Search = string.Empty;
    private DateTime lastCallTime;

    public override bool DrawConditions() => Player.Available;

    public override void Draw()
    {
        try
        {
            DrawAchievementSearch();

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            DrawTracker();
        }
        catch (Exception e)
        {
            Svc.Log.Error(e.ToString());
        }
    }

    private void DrawAchievementSearch()
    {
        var timeSinceLastCall = DateTime.Now - lastCallTime;

        if (timeSinceLastCall.TotalSeconds >= _tweak.Config.UpdateFrequency)
        {
            _tweak.RequestUpdate();
            lastCallTime = DateTime.Now;
        }
        var preview = selectedAchievement is null ? string.Empty : $"{selectedAchievement?.Name}";

        ImGuiEx.TextV($"Select Achievement");
        ImGui.SameLine(120f.Scale());

        ImGuiEx.SetNextItemFullWidth();
        using var combo = ImRaii.Combo("###AchievementSelect", preview);
        if (!combo) return;
        ImGui.Text("Search");
        ImGui.SameLine();
        ImGui.InputText("###AchievementSearch", ref Search, 100);

        if (ImGui.Selectable(string.Empty, selectedAchievement == null))
        {
            selectedAchievement = null;
        }

        foreach (var achv in GetSheet<SheetAchievement>().Where(x => !x.Name.ToString().IsNullOrEmpty() && x.Name.ToString().Contains(Search, StringComparison.CurrentCultureIgnoreCase)))
        {
            ImGui.PushID($"###achievement{achv.RowId}");
            var selected = ImGui.Selectable($"{achv.Name}", achv.RowId == selectedAchievement?.RowId);

            if (selected)
            {
                _tweak.Config.Achievements.Add(new AchievementTracker.Achv { ID = achv.RowId, Name = achv.Name.ToString(), Description = GetRow<SheetAchievement>(achv.RowId)!.Value.Description.ToString(), Points = GetRow<SheetAchievement>(achv.RowId)!.Value.Points });
                _tweak.RequestUpdate(achv.RowId);
            }

            ImGui.PopID();
        }
    }

    private void DrawTracker()
    {
        try
        {
            foreach (var a in _tweak.Config.Achievements.ToList().Select((x, i) => new { Achievement = x, Index = i }))
            {
                if (_tweak.Config.AutoRemoveCompleted && a.Achievement.Completed)
                {
                    _tweak.Config.Achievements.Remove(a.Achievement);
                    continue;
                }

                ImGui.Columns(2);

                if (ImGuiEx.IconButton(FontAwesomeIcon.ArrowUp, $"{a.Achievement.ID}", enabled: a.Index != 0))
                    (_tweak.Config.Achievements[a.Index], _tweak.Config.Achievements[a.Index - 1]) = (_tweak.Config.Achievements[a.Index - 1], _tweak.Config.Achievements[a.Index]);
                ImGui.SameLine();
                if (ImGuiEx.IconButton(FontAwesomeIcon.ArrowDown, $"{a.Achievement.ID}", enabled: a.Index != _tweak.Config.Achievements.Count - 1))
                    (_tweak.Config.Achievements[a.Index], _tweak.Config.Achievements[a.Index + 1]) = (_tweak.Config.Achievements[a.Index + 1], _tweak.Config.Achievements[a.Index]);

                ImGui.SameLine();
                ImGuiEx.TextV($"[{a.Achievement.ID}] {a.Achievement.Name}");
                if (ImGui.IsItemHovered()) ImGui.SetTooltip($"[{a.Achievement.Points}pts] {a.Achievement.Description}");

                ImGui.NextColumn();
                ImGuiX.DrawProgressBar((int)a.Achievement.CurrentProgress, (int)a.Achievement.MaxProgress, _tweak.Config.BarColour);
                ImGui.SameLine();
                ImGui.SetCursorPosX(ImGui.GetContentRegionMax().X - ImGuiX.IconUnitWidth() - ImGui.GetStyle().WindowPadding.X);
                if (ImGuiComponents.IconButton((int)a.Achievement.ID, FontAwesomeIcon.Trash))
                {
                    _tweak.Config.Achievements.Remove(a.Achievement);
                }
                ImGui.Columns(1);
            }
        }
        catch (Exception e) { e.Log(); }
    }
}
