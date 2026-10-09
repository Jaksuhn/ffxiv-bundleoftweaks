using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Common.Configuration;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace ComplexTweaks.Tweaks;

public unsafe partial class LiveThemeChange : Tweak {
    public override string Name => "Live Theme Change";
    public override string Description => "Applies colour theme changes immediately, without needing to re-log. Why did the devs not just do this?";

    [AddressHook<ConfigEntry>(nameof(ConfigEntry.MemberFunctionPointers.SetValueUInt))]
    private bool SetValueUInt(ConfigEntry* entry, uint value, uint save) {
        if (!SetValueUIntHook.Original(entry, value, save))
            return false;
        var module = RaptureAtkModule.Instance();
        if (module == null) return true;
        var themeEntry = Framework.Instance()->SystemConfig.GetConfigOption(ConfigOption.ColorThemeType);
        if (themeEntry == entry)
            SetActiveColorThemeTypeHook.Original(&module->AtkUIColorHolder, (byte)value);
        return true;
    }

    // TODO: replace with CS
    [SigHook("E8 ?? ?? ?? ?? B0 ?? C6 83 ?? ?? ?? ?? ?? 48 8B 5C 24 ?? 48 8B 74 24 ?? 48 83 C4 ?? 5F C3 80 BB")]
    private void SetActiveColorThemeType(AtkUIColorHolder* holder, byte themeType) => SetActiveColorThemeTypeHook.Original(holder, themeType);
}
