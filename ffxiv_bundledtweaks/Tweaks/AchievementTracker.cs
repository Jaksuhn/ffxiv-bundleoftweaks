using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using ECommons.ImGuiMethods;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using SheetAchievement = Lumina.Excel.Sheets.Achievement;

namespace ComplexTweaks.Tweaks;

public class AchievementTrackerConfiguration
{
    public List<AchievementTracker.Achv> Achievements = [];
    [ColorConfig] public Vector4 BarColour = Vector4.One;
    [IntConfig(DefaultValue = 60, SameLine = true)] public int UpdateFrequency = 60;
    [BoolConfig] public bool AutoRemoveCompleted = false;
}

[Tweak]
public unsafe class AchievementTracker : Tweak<AchievementTrackerConfiguration, AchievementTrackerWindow>
{
    public override string Name => "Achievement Tracker";
    public override string Description => $"Adds an achievement tracker";

    public class Achv
    {
        public uint ID;
        public required string Name;
        public uint CurrentProgress;
        public uint MaxProgress;
        public string Description = string.Empty;
        public byte Points = 0;
        public bool Completed => CurrentProgress != default && CurrentProgress >= MaxProgress;
    }

    private readonly Memory.AchievementProgress AchievementProgress = new();
    public override void Enable()
    {
        AchievementProgress.ReceiveAchievementProgressHook.Enable();
        Events.AchievementProgressUpdate += OnAchievementProgressUpdate;
    }

    public override void Disable()
    {
        AchievementProgress.ReceiveAchievementProgressHook.Disable();
        Events.AchievementProgressUpdate -= OnAchievementProgressUpdate;
    }

    [CommandHandler("/atracker", "Toggle the Achievement Tracker window")]
    private void OnCommand(string command, string arguments) => Window<Window>()?.Toggle();

    private void OnAchievementProgressUpdate(uint id, uint current, uint max)
    {
        foreach (var achv in Config.Achievements)
        {
            if (achv.ID == id)
            {
                achv.CurrentProgress = current;
                achv.MaxProgress = max;
            }
        }
    }

    public void RequestUpdate(uint id = 0)
    {
        if (id == 0)
            Config.Achievements.Where(a => !a.Completed).ToList().ForEach(achv => Achievement.Instance()->RequestAchievementProgress(achv.ID));
        else
            Achievement.Instance()->RequestAchievementProgress(id);
    }
}

public unsafe class AchievementTrackerWindow(AchievementTracker tweak) : Window($"Achievement Tracker##{nameof(AchievementTrackerWindow)}")
{
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

        if (timeSinceLastCall.TotalSeconds >= tweak.Config.UpdateFrequency)
        {
            tweak.RequestUpdate();
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
            selectedAchievement = null;

        foreach (var achv in GetSheet<SheetAchievement>().Where(x => !x.Name.ToString().IsNullOrEmpty() && x.Name.ToString().Contains(Search, StringComparison.CurrentCultureIgnoreCase)))
        {
            using var _ = ImRaii.PushId($"###achievement{achv.RowId}");
            var selected = ImGui.Selectable($"{achv.Name}", achv.RowId == selectedAchievement?.RowId);

            if (selected)
            {
                tweak.Config.Achievements.Add(new AchievementTracker.Achv { ID = achv.RowId, Name = achv.Name.ToString(), Description = GetRow<SheetAchievement>(achv.RowId)!.Value.Description.ToString(), Points = GetRow<SheetAchievement>(achv.RowId)!.Value.Points });
                tweak.RequestUpdate(achv.RowId);
            }
        }
    }

    private void DrawTracker()
    {
        try
        {
            foreach (var a in tweak.Config.Achievements.ToList().Select((x, i) => new { Achievement = x, Index = i }))
            {
                if (tweak.Config.AutoRemoveCompleted && a.Achievement.Completed)
                {
                    tweak.Config.Achievements.Remove(a.Achievement);
                    continue;
                }

                ImGui.Columns(2);

                if (ImGuiEx.IconButton(FontAwesomeIcon.ArrowUp, $"{a.Achievement.ID}", enabled: a.Index != 0))
                    (tweak.Config.Achievements[a.Index], tweak.Config.Achievements[a.Index - 1]) = (tweak.Config.Achievements[a.Index - 1], tweak.Config.Achievements[a.Index]);
                ImGui.SameLine();
                if (ImGuiEx.IconButton(FontAwesomeIcon.ArrowDown, $"{a.Achievement.ID}", enabled: a.Index != tweak.Config.Achievements.Count - 1))
                    (tweak.Config.Achievements[a.Index], tweak.Config.Achievements[a.Index + 1]) = (tweak.Config.Achievements[a.Index + 1], tweak.Config.Achievements[a.Index]);

                ImGui.SameLine();
                ImGuiEx.TextV($"[{a.Achievement.ID}] {a.Achievement.Name}");
                if (ImGui.IsItemHovered()) ImGui.SetTooltip($"[{a.Achievement.Points}pts] {a.Achievement.Description}");

                ImGui.NextColumn();
                ImGui.DrawProgressBar((int)a.Achievement.CurrentProgress, (int)a.Achievement.MaxProgress, tweak.Config.BarColour);
                ImGui.SameLine();
                ImGui.SetCursorPosX(ImGui.GetContentRegionMax().X - ImGui.IconUnitWidth() - ImGui.GetStyle().WindowPadding.X);
                if (ImGuiComponents.IconButton((int)a.Achievement.ID, FontAwesomeIcon.Trash))
                {
                    tweak.Config.Achievements.Remove(a.Achievement);
                }
                ImGui.Columns(1);
            }
        }
        catch (Exception e) { e.Log(); }
    }
}
