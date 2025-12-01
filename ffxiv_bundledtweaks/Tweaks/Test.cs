using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using System.Reflection;

namespace ComplexTweaks.Tweaks;

[Tweak(debug: true)]
public unsafe partial class DebugLogging : Tweak {
    public override string Name => "Logger";
    public override string Description => "It just logs random hooks.";

    [SigHook("E8 ?? ?? ?? ?? 41 89 45 38")]
    private unsafe nint RaptureAtkModule_OpenYesNo(RaptureAtkModule* thisPtr, nint addonText, nint a3, nint a4, nint a5, nint a6, sbyte a7, short a8, int a9, nint a10,
        nint a11, int countdownSeconds, nint a13, byte a14, byte a15, byte a16, byte a17, byte a18, int a19, sbyte a20, sbyte a21, sbyte a22) {
        MethodBase.GetCurrentMethod()?.Log([(nint)thisPtr, addonText, a3, a4, a5, a6, a7, a8, a9, a10,
            a11, countdownSeconds, a13, a14, a15, a16, a17, a18, a19, a20, a21, a22]);
        return RaptureAtkModule_OpenYesNoHook.Original(thisPtr, addonText, a3, a4, a5, a6, a7, a8, a9, a10,
            a11, countdownSeconds, a13, a14, a15, a16, a17, a18, a19, a20, a21, a22);
    }

    [SigHook("E8 ?? ?? ?? ?? 8B C8 B3 01")]
    private unsafe uint RaptureAtkModule_OpenYesNo2(RaptureAtkModule* thisPtr, nint structPtr) {
        MethodBase.GetCurrentMethod()?.Log([(nint)thisPtr, structPtr]);
        return RaptureAtkModule_OpenYesNo2Hook.Original(thisPtr, structPtr);
    }

    [SigHook("40 56 41 56 48 83 EC 48 48 8B F1 48 89 5C 24 ??")]
    private unsafe nint* LogoutCallbackInterface_OnLogout(nint a1, nint* a2) {
        MethodBase.GetCurrentMethod()?.Log([a1, (nint)a2]);
        return LogoutCallbackInterface_OnLogoutHook.Original(a1, a2);
    }

    private readonly uint[] _blacklist = [1, 4, 31, 32, 96, 97, 98, 99, 101, 104, 105, 106, 142, 144, 148, 1003, 1005, 1006, 1007, 1008]; // these are checked every frame

    [AddressHook<Conditions>(nameof(Conditions.MemberFunctionPointers.HasPermission))]
    internal unsafe bool HasPermission(Conditions* thisPtr, uint permissionId, int excludedCondition1 = 0, int excludedCondition2 = 0) {
        var ret = HasPermissionHook.Original(thisPtr, permissionId, excludedCondition1, excludedCondition2);
        if (!_blacklist.Contains(permissionId))
            MethodBase.GetCurrentMethod()?.Log([(nint)thisPtr, permissionId, excludedCondition1, excludedCondition2], ret);
        return ret;
    }
}
