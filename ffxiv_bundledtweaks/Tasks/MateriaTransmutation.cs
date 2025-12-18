using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;
using System.Threading.Tasks;

namespace ComplexTweaks.Tasks;

public sealed class MateriaTransmutation : TaskBase {
    protected override Task Execute() {
        if (GetMateria() is { Count: > 0 } materia)
            SetMateria([.. materia.Where(m => m.Type == MateriaWrapper.MateriaType.Combat)]);
        return Task.CompletedTask;
    }

    private unsafe List<MateriaWrapper> GetMateria() {
        List<MateriaWrapper> materia = [];
        foreach (var row in FindRows<Materia>(x => x.Item.FirstOrDefault().RowId != 0))
            foreach (var item in row.Item)
                if (item.RowId != 0)
                    materia.Add(new MateriaWrapper(item.RowId));
        return [.. materia.Where(m => m.Quantity > 0)];
    }

    private unsafe void SetMateria(List<MateriaWrapper> combatMateria) {
        if (combatMateria.Sum(m => m.Quantity) < 5) return;

        var agent = &UIState.Instance()->MateriaTrade;

        agent->MateriaId1 = 0; agent->Quantity1 = 0;
        agent->MateriaId2 = 0; agent->Quantity2 = 0;
        agent->MateriaId3 = 0; agent->Quantity3 = 0;
        agent->MateriaId4 = 0; agent->Quantity4 = 0;
        agent->MateriaId5 = 0; agent->Quantity5 = 0;

        var sortedDistinctCombatMateria = combatMateria.OrderBy(m => m.Quantity).ToList();

        var typesInAgentSlots = new List<MateriaWrapper>();
        var quantityPerTypeInAgent = new Dictionary<uint, ushort>();
        var currentTotalQuantitySet = 0;

        foreach (var mat in sortedDistinctCombatMateria) {
            if (typesInAgentSlots.Count < 5) {
                typesInAgentSlots.Add(mat);
                quantityPerTypeInAgent[mat.ItemId] = 1;
                currentTotalQuantitySet++;
            }
            else {
                break;
            }
        }

        if (currentTotalQuantitySet == 0 && combatMateria.Any()) {
            return;
        }

        var quantityStillToDistribute = 5 - currentTotalQuantitySet;
        var distributorIndex = 0;
        var attemptsSinceLastSuccess = 0;

        while (quantityStillToDistribute > 0) {
            if (!typesInAgentSlots.Any() || attemptsSinceLastSuccess >= typesInAgentSlots.Count) {
                break;
            }

            var matToTryIncrement = typesInAgentSlots[distributorIndex % typesInAgentSlots.Count];
            if (quantityPerTypeInAgent[matToTryIncrement.ItemId] < matToTryIncrement.Quantity) {
                quantityPerTypeInAgent[matToTryIncrement.ItemId]++;
                quantityStillToDistribute--;
                attemptsSinceLastSuccess = 0;
            }
            else {
                attemptsSinceLastSuccess++;
            }
            distributorIndex++;
        }

        if (quantityStillToDistribute > 0) {
            return;
        }

        var agentMateriaIdFields = new ushort*[] { &agent->MateriaId1, &agent->MateriaId2, &agent->MateriaId3, &agent->MateriaId4, &agent->MateriaId5 };
        var agentQuantityFields = new ushort*[] { &agent->Quantity1, &agent->Quantity2, &agent->Quantity3, &agent->Quantity4, &agent->Quantity5 };

        for (var i = 0; i < typesInAgentSlots.Count; i++) {
            var mat = typesInAgentSlots[i];
            *agentMateriaIdFields[i] = (ushort)mat.ItemId;
            *agentQuantityFields[i] = quantityPerTypeInAgent[mat.ItemId];
        }
    }

    private class MateriaWrapper(uint itemId) {
        public uint ItemId { get; } = itemId;
        public int Quantity => Inventory.GetItemCount(ItemId, false);
        public MateriaType Type => GetRow<Materia>(ItemId)!.Value.BaseParam.RowId switch {
            70 or 71 or 11 => MateriaType.Crafting, // craftsmanship, control, cp
            72 or 73 or 10 => MateriaType.Gathering, // gathering, perception, gp
            _ => MateriaType.Combat,
        };

        public enum MateriaType {
            Combat,
            Crafting,
            Gathering,
        }
    }
}
