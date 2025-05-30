using Automaton.Tasks;

namespace Automaton.Features;

public class ARTurnInConfiguration
{
    [IntConfig(DefaultValue = 50, Min = 0, Max = 140)]
    public int InventoryFreeSlotThreshold = 50;

    [IntConfig(DefaultValue = 50, Min = 1, Max = 65000)]
    public int VenturesRemaining = 50;

    [BoolConfig]
    public bool EquipGearsetterRecs = false;

    [CharacterBlacklistConfig]
    public List<ulong> ExcludedCharacters = [];
}

[Tweak]
internal class ARTurnIn : ARTweak<ARTurnInConfiguration>
{
    public override string Name => "AutoRetainer x Deliveroo";
    public override string Description => "On CharacterPostProcess, automatically go to your grand company and turn in gear when below an inventory or venture threshold";
    public override BaseIPC[] Requirements => [Service.AutoRetainerIPC, Service.Navmesh, Service.Deliveroo, Service.Lifestream];

    public override void OnCharacterPostProcessStep()
    {
        if (Config.ExcludedCharacters.Any(x => x == Svc.ClientState.LocalContentId))
            Log("Skipping post process turn in for character: character excluded.");
        else
        {
            if (Service.AutoRetainerIPC.GetInventoryFreeSlotCount() <= Config.InventoryFreeSlotThreshold || Inventory.GetItemCount(21072, false) is { } v && v > 0 && v <= Config.VenturesRemaining)
                AutoRetainer.RequestCharacterPostprocess();
            else
                Log("Skipping post process for character: inventory and ventures above threshold.");
        }
    }

    public override void OnCharacterReadyToPostProcess() => Service.Automation.Start(new AutoDeliveroo(Config.EquipGearsetterRecs), AutoRetainer.FinishCharacterPostProcess);
}
