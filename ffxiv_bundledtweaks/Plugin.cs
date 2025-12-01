using ComplexTweaks.Configuration;
using ComplexTweaks.UI;
using Dalamud.Plugin;
using ECommons;
using ECommons.Configuration;
using ECommons.Reflection;
using ECommons.SimpleGui;
using ECommons.Singletons;
using System.Collections.Specialized;
using System.Reflection;

namespace ComplexTweaks;

public class Plugin : IDalamudPlugin {
    public static string Name => "CBT";
    private const string Command = "/cbt";
    public static Plugin P { get; private set; } = null!;
    public static Config C { get; private set; } = null!;
    public Version Version { get; private set; } = null!;

    public static readonly HashSet<Tweak> Tweaks = [];

    public Plugin(IDalamudPluginInterface pluginInterface) {
        P = this;
        Version = P.GetType().Assembly.GetName().Version ?? new(0, 0);
        ECommonsMain.Init(pluginInterface, P, ECommons.Module.DalamudReflector, ECommons.Module.ObjectFunctions);

#if LOCAL_CS
        FFXIVClientStructs.Interop.Generated.Addresses.Register();
        InteropGenerator.Runtime.Resolver.GetInstance.Setup(Svc.SigScanner.SearchBase, Svc.Data.GameData.Repositories["ffxiv"].Version, new(System.IO.Path.Join(pluginInterface.ConfigDirectory.FullName, "SigCache.json")));
        InteropGenerator.Runtime.Resolver.GetInstance.Resolve();
#endif

        EzConfig.DefaultSerializationFactory = new YamlFactory();
        C = EzConfig.Init<Config>();

        IMigration[] migrations = [new V3(), new V4()];
        foreach (var migration in migrations) {
            if (C.Version < migration.Version) {
                Svc.Log.Info($"Migrating from config version {C.Version} to {migration.Version}");
                var c = C;
                migration.Migrate(ref c);
                C = c;
                C.Version = migration.Version;
            }
        }

        EzCmd.Add(Command, OnCommand, $"Opens the {Name} menu");
        EzConfigGui.Init(new HaselWindow(), nameOverride: $"{Name} v{P.Version.ToString(2)}");
        EzConfigGui.WindowSystem.AddWindow(new DebugWindow());

        SingletonServiceManager.Initialize(typeof(Service));

        Svc.Framework.RunOnFrameworkThread(InitializeTweaks);
        C.EnabledTweaks.CollectionChanged += OnChange;
        DalamudReflector.RegisterOnInstalledPluginsChangedEvents(OnPluginsChanged);
    }

    public static void OnChange(object? sender, NotifyCollectionChangedEventArgs e) {
        foreach (var t in Tweaks) {
            if (C.EnabledTweaks.Contains(t.InternalName) && !t.Enabled)
                TryExecute(t.EnableInternal);
            else if (!C.EnabledTweaks.Contains(t.InternalName) && t.Enabled || t.Enabled && t.IsDebug && !C.ShowDebug)
                t.DisableInternal();
            EzConfig.Save();
        }
    }

    private static void OnPluginsChanged() {
        foreach (var tweak in Tweaks) {
            if (C.EnabledTweaks.Contains(tweak.InternalName) && !tweak.Enabled && !tweak.Outdated && !tweak.Disabled)
                if (tweak.CanBeEnabled())
                    TryExecute(tweak.EnableInternal);

            if (tweak.Enabled && !tweak.CanBeEnabled())
                TryExecute(() => tweak.DisableInternal());
        }
    }

    public void Dispose() {
        foreach (var tweak in Tweaks) {
            Svc.Log.Debug($"Disposing {tweak.InternalName}");
            TryExecute(tweak.DisposeInternal);
        }
        C.EnabledTweaks.CollectionChanged -= OnChange;
        ECommonsMain.Dispose();
    }

    private void OnCommand(string command, string args) {
        if (args.Length == 0)
            EzConfigGui.Window?.Toggle();
        else {
            var arguments = args.Split(' ');
            var subcommand = arguments[0];
            var @params = arguments.Skip(1).ToArray();
            switch (subcommand) {
                case string cmd when cmd.StartsWith('d') && !cmd.EqualsIgnoreCase("disable"):
                    EzConfigGui.GetWindow<DebugWindow>()!.Toggle();
                    break;
                case "enable":
                    if (Tweaks.FirstOrDefault(t => t.InternalName == @params[0]) is { } tweak && !C.EnabledTweaks.Contains(tweak.InternalName) && (!tweak.IsDebug || C.ShowDebug))
                        C.EnabledTweaks.Add(tweak.InternalName);
                    break;
                case "disable":
                    C.EnabledTweaks.Remove(@params[0]);
                    break;
                case "toggle":
                    if (C.EnabledTweaks.Contains(@params[0]))
                        C.EnabledTweaks.Remove(@params[0]);
                    else
                        C.EnabledTweaks.Add(@params[0]);
                    break;
                case "stop":
                    Service.Automation.Stop();
                    Service.TaskManager.Abort();
                    foreach (var t in Tweaks.OfType<ARTweak>())
                        t.AutoRetainer.FinishCharacterPostProcess();
                    break;
            }
        }
    }

    private void InitializeTweaks() {
        foreach (var tweakType in GetType().Assembly.GetTypes().Where(type => type.GetCustomAttribute<TweakAttribute>() != null)) {
            Svc.Log.Verbose($"Initializing {tweakType.Name}");
            try {
                Tweaks.Add((Tweak)Activator.CreateInstance(tweakType)!);
            }
            catch (Exception ex) {
                Svc.Log.Error($"Failed to initialize {tweakType.Name}", ex);
                ex.Log();
            }
        }

        foreach (var tweak in Tweaks) {
            if (!C.EnabledTweaks.Contains(tweak.InternalName))
                continue;

            if (C.EnabledTweaks.Contains(tweak.InternalName) && tweak.IsDebug && !C.ShowDebug)
                C.EnabledTweaks.Remove(tweak.InternalName);

            TryExecute(tweak.EnableInternal);
        }
    }
}
