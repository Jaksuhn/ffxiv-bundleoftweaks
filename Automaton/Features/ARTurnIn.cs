using Automaton.Tasks;
using ImGuiNET;

namespace Automaton.Features;

public class ARTurnInConfiguration
{
    [IntConfig(DefaultValue = 50, Min = 0, Max = 140)]
    public int InventoryFreeSlotThreshold = 50;

    public List<ulong> ExcludedCharacters = [];
}

[Tweak, Requirement(NavmeshIPC.Name, NavmeshIPC.Repo), Requirement(AutoRetainerIPC.Name, AutoRetainerIPC.Repo), Requirement(DeliverooIPC.Name, DeliverooIPC.Repo), Requirement(LifestreamIPC.Name, LifestreamIPC.Repo)]
internal class ARTurnIn : ARTweak<ARTurnInConfiguration>
{
    public override string Name => "AutoRetainer x Deliveroo";
    public override string Description => "On CharacterPostProcess, automatically go to your grand company and turn in your gear when inventory is below a certain threshold.";

    public override void DrawConfig()
    {
        base.DrawConfig();

        if (!Config.ExcludedCharacters.Contains(Svc.ClientState.LocalContentId))
        {
            if (ImGui.Button("Exclude Current Character"))
                Config.ExcludedCharacters.Add(Svc.ClientState.LocalContentId);
        }
        else
        {
            if (ImGui.Button("Remove Character Exclusion"))
                Config.ExcludedCharacters.Remove(Svc.ClientState.LocalContentId);
        }
    }

    public override void OnCharacterPostProcessStep()
    {
        if (Config.ExcludedCharacters.Any(x => x == Svc.ClientState.LocalContentId))
            Log("Skipping post process turn in for character: character excluded.");
        else
        {
            if (Service.AutoRetainerIPC.GetInventoryFreeSlotCount() <= Config.InventoryFreeSlotThreshold)
                AutoRetainer.RequestCharacterPostprocess();
            else
                Log("Skipping post process for character: inventory above threshold.");
        }
    }

    public override void OnCharacterReadyToPostProcess() => Service.Automation.Start(new AutoDeliveroo(), AutoRetainer.FinishCharacterPostProcess);
}
