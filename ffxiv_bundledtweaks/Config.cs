using ComplexTweaks.Tweaks;
using ECommons.Configuration;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using YamlDotNet.Serialization;

namespace ComplexTweaks.Configuration;

public class Config {
    [JsonIgnore]
    public const int CURRENT_CONFIG_VERSION = 4;

    public int Version = CURRENT_CONFIG_VERSION;
    public ObservableCollection<string> EnabledTweaks = [];
    public TweakConfigs Tweaks = new();
    public bool ShowDebug;
}

public class TweakConfigs {
    public AchievementTrackerConfiguration AchievementTrackerConfiguration { get; init; } = new();
    public AutoFollowConfiguration AutoFollow { get; init; } = new();
    public AutoInviteConfiguration AutoInvite { get; init; } = new();
    public ClickToMoveConfiguration ClickToMove { get; init; } = new();
    public CommandsConfiguration Commands { get; init; } = new();
    public DateWithDestinyConfiguration DateWithDestiny { get; init; } = new();
    public DebugToolsConfiguration DebugTools { get; init; } = new();
    public EnhancedDutyStartEndConfiguration EnhancedDutyStartEnd { get; init; } = new();
    public EnhancedLoginLogoutConfig EnhancedLoginLogout { get; init; } = new();
    public FateToolKitConfig FateToolKit { get; init; } = new();
    public GlamourSetsTrackerConfiguration GlamourSets { get; init; } = new();
    public GMAlertConfiguration GMAlert { get; init; } = new();
    public HuntRelayHelperConfiguration HuntRelayHelper { get; init; } = new();
    public SimpleCurrencyAlertConfig SimpleCurrencyAlertConfig { get; init; } = new();
    public ARQuestingConfiguration ARQuestingConfiguration { get; init; } = new();
}

public class YamlFactory : DefaultSerializationFactory, ISerializationFactory {
    public new string DefaultConfigFileName => $"ezAutomaton.yaml";
    public new T Deserialize<T>(string inputData) => new DeserializerBuilder().IgnoreUnmatchedProperties().Build().Deserialize<T>(inputData);
    public new string Serialize(object s, bool prettyPrint) => new SerializerBuilder().Build().Serialize(s);
}

public interface IMigration {
    int Version { get; }
    void Migrate(ref Config config);
}

public class V3 : IMigration {
    public int Version => 3;
    public void Migrate(ref Config config) {
        var oldType = config.Tweaks.HuntRelayHelper.Types[0];
        if (oldType.TypeHeuristics == @"s rank, (?:^|\W)[sS](?:$|\W)")
            config.Tweaks.HuntRelayHelper.Types[0] = (oldType.RelayType, oldType.TypeFormat, @"s rank, rank s, /(?:^|\W)[sS](?:$|\W)/");
        config.Tweaks.HuntRelayHelper.Types.Insert(1, (HuntRelayHelper.RelayTypes.Minions, "Minions", @"ssminion, /\bminions?\b/"));
    }
}

public class V4 : IMigration {
    public int Version => 4;
    public void Migrate(ref Config config) {
        var oldType = config.Tweaks.HuntRelayHelper.Types[0];
        if (oldType.TypeHeuristics == @"s rank, rank s, /(?:^|\W)[sS](?:$|\W)/")
            config.Tweaks.HuntRelayHelper.Types[0] = (oldType.RelayType, oldType.TypeFormat, @"s rank, rank s, /(?:^|\W)(?<!')[sS](?:$|\W)/");
    }
}
