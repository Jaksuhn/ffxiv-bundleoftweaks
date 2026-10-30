using Dalamud.Plugin.Ipc;

namespace ComplexTweaks.IPC;

public sealed class GlamourLogIPC : BaseIPC, IPluginService {
    public override Ipc Id => Ipc.GlamourLog;
    public override string Name => "GlamourLog";
    public override string Repo => Croizat;

    private readonly ICallGateSubscriber<uint, bool> _isItemOwned;
    private readonly ICallGateSubscriber<uint, bool> _isItemStorable;

    public GlamourLogIPC() {
        _isItemOwned = Svc.Interface.GetIpcSubscriber<uint, bool>("GlamourLog.IsItemOwned");
        _isItemStorable = Svc.Interface.GetIpcSubscriber<uint, bool>("GlamourLog.IsItemStorable");
    }

    public bool IsItemOwned(uint itemId) => _isItemOwned.HasFunction && _isItemOwned.InvokeFunc(itemId);
    public bool IsItemStorable(uint itemId) => _isItemStorable.HasFunction && _isItemStorable.InvokeFunc(itemId);
}
