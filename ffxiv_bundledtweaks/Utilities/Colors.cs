using ECommons.ImGuiMethods;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace ComplexTweaks.Utilities;

public static class Colors
{
    public static EzColor Gold { get; } = new(0.847f, 0.733f, 0.49f);
    public static EzColor Grey { get; } = new(0.73f, 0.73f, 0.73f);
    public static EzColor Grey2 { get; } = new(0.87f, 0.87f, 0.87f);
    public static EzColor Grey3 { get; } = new(0.6f, 0.6f, 0.6f);
    public static EzColor Grey4 { get; } = new(0.3f, 0.3f, 0.3f);
    public static EzColor Type { get; } = new(0.2f, 0.9f, 0.9f);
    public static EzColor Field { get; } = new(0.2f, 0.9f, 0.4f);

    public static unsafe bool IsLightTheme
        => RaptureAtkModule.Instance()->AtkUIColorHolder.ActiveColorThemeType == 1;
}
