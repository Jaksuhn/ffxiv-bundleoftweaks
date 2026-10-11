using clib.Configuration;
using ComplexTweaks.Configuration;
using Dalamud.Configuration;
using System.IO;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.TypeInspectors;
using YamlDotNet.Serialization.TypeResolvers;

namespace ComplexTweaks.Services;

public sealed class ConfigService : IPluginService {
    private static readonly string ConfigFileName = "ezAutomaton.yaml";

    public Config Config { get; private set; }

    private string ConfigPath => Path.Combine(Svc.Interface.GetPluginConfigDirectory(), ConfigFileName);

    public ConfigService() {
        var (config, needsSave) = Load();
        Config = config;
        if (needsSave)
            Save();
    }

    public T GetTweakConfig<T>(string tweakName) where T : new() {
        if (Config.Tweaks.TryGetValue(tweakName, out var existing)) {
            if (existing is T typed)
                return typed;

            if (TryConvert(existing, out T converted)) {
                Config.Tweaks[tweakName] = converted!;
                return converted;
            }
        }

        var created = new T();
        Config.Tweaks[tweakName] = created;
        return created;
    }

    public void Save() {
        try {
            var yaml = Serializer.Serialize(Config);
            File.WriteAllText(ConfigPath, yaml);
        }
        catch (Exception ex) {
            IPluginLog.Get().Error(ex, $"[{nameof(ConfigService)}] Failed to save config");
        }
    }

    private (Config config, bool needsSave) Load() {
        try {
            if (!File.Exists(ConfigPath))
                return (new Config(), false);

            var yaml = File.ReadAllText(ConfigPath);
            var version = VersionProbe.Deserialize<ConfigVersionProbe>(yaml)?.Version ?? 0;

            IPluginConfiguration initial = version < 5
                ? LegacyDeserializer.Deserialize<LegacyConfig>(yaml) ?? new LegacyConfig()
                : version < Config.CURRENT_CONFIG_VERSION
                    ? Deserializer.Deserialize<ConfigV5>(yaml) ?? new ConfigV5()
                    : Deserializer.Deserialize<Config>(yaml) ?? new Config();
            var migrated = ConfigHelper.RunMigrationChain(initial, typeof(ConfigService).Assembly, out var final);
            return ((Config)final, migrated);
        }
        catch (Exception ex) {
            IPluginLog.Get().Error(ex, $"[{nameof(ConfigService)}] Failed to load config, using defaults");
            return (new Config(), false);
        }
    }

    private bool TryConvert<T>(object raw, out T result) where T : new() {
        result = default!;
        if (raw is T typed) {
            result = typed;
            return true;
        }

        try {
            var nestedYaml = Serializer.Serialize(raw);
            result = Deserializer.Deserialize<T>(nestedYaml) ?? new T();
            return true;
        }
        catch (Exception ex) {
            IPluginLog.Get().Warning(ex, $"[{nameof(ConfigService)}] Failed to convert tweak config to {typeof(T).Name}, using defaults");
            result = new T();
            return false;
        }
    }

    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithTypeInspector(inner => new CompositeTypeInspector(new ReadableFieldsTypeInspector(new StaticTypeResolver()), inner))
        .Build();

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .IgnoreUnmatchedProperties()
        .Build();

    private static readonly IDeserializer LegacyDeserializer = new DeserializerBuilder()
        .IgnoreUnmatchedProperties()
        .Build();

    private static readonly IDeserializer VersionProbe = new DeserializerBuilder()
        .IgnoreUnmatchedProperties()
        .Build();
}
