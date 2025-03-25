using Dalamud.Game.Inventory;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using System.Threading.Tasks;

namespace Automaton.Tasks;
public sealed class LoopMelding(GameInventoryItem item) : CommonTasks
{
    private static readonly uint GettingTooAttachedVII = 1905;
    protected override async Task Execute()
    {
        Status = $"Getting Achievement Progress";
        var (current, max) = await GetAchievementProgress(GettingTooAttachedVII, $"GetProgress{nameof(GettingTooAttachedVII)}");
        while (current < max)
        {
            Status = $"Melding [{current}/{max}]";
            Meld();
            await WaitUntilThenFalse(() => Svc.Condition[ConditionFlag.MeldingMateria], "Melding");

            Status = $"Retrieving [{current}/{max}]";
            Service.Memory.MaterializeAction(item, MaterializeEntryId.Retrieve);
            await WaitUntilThenFalse(() => Svc.Condition[ConditionFlag.Occupied39], "Retrieving");
            current++;
        }
    }

    private unsafe void Meld()
    {
        var agent = AgentModule.Instance()->GetAgentByInternalId(AgentId.MateriaAttach);
        agent->Show();
    }
}
