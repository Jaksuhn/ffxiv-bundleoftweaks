using ECommons.EzHookManager;
using FFXIVClientStructs.FFXIV.Client.Game;
using static ComplexTweaks.Services.Memory;

namespace ComplexTweaks.Tweaks;

[Tweak]
public class Test : Tweak
{
    public override string Name => "Test";
    public override string Description => "";

    public HasPermissionChecker PermissionChecker = new();

    public override void Enable()
    {
        PermissionChecker.HasPermissionHook.Enable();
        Svc.ClientState.LevelChanged += OnLevelChanged;
    }

    public override void Disable()
    {
        PermissionChecker.HasPermissionHook.Disable();
        Svc.ClientState.LevelChanged -= OnLevelChanged;
    }

    private void OnLevelChanged(uint classJobId, uint level)
    {
        Svc.Log.Info($"{nameof(OnLevelChanged)} {classJobId} / {level}");
    }

    public class HasPermissionChecker : Hook
    {
        [EzHook(Signatures.HasPermission, false)]
        internal EzHook<Memory.Delegates.HasPermissionDelegate> HasPermissionHook = null!;

        private readonly uint[] _blacklist = [1, 4, 31, 32, 96, 97, 98, 99, 101, 104, 105, 106, 142, 144, 1005, 1006, 1007, 1008]; // these are checked every frame
        internal unsafe bool HasPermissionDetour(Conditions* @this, uint permissionId, int excludedCondition1 = 0, int excludedCondition2 = 0)
        {
            var ret = HasPermissionHook.Original.Invoke(@this, permissionId, excludedCondition1, excludedCondition2);
            if (!_blacklist.Contains(permissionId))
                Svc.Log.Info($"Checking permission: {permissionId} [{excludedCondition1}, {excludedCondition2}] [{ret}]");
            return ret;
        }
    }
}
