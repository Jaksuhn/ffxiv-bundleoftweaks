using ComplexTweaks.UI;
using ECommons.SimpleGui;

namespace ComplexTweaks.Features;

public class GlamourSetsTrackerConfiguration
{
    [BoolConfig] public bool ShowOnlyMissing = false;
}

[Tweak]
public class GlamourSets : Tweak<GlamourSetsTrackerConfiguration>
{
    public override string Name => "Glamour Sets Tracker";
    public override string Description => "A tracking window for glamour sets";

    public override void Enable() => EzConfigGui.WindowSystem.AddWindow(new GlamourSetsTrackerUI(this));
    public override void Disable() => EzConfigGui.RemoveWindow<GlamourSetsTrackerUI>();

    [CommandHandler("/glamoursets", "Toggle the Glamour Sets Tracker window")]
    internal void OnCommand(string command, string arguments) => EzConfigGui.GetWindow<GlamourSetsTrackerUI>()?.Toggle();
}
