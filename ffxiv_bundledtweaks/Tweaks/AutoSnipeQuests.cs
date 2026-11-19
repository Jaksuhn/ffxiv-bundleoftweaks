using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Common.Lua;
using Lumina.Excel.Sheets;

namespace ComplexTweaks.Tweaks;

public unsafe partial class AutoSnipeQuests : Tweak {
    public override string Name => "Sniper no sniping";
    public override string Description => "Automatically completes snipe quests.";

    private enum SnipeResult : byte {
        Cleared = 1,
        Failed = 2,
        TimeUp = 3
    }

    [SigHook("48 89 5C 24 ?? 48 89 6C 24 ?? 48 89 74 24 ?? 57 48 83 EC 50 48 8B F9 48 8D 4C 24 ??")] // from xan
    private ulong EnqueueSnipeTask(EventSceneModuleImplBase* scene, lua_State* state) {
        try {
            var val = state->top;
            val->tt = 3;
            val->value.n = IsTimedObserve(state) ? SnipeResult.TimeUp.Value : SnipeResult.Cleared.Value;
            state->top += 1;
            return 1;
        }
        catch {
            return EnqueueSnipeTaskHook.Original.Invoke(scene, state);
        }
    }

    private static bool IsTimedObserve(lua_State* state)
        => state->lua_gettop() >= 2 && Snipe.TryGetRow((uint)state->lua_tonumber(2), out var row) && row.Unknown21 > 0;
}
