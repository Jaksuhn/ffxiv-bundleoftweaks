using Dalamud.Game.Inventory;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using System.Threading.Tasks;

namespace ComplexTweaks.Tasks;
public sealed class LoopMelding(GameInventoryItem item) : CommonTasks
{
    private static readonly uint GettingTooAttachedVII = 1905;
    protected override async Task Execute()
    {
        Status = $"Getting Achievement Progress";
        var (current, max) = await GetAchievementProgress(GettingTooAttachedVII, $"GetProgress{nameof(GettingTooAttachedVII)}");
        try
        {
            while (current < max)
            {
                Status = $"Melding [{current}/{max}]";
                Meld();
                await WaitUntilThenFalse(() => Svc.Condition[ConditionFlag.MeldingMateria], "Melding");

                Status = $"Retrieving [{current}/{max}]";
                unsafe { EventFramework.Instance()->MaterializeItem((InventoryItem*)item.Address, MaterializeEntryId.Retrieve); }
                await WaitUntilThenFalse(() => Svc.Condition[ConditionFlag.Occupied39], "Retrieving");
                current++;
            }
        }
        finally
        {
            unsafe { AgentMateriaAttach.Instance()->Hide(); }
        }
    }

    private unsafe void Meld()
    {
        var agent = AgentModule.Instance()->GetAgentByInternalId(AgentId.MateriaAttach);
        agent->Show();
    }
}
