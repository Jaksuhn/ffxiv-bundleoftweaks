using ECommons.EzIpcManager;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;

namespace ComplexTweaks.IPC;

#nullable disable
[Ipc(Ipc.Gearsetter)]
public class GearsetterIPC : BaseIPC
{
    public override string Name => "Gearsetter";
    public override string Repo => Vera;
    public GearsetterIPC() => EzIPC.Init(this, Name);

    [EzIPC] public Func<byte, List<(uint ItemId, InventoryType? SourceInventory, byte? SourceInventorySlot, RaptureGearsetModule.GearsetItemIndex TargetSlot)>> GetRecommendationsForGearset;
}
