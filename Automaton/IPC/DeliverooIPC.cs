using ECommons.EzIpcManager;

namespace Automaton.IPC;

#nullable disable
public class DeliverooIPC : BaseIPC
{
    public override string Name => "Deliveroo";
    public override string Repo => "https://git.carvel.li/liza/";
    public DeliverooIPC() => EzIPC.Init(this, Name, SafeWrapper.AnyException);

    [EzIPC] public Func<bool> IsTurnInRunning;
}
