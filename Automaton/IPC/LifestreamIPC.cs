using ECommons.EzIpcManager;

namespace Automaton.IPC;

#nullable disable
public class LifestreamIPC : BaseIPC
{
    public override string Name => "Lifestream";
    public override string Repo => "https://github.com/NightmareXIV/MyDalamudPlugins/raw/main/pluginmaster.json";
    public LifestreamIPC() => EzIPC.Init(this, Name);

    [EzIPC] public Func<string, bool> AethernetTeleport;
    [EzIPC] public Func<uint, byte, bool> Teleport;
    [EzIPC] public Func<bool> TeleportToHome;
    [EzIPC] public Func<bool> TeleportToFC;
    [EzIPC] public Func<bool> TeleportToApartment;
    [EzIPC] public Func<bool> IsBusy;
    [EzIPC] public Action<string> ExecuteCommand;
}
