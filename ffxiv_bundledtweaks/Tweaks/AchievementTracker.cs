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
        public string Category = string.Empty;
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
            Config.Achievements.Where(a => !a.Completed).ForEach(achv => Achievement.Instance()->RequestAchievementProgress(achv.ID));
        else
            Achievement.Instance()->RequestAchievementProgress(id);
    }
}

public class AchievementTrackerWindow(AchievementTracker tweak) : Window($"Achievement Tracker##{nameof(AchievementTrackerWindow)}") {
    private Sheets.Achievement? selectedAchievement;
    private string _search = string.Empty;
    private DateTime _lastCall;

    public override bool DrawConditions() => Player.Available;

    public override void Draw() {
        TryExecute(() => {
            DrawSearch();
            ImGui.SpacedSeparator();
            DrawAchievements();
        });
    }

    private void DrawSearch() {
        var timeSinceLastCall = DateTime.Now - _lastCall;

        if (timeSinceLastCall.TotalSeconds >= tweak.Config.UpdateFrequency) {
            tweak.RequestUpdate();
            _lastCall = DateTime.Now;
        }

        ImGuiEx.SetNextItemFullWidth();
        var preview = selectedAchievement is null ? "Add an achievement" : $"{selectedAchievement?.Name}";
        using var combo = ImRaii.Combo("###AchievementSelect", preview);
        if (!combo) return;

        ImGui.Text("Search");
        ImGui.SameLine();
        ImGui.InputText("###AchievementSearch", ref _search, 100);

        if (ImGui.Selectable("(None)", selectedAchievement == null))
            selectedAchievement = null;

        foreach (var achv in GetSheet<Sheets.Achievement>().Where(x => !x.Name.ToString().IsNullOrEmpty() && x.Name.ToString().Contains(_search, StringComparison.CurrentCultureIgnoreCase))) {
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
                _search = string.Empty;
            }
        }
    }

    private void DrawAchievements() {
        try {
            if (tweak.Config.Achievements.Count == 0)
                return;

            var categories = tweak.Config.Achievements.ToList()
                .Select(a => a.Category)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(c => c, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            if (categories.Count == 0) {
                DrawAchievementsList(tweak.Config.Achievements);
                return;
            }

            using var tabBar = ImRaii.TabBar("AchievementCategories");
            if (!tabBar)
                return;

            if (tweak.Config.Achievements.Where(a => string.IsNullOrWhiteSpace(a.Category)).ToList() is { Count: > 0 } misc) {
                using var miscTab = ImRaii.TabItem("Misc");
                if (miscTab)
                    DrawAchievementsList(misc);
            }

            foreach (var category in categories) {
                using var tab = ImRaii.TabItem(category);
                if (!tab)
                    continue;

                DrawAchievementsList([.. tweak.Config.Achievements.Where(a => category.EqualsIgnoreCase(a.Category))]);
            }
        }
        catch (Exception e) { e.Log(); }
    }

    private void DrawAchievementsList(List<AchievementTracker.Achv> achievements) {
        foreach (var (achv, i) in achievements.WithIndex()) {
            if (tweak.Config.AutoRemoveCompleted && achv.Completed) {
                tweak.Config.Achievements.Remove(achv);
                continue;
            }

            var originalIndex = tweak.Config.Achievements.IndexOf(achv);
            if (originalIndex < 0)
                continue;

            using var id = ImRaii.PushId($"achv_{achv.ID}_{originalIndex}");

            var availableWidth = ImGui.GetContentRegionAvail().X;
            var nameWidth = Math.Min(200f.Scale(), availableWidth * 0.4f);
            var progressWidth = availableWidth - nameWidth - ImGui.GetStyle().ItemSpacing.X;

            using (var buttonStyle = ImRaii.PushStyle(ImGuiStyleVar.ButtonTextAlign, new Vector2(0, 0.5f)))
            using (var color = ImRaii.PushColor(ImGuiCol.Button, 0)
                .Push(ImGuiCol.ButtonHovered, ImGui.GetColorU32(ImGuiCol.ButtonHovered))
                .Push(ImGuiCol.ButtonActive, ImGui.GetColorU32(ImGuiCol.ButtonActive))) {
                ImGui.Button($"[{achv.ID}] {achv.Name}", new Vector2(nameWidth, 0)); // prevent window drag

                if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                    ImGui.OpenPopup($"AchvContext##{achv.ID}");

                ImGui.DragDropSource(originalIndex, "ACHIEVEMENT_ITEM"u8, $"[{achv.ID}] {achv.Name}");
                ImGui.DragDropTarget(originalIndex, "ACHIEVEMENT_ITEM"u8, tweak.Config.Achievements.Count, (sourceIndex, insertIndex) => {
                    var dragged = tweak.Config.Achievements[sourceIndex];
                    tweak.Config.Achievements.RemoveAt(sourceIndex);
                    if (sourceIndex < insertIndex) // shift left if removed before the insert point
                        insertIndex--;
                    tweak.Config.Achievements.Insert(insertIndex, dragged);
                });

                ImGui.TooltipOnHover($"[{achv.Points}pts] {achv.Description}\nDrag to reorder\nRight-click for options");
            }

            DrawContextMenu(achv, originalIndex);

            ImGui.SameLine();

            var percentage = achv.MaxProgress > 0 ? (float)achv.CurrentProgress / achv.MaxProgress : 0f;
            var progressLabel = $"{percentage:P0} ({achv.CurrentProgress}/{achv.MaxProgress})";

            var cursorPos = ImGui.GetCursorPos();
            var labelSize = ImGui.CalcTextSize(progressLabel);
            var textX = progressWidth - labelSize.X - 4f;

            var backgroundColor = ImGui.GetStyle().Colors[(int)ImGuiCol.FrameBg];
            var textColor = ImGui.GetProgressBarTextColor(tweak.Config.BarColour, backgroundColor, percentage, textX, labelSize.X, progressWidth);

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

    private void DrawContextMenu(AchievementTracker.Achv achv, int index) {
        using var popup = ImRaii.Popup($"AchvContext##{achv.ID}");
        if (!popup)
            return;

        if (ImGui.Selectable("Remove")) {
            tweak.Config.Achievements.Remove(achv);
            return;
        }

        ImGui.Separator();
        ImGui.TextV("Category");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(150f.Scale());

        var category = achv.Category;
        if (ImGui.InputText("##CategoryInput", ref category, 64, ImGuiInputTextFlags.EnterReturnsTrue)) {
            achv.Category = category.Trim();
        }

        var existingCategories = tweak.Config.Achievements
            .Select(a => a.Category)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(c => c, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (existingCategories.Count > 0) {
            ImGui.Spacing();
            ImGui.Text("Existing categories");
            foreach (var existing in existingCategories) {
                if (ImGui.Selectable(existing, existing.Equals(achv.Category, StringComparison.CurrentCultureIgnoreCase))) {
                    achv.Category = existing;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(achv.Category)) {
            ImGui.Spacing();
            if (ImGui.Selectable("Clear category")) {
                tweak.Config.Achievements
                    .Where(a => !string.IsNullOrWhiteSpace(a.Category) && category.EqualsIgnoreCase(a.Category))
                    .ForEach(a => a.Category = string.Empty);
            }
        }
    }
}
