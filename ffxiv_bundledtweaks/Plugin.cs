using clib;
using Dalamud.Plugin;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using System.Threading;
using System.Threading.Tasks;

namespace ComplexTweaks;

public sealed class Plugin(IDalamudPluginInterface dalamud) : IAsyncDalamudPlugin {
    public async Task LoadAsync(CancellationToken cancellationToken) {
#if LOCAL_CS
        dalamud.InitCustomClientStructs();
#endif
        CLibMain.Init(dalamud, this, CLibModule.Automation);

        ICommandManager.Get().AddHandler("/cbt", new(OnCommand) { HelpMessage = $"Opens the {dalamud.Manifest.Name} menu" });
        await TweakService.Get().InitializeTweaksAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync() {
        ICommandManager.Get().RemoveHandler("/cbt");
        ConfigService.Get().Save();
        await CLibMain.DisposeAsync();
    }

    private void OnCommand(string command, string args) {
        if (args.Length == 0)
            WindowsService.Get().ToggleMain();
        else {
            var arguments = args.Split(' ');
            var subcommand = arguments[0];
            var @params = arguments.Skip(1).ToArray();
            switch (subcommand) {
                case string cmd when cmd.StartsWith('d') && !cmd.EqualsIgnoreCase("disable"):
                    WindowsService.Get().ToggleDebug();
                    break;
                case "enable":
                    TweakService.Get().Enable(@params[0]);
                    break;
                case "disable":
                    TweakService.Get().Disable(@params[0]);
                    break;
                case "toggle":
                    TweakService.Get().Toggle(@params[0]);
                    break;
                case "stop":
                    Svc.Automation.Stop();
                    foreach (var t in TweakService.Get().Tweaks)
                        t.StopAutomation();
                    foreach (var t in TweakService.Get().Tweaks.OfType<ARTweak>())
                        t.AutoRetainer.FinishCharacterPostProcess();
                    break;
                case "leave":
                    EventFramework.LeaveCurrentContent(true);
                    break;
            }
        }
    }
}
