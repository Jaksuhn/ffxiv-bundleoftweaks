using Dalamud.Plugin;
using System.Collections.Specialized;
using System.Threading;
using System.Threading.Tasks;

namespace ComplexTweaks.Services;

public sealed class TweakService : IPluginService, IAsyncDisposable {
    public readonly HashSet<Tweak> Tweaks = [];

    public TweakService() {
        ConfigService.Get().Config.EnabledTweaks.CollectionChanged += OnChange;
        Svc.Interface.ActivePluginsChanged += OnPluginsChanged;
    }

    public async Task InitializeTweaksAsync(CancellationToken cancellationToken) {
        foreach (var tweakType in GetType().Assembly.GetTypes().Where(type => type is { IsClass: true, IsAbstract: false } && typeof(Tweak).IsAssignableFrom(type))) {
            IPluginLog.Get().Verbose($"Initializing {tweakType.Name}");
            try {
                Tweaks.Add((Tweak)Activator.CreateInstance(tweakType)!);
            }
            catch (Exception ex) {
                ex.Log($"Failed to initialize {tweakType.Name}");
            }
        }

        await Task.WhenAll(Tweaks.Select(t => t.StartAsync(cancellationToken)));
    }

    public void Add(Tweak tweak) => Tweaks.Add(tweak);

    public void Remove(Tweak tweak) => Tweaks.Remove(tweak);

    public Tweak? Find(string key) {
        var byKey = Tweaks.FirstOrDefault(tweak => tweak.ConfigKey == key);
        if (byKey != null)
            return byKey;

        var byName = Tweaks.Where(tweak => tweak.InternalName == key).Take(2).ToArray();
        return byName.Length == 1 ? byName[0] : null;
    }

    public bool IsEnabled(string name)
        => ConfigService.Get().Config.EnabledTweaks.Any(key => key == name || key.EndsWith($"/{name}"));

    public void Enable(string name) {
        var cfg = ConfigService.Get().Config;
        if (Find(name) is { } tweak && !cfg.EnabledTweaks.Contains(tweak.ConfigKey) && (!tweak.IsDebug || cfg.ShowDebug))
            cfg.EnabledTweaks.Add(tweak.ConfigKey);
    }

    public void Disable(string name)
        => ConfigService.Get().Config.EnabledTweaks.Remove(Find(name)?.ConfigKey ?? name);

    public void Toggle(string name) {
        var enabled = ConfigService.Get().Config.EnabledTweaks;
        var key = Find(name)?.ConfigKey ?? name;
        if (!enabled.Remove(key))
            enabled.Add(key);
    }

    private void OnChange(object? sender, NotifyCollectionChangedEventArgs e) {
        foreach (var t in Tweaks) {
            if (t.ShouldEnable())
                _ = t.StartAsync(CancellationToken.None);
            else if (t.ShouldDisable())
                _ = t.StopAsync(CancellationToken.None);
        }
        ConfigService.Get().Save();
    }

    private void OnPluginsChanged(IActivePluginsChangedEventArgs args) {
        foreach (var tweak in Tweaks) {
            if (ConfigService.Get().Config.EnabledTweaks.Contains(tweak.ConfigKey) && tweak.CanBeEnabled())
                _ = tweak.StartAsync(CancellationToken.None);

            if (tweak.Status == TweakStatus.Enabled && !tweak.HasRuntimeRequirements())
                _ = tweak.StopAsync(CancellationToken.None);

            if (tweak.Status == TweakStatus.Enabled)
                tweak.RefreshCommands();
        }
    }

    public ValueTask DisposeAsync() {
        ConfigService.Get().Config.EnabledTweaks.CollectionChanged -= OnChange;
        Svc.Interface.ActivePluginsChanged -= OnPluginsChanged;

        foreach (var t in Tweaks) {
            IPluginLog.Get().Debug($"Disposing {t.InternalName}");
            t.Dispose();
        }
        return ValueTask.CompletedTask;
    }
}
