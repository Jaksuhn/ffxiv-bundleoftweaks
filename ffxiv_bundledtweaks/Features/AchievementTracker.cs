using ComplexTweaks.UI;
using ECommons.SimpleGui;
using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace ComplexTweaks.Features;

public class AchievementTrackerConfiguration
{
    public List<AchievementTracker.Achv> Achievements = [];
    [ColorConfig] public Vector4 BarColour = Vector4.One;
    [IntConfig(DefaultValue = 60, SameLine = true)] public int UpdateFrequency = 60;
    [BoolConfig] public bool AutoRemoveCompleted = false;
}

[Tweak]
public unsafe class AchievementTracker : Tweak<AchievementTrackerConfiguration>
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
        EzConfigGui.WindowSystem.AddWindow(new AchievementTrackerUI(this));
    }

    public override void Disable()
    {
        AchievementProgress.ReceiveAchievementProgressHook.Disable();
        Events.AchievementProgressUpdate -= OnAchievementProgressUpdate;
        EzConfigGui.RemoveWindow<AchievementTrackerUI>();
    }

    [CommandHandler("/atracker", "Toggle the Achievement Tracker window")]
    private void OnCommand(string command, string arguments) => EzConfigGui.GetWindow<AchievementTrackerUI>()!.IsOpen ^= true;

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
