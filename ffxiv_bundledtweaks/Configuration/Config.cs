using Dalamud.Configuration;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace ComplexTweaks.Configuration;

public class Config : IPluginConfiguration {
    [JsonIgnore]
    public const int CURRENT_CONFIG_VERSION = 5;

    public int Version { get; set; } = CURRENT_CONFIG_VERSION;
    public ObservableCollection<string> EnabledTweaks = [];
    public Dictionary<string, object> Tweaks = [with(StringComparer.Ordinal)];
    public List<TweakRepo> Repos = [];
    public bool ShowDebug;
}
