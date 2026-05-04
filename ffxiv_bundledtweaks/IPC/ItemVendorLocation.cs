using ECommons.EzIpcManager;

namespace ComplexTweaks.IPC;

#nullable disable
[Ipc(Ipc.ItemVendorLocation)]
public class ItemVendorLocation : BaseIPC {
    public override string Name => "ItemVendorLocation";
    public override string Repo => Main;
    public ItemVendorLocation() => EzIPC.Init(this, Name);

    /// <summary> uint itemId </summary>
    [EzIPC] public Func<uint, object> OpenVendorResults;

    /// <summary> uint itemId, bool filterNoLocation </summary>
    [EzIPC] public Func<uint, bool, HashSet<(uint npcId, uint territory, (float x, float y))>> GetItemVendors;

    /// <summary> uint npcId </summary>
    [EzIPC] public Func<uint, (uint territory, (float x, float y))?> GetVendorLocation;
}
