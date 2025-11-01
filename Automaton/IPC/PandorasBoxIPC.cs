using ECommons.EzIpcManager;

namespace Automaton.IPC;

#nullable disable
#pragma warning disable CS0649
[Ipc(Ipc.PandorasBox)]
internal class PandorasBoxIPC : BaseIPC
{
    public override string Name => "PandorasBox";
    public override string Repo => Punish;
    public PandorasBoxIPC() => EzIPC.Init(this, Name);

    [EzIPC] public readonly Func<string, bool?> GetFeatureEnabled;
    [EzIPC] public readonly Func<string, string, bool?> GetConfigEnabled;

    [EzIPC] public readonly Action<string, bool, object> SetFeatureEnabled;
    [EzIPC] public readonly Action<string, string, bool, object> SetConfigEnabled;
    [EzIPC] public readonly Action<string, int, object> PauseFeature;
}
