using Lumina.Excel.Sheets;

namespace ComplexTweaks.Features;

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

        // TODO: add a check if there are any seats left to get into
        var target = Svc.Party.FirstOrDefault(o => o?.EntityId != Player.Object.GameObjectId && o?.GameObject?.YalmDistanceX < 3 && GetRow<Mount>(o.GameObject.Character()->Mount.MountId)!.Value.ExtraSeats > 0, null);
        if (target != null && target.GameObject != null && Service.Memory.RidePillion != null)
        {
            TaskManager.Enqueue(() => Debug("Detected mounted party member with extra seats, mounting..."));
            TaskManager.Enqueue(() => Service.Memory.RidePillion!(target.GameObject.BattleChara(), 10));
            TaskManager.Enqueue(() => Svc.Condition[ConditionFlag.Mounted]);
        }
    }
}
