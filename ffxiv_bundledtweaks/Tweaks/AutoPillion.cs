using FFXIVClientStructs.FFXIV.Client.Game.Character;
using Lumina.Excel.Sheets;

namespace ComplexTweaks.Tweaks;

[Tweak]
public class AutoPillion : Tweak
{
    public override string Name => "Auto Pillion";
    public override string Description => "Automatically hop in to other peoples' mounts when you are near them.";

    public override void Enable() => Svc.Framework.Update += OnUpdate;
    public override void Disable() => Svc.Framework.Update -= OnUpdate;

    private unsafe void OnUpdate(IFramework framework)
    {
        if (!Player.Available || Player.IsBusy || Svc.Condition[ConditionFlag.Mounted])
        {
            if (TaskManager.Tasks.Count > 0)
                TaskManager.Abort();
            return;
        }

        var target = Svc.Party.FirstOrDefault(o => o?.EntityId != Player.Object.GameObjectId && o?.GameObject?.YalmDistanceX < 3 && HasMountSpace(o.GameObject.Character()->Mount), null);
        if (target != null && target.GameObject != null && Service.Memory.RidePillion != null)
        {
            TaskManager.Enqueue(() => Debug("Detected mounted party member with extra seats, mounting..."));
            TaskManager.Enqueue(() => Service.Memory.RidePillion!(target.GameObject.BattleChara(), 10));
            TaskManager.Enqueue(() => Svc.Condition[ConditionFlag.Mounted]);
        }
    }

    private unsafe bool HasMountSpace(MountContainer cont)
    {
        var capacity = GetRow<Mount>(cont.MountId)?.ExtraSeats ?? 0;
        return cont.MountedEntityIds[1..].ToArray().Count(x => x != 0) < capacity;
    }
}
