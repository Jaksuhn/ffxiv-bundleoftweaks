using ECommons.EzIpcManager;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;

namespace Automaton.IPC;

#nullable disable
public class GearsetterIPC : BaseIPC
{
    public override string Name => "Gearsetter";
    public override string Repo => "https://git.carvel.li/liza/";
    public GearsetterIPC() => EzIPC.Init(this, Name);

    [EzIPC] public Func<byte, List<(uint ItemId, InventoryType? SourceInventory, byte? SourceInventorySlot, RaptureGearsetModule.GearsetItemIndex TargetSlot)>> GetRecommendationsForGearset;
}
