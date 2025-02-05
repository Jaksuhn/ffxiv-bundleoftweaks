using Automaton.Tasks;

namespace Automaton.Features;

[Tweak, Requirement(AutoRetainerIPC.Name, AutoRetainerIPC.Repo)]
internal class ARCeruleum : ARTweak
{
    public override string Name => "AutoRetainer x Ceruleum";
    public override string Description => "On CharacterPostProcess, refill the stack of ceruleum tanks. Triggers when inventory has <200 and you're in a workshop.";

    private const uint CeruleumTankId = 10155;
    private static readonly uint[] CompanyWorkshopTerritories = [423, 424, 425, 653, 984];

    public override void OnCharacterPostProcessStep()
    {
        if (Service.AutoRetainerApi.GetOfflineCharacterData(Player.CID).EnabledSubs.Count > 0 && Inventory.GetItemCount(CeruleumTankId, false) <= 200 && CompanyWorkshopTerritories.Contains(Player.Territory))
            AutoRetainer.RequestCharacterPostprocess();
        else
            Log("Skipping post process for character: inventory above threshold or not in workshop.");
    }

    public override void OnCharacterReadyToPostProcess() => Service.Automation.Start(new BuyCeruleumTanks(), AutoRetainer.FinishCharacterPostProcess);
}
