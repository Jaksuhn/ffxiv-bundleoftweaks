using ECommons.EzIpcManager;

namespace ComplexTweaks.IPC;

#nullable disable
[Ipc(Ipc.AutoRetainer)]
public class AutoRetainerIPC : BaseIPC
{
    public override string Name => "AutoRetainer";
    public override string Repo => Punish;
    public AutoRetainerIPC() => EzIPC.Init(this, Name);

    [EzIPC("PluginState.%m")] public readonly Func<bool> IsBusy;
    [EzIPC("PluginState.%m")] public readonly Func<int> GetInventoryFreeSlotCount;
    [EzIPC] public readonly Func<bool> GetMultiModeEnabled;
    [EzIPC] public readonly Action<bool> SetMultiModeEnabled;
    [EzIPC] public readonly Func<bool> GetSuppressed;
    [EzIPC] public readonly Action<bool> SetSuppressed;
    [EzIPC("GC.%m")] public readonly Action EnqueueInitiation;
}
