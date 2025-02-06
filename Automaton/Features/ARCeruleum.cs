using Automaton.Tasks;

namespace Automaton.Features;

[Tweak]
internal class ARCeruleum : ARTweak
{
    public override string Name => "AutoRetainer x Ceruleum";
    public override string Description => "On CharacterPostProcess, refill the stack of ceruleum tanks. Triggers when inventory has <200.";
    public override BaseIPC[] Requirements => [Service.AutoRetainerIPC, Service.Lifestream, Service.Navmesh];

    private const uint CeruleumTankId = 10155;

    public override void OnCharacterPostProcessStep()
    {
        if (Service.AutoRetainerApi.GetOfflineCharacterData(Player.CID).EnabledSubs.Count > 0 && Inventory.GetItemCount(CeruleumTankId, false) <= 200)
            AutoRetainer.RequestCharacterPostprocess();
        else
            Log("Skipping post process for character: inventory above threshold.");
    }

    public override void OnCharacterReadyToPostProcess() => Service.Automation.Start(new BuyCeruleumTanks(), AutoRetainer.FinishCharacterPostProcess);
}
