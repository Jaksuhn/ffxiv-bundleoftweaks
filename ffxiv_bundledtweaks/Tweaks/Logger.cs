using FFXIVClientStructs.FFXIV.Client.Game;
using System.Reflection;

namespace ComplexTweaks.Tweaks;

[Tweak(debug: true)]
public unsafe partial class DebugLogging : Tweak {
    public override string Name => "Logger";
    public override string Description => "It just logs random hooks.";

    private readonly uint[] _blacklist = [1, 4, 31, 32, 96, 97, 98, 99, 101, 104, 105, 106, 110, 142, 144, 148, 1003, 1005, 1006, 1007, 1008]; // these are checked every frame

    [AddressHook<Conditions>(nameof(Conditions.MemberFunctionPointers.HasPermission))]
    internal unsafe bool HasPermission(Conditions* thisPtr, uint permissionId, int excludedCondition1 = 0, int excludedCondition2 = 0) {
        var ret = HasPermissionHook.Original(thisPtr, permissionId, excludedCondition1, excludedCondition2);
        if (!_blacklist.Contains(permissionId))
            MethodBase.GetCurrentMethod()?.Log([(nint)thisPtr, permissionId, excludedCondition1, excludedCondition2], ret);
        return ret;
    }

    [AddressHook<GameMain>(nameof(GameMain.MemberFunctionPointers.ExecuteCommand))]
    internal unsafe bool ExecuteCommand(int command, int param1 = 0, int param2 = 0, int param3 = 0, int param4 = 0) {
        var ret = ExecuteCommandHook.Original(command, param1, param2, param3, param4);
        MethodBase.GetCurrentMethod()?.Log([(CommandFlag)command, param1, param2, param3, param4], additionalValues: ret);
        return ret;
    }

    [AddressHook<GameMain>(nameof(GameMain.MemberFunctionPointers.ExecuteLocationCommand))]
    internal bool ExecuteLocationCommand(int command, Vector3* location, int param1 = 0, int param2 = 0, int param3 = 0, int param4 = 0) {
        var ret = ExecuteLocationCommandHook.Original(command, location, param1, param2, param3, param4);
        MethodBase.GetCurrentMethod()?.Log([(LocationCommandFlag)command, *location, param1, param2, param3, param4], ret);
        return ret;
    }
}
