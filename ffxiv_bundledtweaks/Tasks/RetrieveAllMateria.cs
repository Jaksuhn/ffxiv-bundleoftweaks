using Dalamud.Game.Inventory;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using System.Threading.Tasks;

namespace ComplexTweaks.Tasks;
public sealed class RetrieveAllMateria(GameInventoryItem item) : CommonTasks
{
    protected override async Task Execute()
    {
        Status = $"Retrieving Materia";
        var materias = item.Materia.ToArray().Where(x => x != 0);
        foreach (var materia in materias)
        {
            unsafe { EventFramework.Instance()->MaterializeItem((InventoryItem*)item.Address, MaterializeEntryId.Retrieve); }
            await WaitUntilThenFalse(() => Svc.Condition[ConditionFlag.Occupied39], "RetrievingMateria");
        }
    }
}
