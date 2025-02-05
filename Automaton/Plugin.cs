using Automaton.Configuration;
using Automaton.UI;
using Dalamud.Plugin;
using ECommons;
using ECommons.Configuration;
using ECommons.EzEventManager;
using ECommons.SimpleGui;
using ECommons.Singletons;
using System.Collections.Specialized;
using System.Reflection;

namespace Automaton;

public class Plugin : IDalamudPlugin
{
    public static string Name => "CBT";
    public static string VersionString => $"v{P.GetType().Assembly.GetName().Version?.Major}.{P.GetType().Assembly.GetName().Version?.Minor}";
    private const string Command = "/cbt";
    private const string LegacyCommand = "/automaton";
    public static Plugin P { get; private set; } = null!;
    public static Config C => P.Config;
    private readonly Config Config;

    public static readonly HashSet<Tweak> Tweaks = [];
    internal bool UsingARPostProcess;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        P = this;
        ECommonsMain.Init(pluginInterface, P, ECommons.Module.DalamudReflector, ECommons.Module.ObjectFunctions);
        EzConfig.DefaultSerializationFactory = new YamlFactory();
        Config = EzConfig.Init<Config>();

        IMigration[] migrations = [new V3()];
        foreach (var migration in migrations)
        {
            if (Config.Version < migration.Version)
            {
                Svc.Log.Info($"Migrating from config version {Config.Version} to {migration.Version}");
                migration.Migrate(ref Config);
                Config.Version = migration.Version;
            }
        }

        EzCmd.Add(Command, OnCommand, $"Opens the {Name} menu");
        EzCmd.Add(LegacyCommand, OnCommand);
        EzConfigGui.Init(new HaselWindow().Draw, nameOverride: $"{Name} {VersionString}");
        EzConfigGui.WindowSystem.AddWindow(new DebugWindow());

        SingletonServiceManager.Initialize(typeof(Service));

        Svc.Framework.RunOnFrameworkThread(InitializeTweaks);
        C.EnabledTweaks.CollectionChanged += OnChange;
        _ = new EzFrameworkUpdate(EventWatcher);
    }

    private bool inpvp = false;
    private void EventWatcher()
    {
        if (PlayerEx.InPvP)
        {
            if (!inpvp)
            {
                inpvp = true;
                Events.OnEnteredPvPInstance();
            }
        }
        else
            inpvp = false;
    }

    public static void OnChange(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var t in Tweaks)
        {
            if (C.EnabledTweaks.Contains(t.InternalName) && !t.Enabled)
                TryExecute(t.EnableInternal);
            else if (!C.EnabledTweaks.Contains(t.InternalName) && t.Enabled || t.Enabled && t.IsDebug && !C.ShowDebug)
                t.DisableInternal();
            EzConfig.Save();
        }
    }

    public void Dispose()
    {
        foreach (var tweak in Tweaks)
        {
            Svc.Log.Debug($"Disposing {tweak.InternalName}");
            TryExecute(tweak.DisposeInternal);
        }
        C.EnabledTweaks.CollectionChanged -= OnChange;
        ECommonsMain.Dispose();
    }

    private void OnCommand(string command, string args)
    {
        if (args.Length == 0)
            EzConfigGui.Window.Toggle();
        else
        {
            var arguments = args.Split(' ');
            var subcommand = arguments[0];
            var @params = arguments.Skip(1).ToArray();
            switch (subcommand)
            {
                case string cmd when cmd.StartsWith('d') && !cmd.EqualsIgnoreCase("disable"):
                    EzConfigGui.GetWindow<DebugWindow>()!.Toggle();
                    break;
                case "enable":
                    C.EnabledTweaks.Add(@params[0]);
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
                    break;
            }
        }
    }

    private void InitializeTweaks()
    {
        foreach (var tweakType in GetType().Assembly.GetTypes().Where(type => type.Namespace == "Automaton.Features" && type.GetCustomAttribute<TweakAttribute>() != null))
        {
            Svc.Log.Verbose($"Initializing {tweakType.Name}");
            try
            {
                Tweaks.Add((Tweak)Activator.CreateInstance(tweakType)!);
            }
            catch (Exception ex)
            {
                Svc.Log.Error($"Failed to initialize {tweakType.Name}", ex);
            }
        }

        foreach (var tweak in Tweaks)
        {
            if (!Config.EnabledTweaks.Contains(tweak.InternalName))
                continue;

            if (Config.EnabledTweaks.Contains(tweak.InternalName) && tweak.IsDebug && !Config.ShowDebug)
                Config.EnabledTweaks.Remove(tweak.InternalName);

            TryExecute(tweak.EnableInternal);
        }
    }
}
