using ComplexTweaks.Tasks;

namespace ComplexTweaks.Tweaks;

[Tweak]
[Requires(Ipc.AutoRetainer | Ipc.Lifestream | Ipc.Navmesh)]
internal class ARCeruleum : ARTweak {
    public override string Name => "AutoRetainer x Ceruleum";
    public override string Description => "On CharacterPostProcess, refill the stack of ceruleum tanks. Triggers when inventory has <200.";
    private ItemHandle CeruleumTank => new(10155);

    public override void OnCharacterPostProcessStep() {
        if (Service.AutoRetainerApi.GetOfflineCharacterData(Player.CID).EnabledSubs.Count > 0 && CeruleumTank.GetCount(false) <= 200)
            AutoRetainer.RequestCharacterPostprocess();
        else
            Log("Skipping post process for character: inventory above threshold.");
    }

    public override void OnCharacterReadyToPostProcess() => Service.Automation.Start(new BuyCeruleumTanks(), AutoRetainer.FinishCharacterPostProcess);
}
