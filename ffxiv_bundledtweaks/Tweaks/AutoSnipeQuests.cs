using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Common.Lua;

namespace ComplexTweaks.Tweaks;

[Tweak]
public unsafe partial class AutoSnipeQuests : Tweak {
    public override string Name => "Sniper no sniping";
    public override string Description => "Automatically completes snipe quests.";

    [SigHook("48 89 5C 24 ?? 48 89 6C 24 ?? 48 89 74 24 ?? 57 48 83 EC 50 48 8B F9 48 8D 4C 24 ??")] // from xan
    private ulong EnqueueSnipeTask(EventSceneModuleImplBase* scene, lua_State* state) {
        try {
            var val = state->top;
            val->tt = 3;
            val->value.n = 1;
            state->top += 1;
            return 1;
        }
        catch {
            return EnqueueSnipeTaskHook.Original.Invoke(scene, state);
        }
    }
}
