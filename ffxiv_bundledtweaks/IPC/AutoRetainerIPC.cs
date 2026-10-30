using Dalamud.Plugin.Ipc;

namespace ComplexTweaks.IPC;

public sealed class AutoRetainerIPC : BaseIPC, IPluginService, IDisposable {
    public override Ipc Id => Ipc.AutoRetainer;
    public override string Name => "AutoRetainer";
    public override string Repo => Punish;

    public sealed class OfflineCharacterData {
        public ulong CID;
        public string Name = "Unknown";
        public string World = "";
        public bool ExcludeRetainer;
        public bool ExcludeWorkshop;
    }

    private readonly string? _pluginName;
    private readonly ICallGateSubscriber<bool> _isBusy;
    private readonly ICallGateSubscriber<bool> _getMultiModeEnabled;
    private readonly ICallGateSubscriber<List<ulong>> _getRegisteredCIDs;
    private readonly ICallGateSubscriber<ulong, OfflineCharacterData> _getOfflineCharacterData;
    private readonly ICallGateSubscriber<Dictionary<ulong, HashSet<string>>> _getEnabledRetainers;
    private readonly ICallGateSubscriber<uint, bool> _isItemProtected;
    private readonly ICallGateSubscriber<object>? _onCharacterAdditionalTask;
    private readonly ICallGateSubscriber<string, object>? _onCharacterReadyForPostprocess;
    private readonly ICallGateSubscriber<string, object>? _requestCharacterPostProcess;
    private readonly ICallGateSubscriber<object>? _finishCharacterPostprocessRequest;
    private readonly ICallGateSubscriber<string, object>? _onRetainerAdditionalTask;
    private readonly ICallGateSubscriber<string, string, object>? _onRetainerReadyForPostprocess;
    private readonly ICallGateSubscriber<string, object>? _requestRetainerPostProcess;
    private readonly ICallGateSubscriber<object>? _finishRetainerPostprocessRequest;

    public event Action? OnCharacterPostprocessStep;
    public event Action? OnCharacterReadyToPostProcess;
    public event Action<string>? OnRetainerPostprocessStep;
    public event Action<string>? OnRetainerReadyToPostProcess;

    public AutoRetainerIPC() : this(null) { }

    public AutoRetainerIPC(string? suffix) {
        _isBusy = Svc.Interface.GetIpcSubscriber<bool>("AutoRetainer.PluginState.IsBusy");
        _getMultiModeEnabled = Svc.Interface.GetIpcSubscriber<bool>("AutoRetainer.GetMultiModeEnabled");
        _getRegisteredCIDs = Svc.Interface.GetIpcSubscriber<List<ulong>>("AutoRetainer.GetRegisteredCIDs");
        _getOfflineCharacterData = Svc.Interface.GetIpcSubscriber<ulong, OfflineCharacterData>("AutoRetainer.GetOfflineCharacterData");
        _getEnabledRetainers = Svc.Interface.GetIpcSubscriber<Dictionary<ulong, HashSet<string>>>("AutoRetainer.PluginState.GetEnabledRetainers");
        _isItemProtected = Svc.Interface.GetIpcSubscriber<uint, bool>("AutoRetainer.PluginState.IsItemProtected");

        if (suffix is null)
            return;

        _pluginName = Svc.Interface.InternalName + $"_{suffix}";
        _onCharacterAdditionalTask = Svc.Interface.GetIpcSubscriber<object>("AutoRetainer.OnCharacterAdditionalTask");
        _onCharacterReadyForPostprocess = Svc.Interface.GetIpcSubscriber<string, object>("AutoRetainer.OnCharacterReadyForPostprocess");
        _requestCharacterPostProcess = Svc.Interface.GetIpcSubscriber<string, object>("AutoRetainer.RequestCharacterPostprocess");
        _finishCharacterPostprocessRequest = Svc.Interface.GetIpcSubscriber<object>("AutoRetainer.FinishCharacterPostprocessRequest");

        _onCharacterAdditionalTask.Subscribe(OnCharacterAdditionalTaskHandler);
        _onCharacterReadyForPostprocess.Subscribe(OnCharacterReadyForPostprocessHandler);

        _onRetainerAdditionalTask = Svc.Interface.GetIpcSubscriber<string, object>("AutoRetainer.OnRetainerAdditionalTask");
        _onRetainerReadyForPostprocess = Svc.Interface.GetIpcSubscriber<string, string, object>("AutoRetainer.OnRetainerReadyForPostprocess");
        _requestRetainerPostProcess = Svc.Interface.GetIpcSubscriber<string, object>("AutoRetainer.RequestPostprocess");
        _finishRetainerPostprocessRequest = Svc.Interface.GetIpcSubscriber<object>("AutoRetainer.FinishPostprocessRequest");

        _onRetainerAdditionalTask.Subscribe(OnRetainerAdditionalTaskHandler);
        _onRetainerReadyForPostprocess.Subscribe(OnRetainerReadyForPostprocessHandler);
    }

