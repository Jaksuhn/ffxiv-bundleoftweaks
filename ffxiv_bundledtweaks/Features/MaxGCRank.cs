using ECommons.EzHookManager;
using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace ComplexTweaks.Features;

[Tweak]
internal class MaxGCRank : Tweak
{
    public override string Name => "Enforce Expert Delivery";
    public override string Description => "Automatically maxes your GC rank to force the expert delivery window to show. Does not bypass anything else rank-restricted. Only in effect if you do not have expert delivery unlocked.";

    public override unsafe void Enable()
    {
        Hook ??= new((nint)PlayerState.MemberFunctionPointers.GetGrandCompanyRank, Detour, false);
        Hook.Enable();
    }

    public override void Disable() => Hook?.Pause();

    internal unsafe EzHook<PlayerState.Delegates.GetGrandCompanyRank>? Hook;
    internal unsafe byte Detour(PlayerState* thisPtr)
    {
        var ret = Hook!.Original(thisPtr);
        return ret < 6 ? (byte)17 : ret;
    }
}
