using Dalamud.Plugin.Ipc;

namespace ComplexTweaks.IPC;

[Flags]
public enum GlamourLogStoreTargets {
    None = 0,
    Armoire = 1 << 0,
    Dresser = 1 << 1,
}

public sealed class GlamourLogIPC : BaseIPC, IPluginService {
    public override Ipc Id => Ipc.GlamourLog;
    public override string Name => "GlamourLog";
    public override string Repo => Croizat;

    private readonly ICallGateSubscriber<uint, bool> _isItemOwned;
    private readonly ICallGateSubscriber<uint, bool> _isItemStorable;
    private readonly ICallGateSubscriber<uint, bool> _isSetComplete;
    private readonly ICallGateSubscriber<List<uint>> _getArmoireItemIds;
    private readonly ICallGateSubscriber<int> _hasAnythingToStore;
    private readonly ICallGateSubscriber<bool> _readyToStore;
    private readonly ICallGateSubscriber<bool> _entrustAll;
    private readonly ICallGateSubscriber<bool> _isBusy;

    public GlamourLogIPC() {
        _isItemOwned = Svc.Interface.GetIpcSubscriber<uint, bool>("GlamourLog.IsItemOwned");
        _isItemStorable = Svc.Interface.GetIpcSubscriber<uint, bool>("GlamourLog.IsItemStorable");
        _isSetComplete = Svc.Interface.GetIpcSubscriber<uint, bool>("GlamourLog.IsSetComplete");
        _getArmoireItemIds = Svc.Interface.GetIpcSubscriber<List<uint>>("GlamourLog.GetArmoireItemIds");
        _hasAnythingToStore = Svc.Interface.GetIpcSubscriber<int>("GlamourLog.HasAnythingToStore");
        _readyToStore = Svc.Interface.GetIpcSubscriber<bool>("GlamourLog.ReadyToStore");
        _entrustAll = Svc.Interface.GetIpcSubscriber<bool>("GlamourLog.EntrustAll");
        _isBusy = Svc.Interface.GetIpcSubscriber<bool>("GlamourLog.IsBusy");
    }

    public bool IsItemOwned(uint itemId) => _isItemOwned.HasFunction && _isItemOwned.InvokeFunc(itemId);
    public bool IsItemStorable(uint itemId) => _isItemStorable.HasFunction && _isItemStorable.InvokeFunc(itemId);
    public bool IsSetComplete(uint setItemId) => _isSetComplete.HasFunction && _isSetComplete.InvokeFunc(setItemId);
    public List<uint> GetArmoireItemIds() => _getArmoireItemIds.HasFunction ? _getArmoireItemIds.InvokeFunc() : [];
    public GlamourLogStoreTargets HasAnythingToStore() => _hasAnythingToStore.HasFunction ? (GlamourLogStoreTargets)_hasAnythingToStore.InvokeFunc() : GlamourLogStoreTargets.None;
    public bool ReadyToStore() => _readyToStore.HasFunction && _readyToStore.InvokeFunc();
    public bool EntrustAll() => _entrustAll.HasFunction && _entrustAll.InvokeFunc();
    public bool IsBusy() => _isBusy.HasFunction && _isBusy.InvokeFunc();
}
