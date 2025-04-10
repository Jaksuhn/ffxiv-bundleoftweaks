using Automaton.Tasks;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace Automaton.Features;

[Tweak]
internal class ARQuesting : ARTweak
{
    public override string Name => "AutoRetainer x Questionable";
    public override string Description => "On CharacterPostProcess, do any seasonal quests that are available.";
    public override BaseIPC[] Requirements => [Service.AutoRetainerIPC, Service.Lifestream, Service.Questionable];

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

    public override void OnCharacterReadyToPostProcess() => Service.Automation.Start(new DoQuests(_quests), AutoRetainer.FinishCharacterPostProcess);
}