    public bool IsBusy() => _isBusy.HasFunction && _isBusy.InvokeFunc();
    public bool GetMultiModeEnabled() => _getMultiModeEnabled.HasFunction && _getMultiModeEnabled.InvokeFunc();

    public List<ulong> GetRegisteredCharacters()
        => _getRegisteredCIDs.HasFunction ? _getRegisteredCIDs.InvokeFunc() : [];

    public OfflineCharacterData? GetOfflineCharacterData(ulong cid)
        => _getOfflineCharacterData.HasFunction ? _getOfflineCharacterData.InvokeFunc(cid) : null;

    public Dictionary<ulong, HashSet<string>>? GetEnabledRetainers()
        => _getEnabledRetainers.HasFunction ? _getEnabledRetainers.InvokeFunc() : null;

    public bool IsItemProtected(uint itemId) => _isItemProtected.HasFunction && _isItemProtected.InvokeFunc(itemId);

    public void RequestCharacterPostprocess() {
        if (_requestCharacterPostProcess?.HasAction == true) {
            IPluginLog.Get().Debug($"[{_pluginName}] Requesting CharacterPostProcess");
            _requestCharacterPostProcess.InvokeAction(_pluginName!);
        }
        else
            IPluginLog.Get().Warning($"[{_pluginName}] Unable to request CharacterPostProcess");
    }

    public void FinishCharacterPostProcess() {
        if (_finishCharacterPostprocessRequest?.HasAction == true) {
            IPluginLog.Get().Debug($"[{_pluginName}] Finishing CharacterPostProcess");
            _finishCharacterPostprocessRequest.InvokeAction();
        }
        else
            IPluginLog.Get().Warning($"[{_pluginName}] Unable to finish CharacterPostProcess");
    }

    public void RequestRetainerPostprocess() {
        if (_requestRetainerPostProcess?.HasAction == true) {
            IPluginLog.Get().Debug($"[{_pluginName}] Requesting RetainerPostProcess");
            _requestRetainerPostProcess.InvokeAction(_pluginName!);
        }
        else
            IPluginLog.Get().Warning($"[{_pluginName}] Unable to request RetainerPostProcess");
    }

    public void FinishRetainerPostprocess() {
        if (_finishRetainerPostprocessRequest?.HasAction == true) {
            IPluginLog.Get().Debug($"[{_pluginName}] Finishing RetainerPostProcess");
            _finishRetainerPostprocessRequest.InvokeAction();
        }
        else
            IPluginLog.Get().Warning($"[{_pluginName}] Unable to finish RetainerPostProcess");
    }

    public void Dispose() {
        _onCharacterAdditionalTask?.Unsubscribe(OnCharacterAdditionalTaskHandler);
        _onCharacterReadyForPostprocess?.Unsubscribe(OnCharacterReadyForPostprocessHandler);
        _onRetainerAdditionalTask?.Unsubscribe(OnRetainerAdditionalTaskHandler);
        _onRetainerReadyForPostprocess?.Unsubscribe(OnRetainerReadyForPostprocessHandler);
    }

    private void OnCharacterAdditionalTaskHandler() {
        try {
            OnCharacterPostprocessStep?.Invoke();
        }
        catch (Exception ex) {
            IPluginLog.Get().Error(ex, $"[{_pluginName}] {nameof(AutoRetainerIPC)}.{nameof(OnCharacterPostprocessStep)} failed.");
        }
    }

    private void OnCharacterReadyForPostprocessHandler(string plugin) {
        if (plugin != _pluginName)
            return;

        try {
            OnCharacterReadyToPostProcess?.Invoke();
        }
        catch (Exception ex) {
            IPluginLog.Get().Error(ex, $"[{_pluginName}] {nameof(AutoRetainerIPC)},{nameof(OnCharacterReadyToPostProcess)} failed.");
        }
    }

    private void OnRetainerAdditionalTaskHandler(string retainerName) {
        try {
            OnRetainerPostprocessStep?.Invoke(retainerName);
        }
        catch (Exception ex) {
            IPluginLog.Get().Error(ex, $"[{_pluginName}] {nameof(AutoRetainerIPC)}.{nameof(OnRetainerPostprocessStep)} failed.");
        }
    }

    private void OnRetainerReadyForPostprocessHandler(string plugin, string retainerName) {
        if (plugin != _pluginName)
            return;

        try {
            OnRetainerReadyToPostProcess?.Invoke(retainerName);
        }
        catch (Exception ex) {
            IPluginLog.Get().Error(ex, $"[{_pluginName}] {nameof(AutoRetainerIPC)}.{nameof(OnRetainerReadyToPostProcess)} failed.");
        }
    }
}
