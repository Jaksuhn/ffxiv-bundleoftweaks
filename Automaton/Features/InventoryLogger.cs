using ECommons.EzHookManager;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace Automaton.Features;
[Tweak(disabled: true)]
internal class InventoryLogger : Tweak
{
    public override string Name => "Inventory Logger";
    public override string Description => "";

    internal static unsafe EzHook<InventoryManager.Delegates.MoveItemSlot>? Hook = new((nint)InventoryManager.MemberFunctionPointers.MoveItemSlot, Detour, false);
    private static unsafe int Detour(InventoryManager* thisPtr, InventoryType srcContainer, ushort srcSlot, InventoryType dstContainer, ushort dstSlot, byte unk)
    {
        Svc.Log.Info($"MoveItemSlot({srcContainer}, {srcSlot}, {dstContainer}, {dstSlot}, {unk})");
        return Hook.Original(thisPtr, srcContainer, srcSlot, dstContainer, dstSlot, unk);
    }
}
