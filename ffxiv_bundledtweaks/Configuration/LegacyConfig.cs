using clib.Configuration;
using ComplexTweaks.Tweaks;
using Dalamud.Configuration;
using System.Collections.ObjectModel;

namespace ComplexTweaks.Configuration;

internal sealed class ConfigVersionProbe {
    public int Version { get; set; }
}

internal sealed class LegacyConfig : IPluginConfiguration {
    public int Version { get; set; }
    public ObservableCollection<string> EnabledTweaks = [];
    public LegacyTweakConfigs Tweaks = new();
#pragma warning disable CS0649
    public bool ShowDebug;
#pragma warning restore CS0649
}

internal sealed class LegacyTweakConfigs {
    public AchievementTrackerConfiguration AchievementTrackerConfiguration { get; init; } = new();
    public AutoFollowConfiguration AutoFollow { get; init; } = new();
    public AutoInviteConfiguration AutoInvite { get; init; } = new();
    public ARQuestingConfiguration ARQuestingConfiguration { get; init; } = new();
    public ClickToMoveConfiguration ClickToMove { get; init; } = new();
    public CommandsConfiguration Commands { get; init; } = new();
    public EnhancedDutyStartEndConfiguration EnhancedDutyStartEnd { get; init; } = new();
    public EnhancedLoginLogoutConfig EnhancedLoginLogout { get; init; } = new();
    public FateToolKitConfig FateToolKit { get; init; } = new();
    public GMAlertConfiguration GMAlert { get; init; } = new();
    public HuntRelayHelperConfiguration HuntRelayHelper { get; init; } = new();
    public SimpleCurrencyAlertConfig SimpleCurrencyAlertConfig { get; init; } = new();
}

internal sealed class V3 : IConfigMigration<LegacyConfig> {
    public int TargetVersion => 3;

    public void Migrate(LegacyConfig config) {
        var oldType = config.Tweaks.HuntRelayHelper.Types[0];
        if (oldType.TypeHeuristics == @"s rank, (?:^|\W)[sS](?:$|\W)")
            config.Tweaks.HuntRelayHelper.Types[0] = (oldType.RelayType, oldType.TypeFormat, @"s rank, rank s, /(?:^|\W)[sS](?:$|\W)/");
        config.Tweaks.HuntRelayHelper.Types.Insert(1, (HuntRelayHelper.RelayTypes.Minions, "Minions", @"ssminion, /\bminions?\b/"));
    }
}

internal sealed class V4 : IConfigMigration<LegacyConfig> {
    public int TargetVersion => 4;

    public void Migrate(LegacyConfig config) {
        var oldType = config.Tweaks.HuntRelayHelper.Types[0];
        if (oldType.TypeHeuristics == @"s rank, rank s, /(?:^|\W)[sS](?:$|\W)/")
            config.Tweaks.HuntRelayHelper.Types[0] = (oldType.RelayType, oldType.TypeFormat, @"s rank, rank s, /(?:^|\W)(?<!')[sS](?:$|\W)/");
    }
}

internal sealed class V5 : IConfigShapeMigration<LegacyConfig, Config> {
    public int TargetVersion => Config.CURRENT_CONFIG_VERSION;

    public Config Migrate(LegacyConfig from)
        => new() {
            Version = Config.CURRENT_CONFIG_VERSION,
            EnabledTweaks = from.EnabledTweaks,
            ShowDebug = from.ShowDebug,
            Tweaks = new Dictionary<string, object>(StringComparer.Ordinal) {
                [nameof(AchievementTracker)] = from.Tweaks.AchievementTrackerConfiguration,
                [nameof(AutoFollow)] = from.Tweaks.AutoFollow,
                [nameof(AutoInvite)] = from.Tweaks.AutoInvite,
                [nameof(ARQuesting)] = from.Tweaks.ARQuestingConfiguration,
                [nameof(ClickToMove)] = from.Tweaks.ClickToMove,
                [nameof(Commands)] = from.Tweaks.Commands,
                [nameof(EnhancedDutyStartEnd)] = from.Tweaks.EnhancedDutyStartEnd,
                [nameof(EnhancedLoginLogout)] = from.Tweaks.EnhancedLoginLogout,
                [nameof(FateToolKit)] = from.Tweaks.FateToolKit,
                [nameof(GMAlert)] = from.Tweaks.GMAlert,
                [nameof(HuntRelayHelper)] = from.Tweaks.HuntRelayHelper,
                [nameof(SimpleCurrencyAlert)] = from.Tweaks.SimpleCurrencyAlertConfig,
            },
        };
}
