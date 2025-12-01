using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using ECommons.ImGuiMethods;
using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace ComplexTweaks.Tweaks;

public class AchievementTrackerConfiguration {
    public List<AchievementTracker.Achv> Achievements = [];
    [ColorConfig] public Vector4 BarColour = Vector4.One;
    [IntConfig(DefaultValue = 60, SameLine = true)] public int UpdateFrequency = 60;
    [BoolConfig] public bool AutoRemoveCompleted = false;
}

[Tweak]
public unsafe partial class AchievementTracker : Tweak<AchievementTrackerConfiguration, AchievementTrackerWindow> {
    public override string Name => "Achievement Tracker";
    public override string Description => $"Adds an achievement tracker";

    public class Achv {
        public uint ID;
        public required string Name;
        public uint CurrentProgress;
        public uint MaxProgress;
        public string Description = string.Empty;
        public byte Points = 0;
        public bool Completed => CurrentProgress != default && CurrentProgress >= MaxProgress;
    }

    [CommandHandler("/atracker", "Toggle the Achievement Tracker window")]
    private void OnCommand(string command, string arguments) => Window<Window>()?.Toggle();

    [AddressHook<Achievement>(nameof(Achievement.MemberFunctionPointers.ReceiveAchievementProgress))]
    private void ReceiveAchievementProgress(Achievement* achievement, uint id, uint current, uint max) {
        try {
            foreach (var achv in Config.Achievements) {
                if (achv.ID == id) {
                    achv.CurrentProgress = current;
                    achv.MaxProgress = max;
                }
            }
        }
        catch (Exception e) {
            Error(e, $"Error receiving achievement progress");
        }

        ReceiveAchievementProgressHook.Original(achievement, id, current, max);
    }

    public void RequestUpdate(uint id = 0) {
        if (id == 0)
            Config.Achievements.Where(a => !a.Completed).ToList().ForEach(achv => Achievement.Instance()->RequestAchievementProgress(achv.ID));
        else
            Achievement.Instance()->RequestAchievementProgress(id);
    }
}

public unsafe class AchievementTrackerWindow(AchievementTracker tweak) : Window($"Achievement Tracker##{nameof(AchievementTrackerWindow)}") {
    private Sheets.Achievement? selectedAchievement;
    internal static string Search = string.Empty;
    private DateTime lastCallTime;

    public override bool DrawConditions() => Player.Available;

    public override void Draw() {
        TryExecute(() => {
            DrawSearch();
            ImGui.SpacedSeparator();
            DrawAchievements();
        });
    }

    private void DrawSearch() {
        var timeSinceLastCall = DateTime.Now - lastCallTime;

        if (timeSinceLastCall.TotalSeconds >= tweak.Config.UpdateFrequency) {
            tweak.RequestUpdate();
            lastCallTime = DateTime.Now;
        }

        ImGuiEx.SetNextItemFullWidth();
        var preview = selectedAchievement is null ? "Add an achievement" : $"{selectedAchievement?.Name}";
        using var combo = ImRaii.Combo("###AchievementSelect", preview);
        if (!combo) return;

        ImGui.Text("Search");
        ImGui.SameLine();
        ImGui.InputText("###AchievementSearch", ref Search, 100);

        if (ImGui.Selectable("(None)", selectedAchievement == null))
            selectedAchievement = null;

        foreach (var achv in GetSheet<Sheets.Achievement>().Where(x => !x.Name.ToString().IsNullOrEmpty() && x.Name.ToString().Contains(Search, StringComparison.CurrentCultureIgnoreCase))) {
            using var _ = ImRaii.PushId($"###achievement{achv.RowId}");
            var selected = ImGui.Selectable($"{achv.Name}", achv.RowId == selectedAchievement?.RowId);

            if (selected) {
                if (!tweak.Config.Achievements.Any(a => a.ID == achv.RowId)) {
                    tweak.Config.Achievements.Add(new AchievementTracker.Achv {
                        ID = achv.RowId,
                        Name = achv.Name.ToString(),
                        Description = GetRow<Sheets.Achievement>(achv.RowId)!.Value.Description.ToString(),
                        Points = GetRow<Sheets.Achievement>(achv.RowId)!.Value.Points
                    });
                    tweak.RequestUpdate(achv.RowId);
                }
                selectedAchievement = null;
                Search = string.Empty;
            }
        }
    }

