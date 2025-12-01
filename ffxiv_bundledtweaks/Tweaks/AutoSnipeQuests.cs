using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Common.Lua;

namespace ComplexTweaks.Tweaks;

[Tweak]
public unsafe partial class AutoSnipeQuests : Tweak {
    public override string Name => "Sniper no sniping";
    public override string Description => "Automatically completes snipe quests.";

    [SigHook(Memory.Signatures.EnqueueSnipeTask)]
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
