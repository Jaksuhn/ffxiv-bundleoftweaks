using Dalamud.Plugin.Ipc;

namespace ComplexTweaks.IPC;

// reminding myself but the docs are in installedPlugins/Knightshopper/<ver>/docs/IPC.md
public sealed class KnightshopperIPC : BaseIPC, IPluginService {
    public override Ipc Id => Ipc.Knightshopper;
    public override string Name => "Knightshopper";
    public override string Repo => Dynamis + "knightmore";

    public enum CurrencyId {
        BicolorGemstone = 0,
        Gil = 2,
        Hunt = 3,
        MGP = 4,
        PVP = 5,
        Scrip = 6,
        Tomestone = 7,
        Firmament = 8,
        Cosmocredits = 9,
        OccultCrescent = 10,
    }

    public enum StartResult {
        Started = 0,
        Busy = 1,
        InvalidCurrency = 2,
        EmptyList = 3,
        NotReady = 4,
        NotLoggedIn = 5,
        NotAllowed = 6,
        ItemUnavailable = 7,
        InvalidQuantity = 8,
        ListNotFound = 9,
    }

    public enum PurchaseState {
        Unknown = 0,
        Running = 1,
        CancellationRequested = 2,
        Succeeded = 3,
        Failed = 4,
        Cancelled = 5,
    }

    public readonly record struct StartResponse(StartResult Result, Guid OperationId, string Message) {
        public bool Started => Result == StartResult.Started;
    }

    public readonly record struct PurchaseStatus(PurchaseState State, int CurrentIndex, int TotalItems, uint CurrentItemId, string Message) {
        public bool IsFinished => State is PurchaseState.Succeeded or PurchaseState.Failed or PurchaseState.Cancelled or PurchaseState.Unknown;
    }

    private readonly ICallGateSubscriber<int> _apiVersion;
    private readonly ICallGateSubscriber<bool> _isBusy;
    private readonly ICallGateSubscriber<int, (int Result, Guid OperationId, string Message)> _start;
    private readonly ICallGateSubscriber<int, (Guid Id, string Name)[]> _getShoppingLists;
    private readonly ICallGateSubscriber<Guid, (int State, int CurrentIndex, int TotalItems, uint CurrentItemId, string Message)> _getStatus;
    private readonly ICallGateSubscriber<Guid, bool> _cancel;

    public KnightshopperIPC() {
        _apiVersion = Svc.Interface.GetIpcSubscriber<int>("Knightshopper.ApiVersion");
        _isBusy = Svc.Interface.GetIpcSubscriber<bool>("Knightshopper.IsBusy");
        _start = Svc.Interface.GetIpcSubscriber<int, (int Result, Guid OperationId, string Message)>("Knightshopper.Purchase.Start");
        _getShoppingLists = Svc.Interface.GetIpcSubscriber<int, (Guid Id, string Name)[]>("Knightshopper.List.GetShoppingLists");
        _getStatus = Svc.Interface.GetIpcSubscriber<Guid, (int State, int CurrentIndex, int TotalItems, uint CurrentItemId, string Message)>("Knightshopper.Purchase.GetStatus");
        _cancel = Svc.Interface.GetIpcSubscriber<Guid, bool>("Knightshopper.Purchase.Cancel");
    }

    public bool IsAvailable => _apiVersion.HasFunction;
    public bool IsBusy => _isBusy.HasFunction && _isBusy.InvokeFunc();

    public bool HasShoppingList(CurrencyId currency) {
        if (!IsAvailable)
            return false;
        if (!_getShoppingLists.HasFunction)
            return true;
        return _getShoppingLists.InvokeFunc((int)currency).Length > 0;
    }

    public StartResponse Start(CurrencyId currency) {
        if (!_start.HasFunction)
            return new(StartResult.NotReady, Guid.Empty, "Knightshopper not loaded");
        var r = _start.InvokeFunc((int)currency);
        return new((StartResult)r.Result, r.OperationId, r.Message);
    }

    public PurchaseStatus GetStatus(Guid operationId) {
        if (!_getStatus.HasFunction)
            return new(PurchaseState.Unknown, 0, 0, 0, "Knightshopper not loaded");
        var s = _getStatus.InvokeFunc(operationId);
        return new((PurchaseState)s.State, s.CurrentIndex, s.TotalItems, s.CurrentItemId, s.Message);
    }

    public bool Cancel(Guid operationId) => _cancel.HasFunction && _cancel.InvokeFunc(operationId);
}
