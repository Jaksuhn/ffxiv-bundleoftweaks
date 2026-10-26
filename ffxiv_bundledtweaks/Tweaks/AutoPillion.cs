using FFXIVClientStructs.FFXIV.Client.Game;

namespace ComplexTweaks.Tweaks;

public class AutoPillion : Tweak {
    public override string Name => "Auto Pillion";
    public override string Description => "Automatically hop in to other peoples' mounts when you are near them.";

    private long cooldownUntil;

    public override void OnEnable() {
        IFramework.Get().Update += OnUpdate;
        ICondition.Get().ConditionChange += OnConditionChange;
    }

    public override void OnDisable() {
        IFramework.Get().Update -= OnUpdate;
        ICondition.Get().ConditionChange -= OnConditionChange;
        cooldownUntil = 0;
    }

    private void OnConditionChange(ConditionFlag flag, bool value) {
        if (flag == ConditionFlag.RidingPillion && !value)
            cooldownUntil = Environment.TickCount64 + 5000; // just blanket re-pillioning for 5s
    }

    private void OnUpdate(IFramework framework) {
        if (IObjectTable.Get().LocalPlayer is not { Available: true, IsBusy: false, GameObjectId: var playerId } || ICondition.Get()[ConditionFlag.Mounted]) {
            if (Automation.Running)
                Automation.Stop();
            return;
        }

        if (Automation.Running || Environment.TickCount64 < cooldownUntil)
            return;

        if (IPartyList.Get().FirstOrDefault(o => o?.EntityId != playerId && o?.GameObject?.CurrentDistance < 3 && o.GameObject.CanRidePillion(), null) is { GameObject.EntityId: var entityId }) {
            Automation.Start(AutoTask.From(async t => {
                t.Log("Detected mounted party member with extra seats, mounting...");
                GameMain.ExecuteCommand(CommandFlag.RidePillion, (int)entityId, 10);
                await t.WaitUntil(() => ICondition.Get()[ConditionFlag.Mounted], "Mounted", timeout: TimeSpan.FromSeconds(5));
            }, name: "AutoPillion"));
        }
    }
}
