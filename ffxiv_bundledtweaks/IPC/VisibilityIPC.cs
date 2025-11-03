using ECommons.EzIpcManager;

namespace ComplexTweaks.IPC;
#nullable disable
[Ipc(Ipc.Visibility)]
public class VisibilityIPC : BaseIPC
{
    public override string Name => "Visibility";
    public override string Repo => Main;

    public VisibilityIPC() => EzIPC.Init(this, Name);

    [EzIPC] public Func<int> ApiVersion;
    [EzIPC] public Func<IEnumerable<string>> GetVoidListEntries;
    /// <summary>
    /// name, worldid, reason
    /// </summary>
    [EzIPC] public Action<string, uint, string> AddToVoidList;
    /// <summary>
    /// name, worldid
    /// </summary>
    [EzIPC] public Action<string, uint> RemoveFromVoidList;

    [EzIPC] public Func<IEnumerable<string>> GetWhitelistEntries;
    /// <summary>
    /// name, worldid, reason
    /// </summary>
    [EzIPC] public Action<string, uint, string> AddToWhitelist;
    /// <summary>
    /// name, worldid
    /// </summary>
    [EzIPC] public Action<string, uint> RemoveFromWhitelist;
    /// <summary>
    /// state
    /// </summary>
    [EzIPC] public Action<bool> Enable;
}
