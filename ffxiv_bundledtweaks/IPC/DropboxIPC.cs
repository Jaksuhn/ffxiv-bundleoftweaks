using ECommons.EzIpcManager;

namespace SomethingNeedDoing.IPC;

#nullable disable
[Ipc(Ipc.Dropbox)]
public class DropboxIPC : BaseIPC
{
    public override string Name => "Dropbox";
    public override string Repo => Kawaii;
    public DropboxIPC() => EzIPC.Init(this, Name);

    [EzIPC] public readonly Func<bool> IsBusy;
    [EzIPC] public readonly Func<uint, bool, int> GetItemQuantity; // id, hq
    [EzIPC] public readonly Action<uint, bool, int> SetItemQuantity; // id, hq, quantity

    [EzIPC] public readonly Action BeginTradingQueue;
    [EzIPC] public readonly Action Stop;
}
