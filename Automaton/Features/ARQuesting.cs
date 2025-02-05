using Automaton.Tasks;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace Automaton.Features;

[Tweak, Requirement(AutoRetainerIPC.Name, AutoRetainerIPC.Repo), Requirement(QuestionableIPC.Name, QuestionableIPC.Repo)]
internal class ARQuesting : ARTweak
{
    public override string Name => "AutoRetainer x Questionable";
    public override string Description => "On CharacterPostProcess, do any seasonal quests that are available.";

    private List<string> _quests = [];

    public override void OnCharacterPostProcessStep()
    {
        if (Service.Questionable.GetCurrentlyActiveEventQuests().Where(q => uint.TryParse(q, out var id) && !QuestManager.IsQuestComplete(id)).ToList() is { Count: > 0 } quests)
        {
            _quests = quests;
            AutoRetainer.RequestCharacterPostprocess();
        }
        else
            Log("Skipping post process for character: no seasonal quests available.");
    }

    public override void OnCharacterReadyToPostProcess() => Service.Automation.Start(new DoQuests(_quests), AutoRetainer.FinishCharacterPostProcess);
}
