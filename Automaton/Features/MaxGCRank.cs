using ECommons.EzHookManager;
using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace Automaton.Features;

[Tweak]
internal class MaxGCRank : Tweak
{
    public override string Name => "Enforce Expert Delivery";
    public override string Description => "Automatically maxes your GC rank to force the expert delivery window to show. Does not bypass anything else rank-restricted.";

    internal unsafe EzHook<PlayerState.Delegates.GetGrandCompanyRank>? Hook = new((nint)PlayerState.MemberFunctionPointers.GetGrandCompanyRank, Detour, false);
    internal static unsafe byte Detour(PlayerState* thisPtr) => 17;
}
