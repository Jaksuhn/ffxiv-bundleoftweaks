using Dalamud.Plugin.Ipc;

namespace ComplexTweaks.IPC;

public sealed class GlamourLogIPC : BaseIPC, IPluginService {
    public override Ipc Id => Ipc.GlamourLog;
    public override string Name => "GlamourLog";
    public override string Repo => Croizat;

    private readonly ICallGateSubscriber<uint, bool> _isItemOwned;
    private readonly ICallGateSubscriber<uint, bool> _isItemStorable;
    private readonly ICallGateSubscriber<uint, bool> _isSetComplete;
    private readonly ICallGateSubscriber<List<uint>> _getArmoireItemIds;

    public GlamourLogIPC() {
        _isItemOwned = Svc.Interface.GetIpcSubscriber<uint, bool>("GlamourLog.IsItemOwned");
        _isItemStorable = Svc.Interface.GetIpcSubscriber<uint, bool>("GlamourLog.IsItemStorable");
        _isSetComplete = Svc.Interface.GetIpcSubscriber<uint, bool>("GlamourLog.IsSetComplete");
        _getArmoireItemIds = Svc.Interface.GetIpcSubscriber<List<uint>>("GlamourLog.GetArmoireItemIds");
    }

    public bool IsItemOwned(uint itemId) => _isItemOwned.HasFunction && _isItemOwned.InvokeFunc(itemId);
    public bool IsItemStorable(uint itemId) => _isItemStorable.HasFunction && _isItemStorable.InvokeFunc(itemId);
    public bool IsSetComplete(uint setItemId) => _isSetComplete.HasFunction && _isSetComplete.InvokeFunc(setItemId);
    public List<uint> GetArmoireItemIds() => _getArmoireItemIds.HasFunction ? _getArmoireItemIds.InvokeFunc() : [];
}
