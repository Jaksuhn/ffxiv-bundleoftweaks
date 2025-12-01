using ECommons.EzIpcManager;

namespace ComplexTweaks.IPC;

#nullable disable
#pragma warning disable CS0649
[Ipc(Ipc.QoLBar)]
internal class QoLBarIPC : BaseIPC {
    public override string Name => "QoLBar";
    public override string Repo => Main;
    public QoLBarIPC() => EzIPC.Init(this, Name);

    [EzIPC] public readonly Func<string> GetVersion;
    [EzIPC] public readonly Func<int> GetIPCVersion;
    [EzIPC] public readonly Func<string, object> ImportBar;
    [EzIPC] public readonly Func<string[]> GetConditionSets;
    [EzIPC] public readonly Func<int, bool> CheckConditionSet;
    [EzIPC] public readonly Func<int, int, object> MovedConditionSet;
    [EzIPC] public readonly Func<int, object> RemovedConditionSet;
}
