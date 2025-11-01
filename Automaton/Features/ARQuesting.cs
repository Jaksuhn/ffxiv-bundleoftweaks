using Automaton.Tasks;

namespace Automaton.Features;

public class ARQuestingConfiguration
{
    [BoolConfig] public bool ReturnHome = true;
}

[Tweak]
[Requires(Ipc.AutoRetainer | Ipc.Lifestream | Ipc.Questionable)]
internal class ARQuesting : ARTweak<ARQuestingConfiguration>
{
    public override string Name => "AutoRetainer x Questionable";
    public override string Description => "On CharacterPostProcess, do any seasonal quests that are available.";

    private List<string> _quests = [];

    public override void OnCharacterPostProcessStep()
    {
        if (Service.Questionable.GetCurrentlyActiveEventQuests() is { Count: > 0 } quests)
        {
            _quests = quests;
            AutoRetainer.RequestCharacterPostprocess();
        }
        else
            Log("Skipping post process for character: no seasonal quests available.");
    }

    public override void OnCharacterReadyToPostProcess() => Service.Automation.Start(new RunQuestionable(_quests, Config.ReturnHome), AutoRetainer.FinishCharacterPostProcess);
}