    private void DrawAchievements() {
        try {
            if (tweak.Config.Achievements.Count == 0)
                return;

            var achievements = tweak.Config.Achievements.ToList();
            var style = ImGui.GetStyle();

            for (var i = 0; i < achievements.Count; i++) {
                var achv = achievements[i];

                if (tweak.Config.AutoRemoveCompleted && achv.Completed) {
                    tweak.Config.Achievements.Remove(achv);
                    continue;
                }

                using var id = ImRaii.PushId($"achv_{achv.ID}_{i}");

                var availableWidth = ImGui.GetContentRegionAvail().X;
                var nameWidth = Math.Min(200f.Scale(), availableWidth * 0.4f);
                var progressWidth = availableWidth - nameWidth - style.ItemSpacing.X;

                using (var buttonStyle = ImRaii.PushStyle(ImGuiStyleVar.ButtonTextAlign, new Vector2(0, 0.5f)))
                using (var color = ImRaii.PushColor(ImGuiCol.Button, 0)
                    .Push(ImGuiCol.ButtonHovered, ImGui.GetColorU32(ImGuiCol.ButtonHovered))
                    .Push(ImGuiCol.ButtonActive, ImGui.GetColorU32(ImGuiCol.ButtonActive))) {
                    ImGui.Button($"[{achv.ID}] {achv.Name}", new Vector2(nameWidth, 0)); // prevent window drag

                    if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                        tweak.Config.Achievements.Remove(achv);

                    ImGui.DragDropSource(i, "ACHIEVEMENT_ITEM"u8, $"[{achv.ID}] {achv.Name}");
                    ImGui.DragDropTarget(i, "ACHIEVEMENT_ITEM"u8, tweak.Config.Achievements.Count, (sourceIndex, insertIndex) => {
                        var dragged = tweak.Config.Achievements[sourceIndex];
                        tweak.Config.Achievements.RemoveAt(sourceIndex);
                        if (sourceIndex < insertIndex) // shift left if removed before the insert point
                            insertIndex--;
                        tweak.Config.Achievements.Insert(insertIndex, dragged);
                    });

                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip($"[{achv.Points}pts] {achv.Description}\nDrag to reorder\nRight-click to remove");
                }

                ImGui.SameLine();

                var percentage = achv.MaxProgress > 0 ? (float)achv.CurrentProgress / achv.MaxProgress : 0f;
                var progressLabel = $"{percentage:P0} ({achv.CurrentProgress}/{achv.MaxProgress})";

                var cursorPos = ImGui.GetCursorPos();
                var barWidth = progressWidth;
                var labelSize = ImGui.CalcTextSize(progressLabel);
                var textX = progressWidth - labelSize.X - 4f;

                var backgroundColor = style.Colors[(int)ImGuiCol.FrameBg];
                var textColor = ImGui.GetProgressBarTextColor(tweak.Config.BarColour, backgroundColor, percentage, textX, labelSize.X, barWidth);

                using (var color = ImRaii.PushColor(ImGuiCol.PlotHistogram, tweak.Config.BarColour))
                    ImGui.ProgressBar(percentage, new Vector2(progressWidth, ImGui.GetFrameHeight()), "");

                ImGui.SetCursorPos(new Vector2(cursorPos.X + textX, cursorPos.Y + (ImGui.GetFrameHeight() - labelSize.Y) * 0.5f));
                ImGui.TextColored(textColor, progressLabel);

                if (i < achievements.Count - 1) {
                    ImGui.Spacing();
                    ImGui.Separator();
                }
            }
        }
        catch (Exception e) { e.Log(); }
    }
}
