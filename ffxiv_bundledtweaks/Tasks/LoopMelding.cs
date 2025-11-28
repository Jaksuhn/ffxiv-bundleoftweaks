using Dalamud.Game.Inventory;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System.Threading.Tasks;
using ValueType = FFXIVClientStructs.FFXIV.Component.GUI.ValueType;

namespace ComplexTweaks.Tasks;

public sealed partial class LoopMelding(GameInventoryItem item) : CommonTasks
{
    private static readonly uint GettingTooAttachedVII = 1905;
    private static unsafe bool AgentActive => AgentMateriaAttach.Instance()->IsAgentActive();
    private static unsafe bool AgentLoading => AgentMateriaAttach.Instance()->UpdateState != 0;

    protected override async Task Execute()
    {
        Status = $"Getting Achievement Progress";
        var (_, current, max) = await GetAchievementProgress(GettingTooAttachedVII);
        try
        {
            while (current < max)
            {
                Status = $"Melding [{current}/{max}]";
                await Meld();

                Status = $"Retrieving [{current}/{max}]";
                await Retrieve();
                current++;
            }
        }
        finally
        {
            unsafe { AgentMateriaAttach.Instance()->Hide(); }
        }
    }

    private async Task<(uint id, uint current, uint max)> GetAchievementProgress(uint achievementId)
    {
        using var scope = BeginScope($"WaitingOn#{achievementId}");
        unsafe { Achievement.Instance()->RequestAchievementProgress(achievementId); }
        return await WaitForReceiveAchievementProgress(id: achievementId);
    }

    [AddressHook<Achievement>(nameof(Achievement.MemberFunctionPointers.ReceiveAchievementProgress))]
    private unsafe void ReceiveAchievementProgress(Achievement* achievement, uint id, uint current, uint max)
        => ReceiveAchievementProgressHook.Original(achievement, id, current, max);

    private async Task Meld()
    {
        using var scope = BeginScope(nameof(Meld));
        await Open();
        await SelectItem();
        await WaitWhile(() => AgentLoading, "WaitAgentLoad");

        if (GetUsableMateria() is not { } materia)
        {
            Error($"No materia that can be guaranteed melded to {item.GameData.Value.Name}");
            return;
        }
        await SelectMateria(materia);
        await HandleMateriaAttachDialog();
    }

    private async Task Retrieve()
    {
        using var scope = BeginScope(nameof(Retrieve));
        unsafe { EventFramework.Instance()->MaterializeItem((InventoryItem*)item.Address, MaterializeEntryId.Retrieve); }
        await WaitUntilThenFalse(() => Svc.Condition[ConditionFlag.Occupied39], "Retrieving");
    }

    private async Task Open()
    {
        using var scope = BeginScope(nameof(Open));
        if (AgentActive) return;
        bool res;
        unsafe { res = ActionManager.Instance()->UseAction(ActionType.GeneralAction, 12); }
        ErrorIf(!res, "Unable to open melding addon");
        await WaitUntil(() => AgentActive, $"WaitForAgent");
    }

    private async Task SelectItem()
    {
        using var scope = BeginScope(nameof(SelectItem));
        var category = GetCategory(item);
        ErrorIf(category is AgentMateriaAttach.FilterCategory.None, $"{item.GameData.Value.Name} has no inventory category");

        unsafe
        {
            if (AgentMateriaAttach.Instance()->Category != category)
                ReceiveEvent(0, [0, (int)category]);
        }

        await WaitWhile(() => AgentLoading, "WaitAgentLoad");

        unsafe
        {
            var agent = AgentMateriaAttach.Instance();
            var it = item.ToPtr();
            for (var i = 0; i < agent->ItemCount; i++)
            {
                if (it == agent->Data->ItemsSorted[i].Value->Item)
                {
                    Log($"Selecting item at index #{i}");
                    ReceiveEvent(0, [1, i, 1, 0]);
                    return;
                }
            }

            throw new KeyNotFoundException($"{item.GameData.Value.Name} not found");
        }
    }

    private async Task SelectMateria(uint id)
    {
        using var scope = BeginScope(nameof(SelectMateria));
        await WaitWhile(() => AgentLoading, "WaitAgentLoad");
        unsafe
        {
            var agent = AgentMateriaAttach.Instance();
            for (var i = 0; i < agent->MateriaCount; i++)
            {
                var invItem = agent->Data->MateriaSorted[i].Value->Item;
                if (invItem->ItemId == id)
                {
                    Log($"Selecting materia at index {i}");
                    ReceiveEvent(0, [2, i, 1, 0]);
                    return;
                }
            }

            throw new KeyNotFoundException($"Materia #{id} not found");
        }
    }

    private async Task HandleMateriaAttachDialog()
    {
        using var scope = BeginScope(nameof(HandleMateriaAttachDialog));
        await WaitUntil(() => Svc.Condition[ConditionFlag.MeldingMateria], "WaitForMeldState");
        await WaitUntil(() => Game.AddonActive("MateriaAttachDialog"), "WaitForDialog");
        Game.ProgressMateriaAttachDialog();
        await WaitWhile(() => Svc.Condition[ConditionFlag.MeldingMateria], "WaitForMeldFinish");
    }

    private unsafe void ReceiveEvent(ulong eventKind, int[] values)
    {
        var ret = new AtkValue();
        var atkvalues = stackalloc AtkValue[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            atkvalues[i].Type = ValueType.Int;
            atkvalues[i].Int = values[i];
        }
        AgentMateriaAttach.Instance()->ReceiveEvent(&ret, atkvalues, (uint)values.Length, eventKind);
    }

    private AgentMateriaAttach.FilterCategory GetCategory(GameInventoryItem item)
    {
        unsafe
        {
            return (InventoryType)item.ContainerType switch
            {
                InventoryType.Inventory1 or InventoryType.Inventory2 or InventoryType.Inventory3 or InventoryType.Inventory4 => AgentMateriaAttach.FilterCategory.Inventory,
                InventoryType.ArmoryMainHand or InventoryType.ArmoryOffHand => AgentMateriaAttach.FilterCategory.ArmouryWeapon,
                InventoryType.ArmoryHead or InventoryType.ArmoryBody or InventoryType.ArmoryHands => AgentMateriaAttach.FilterCategory.ArmouryHeadBodyHands,
                InventoryType.ArmoryLegs or InventoryType.ArmoryFeets => AgentMateriaAttach.FilterCategory.ArmouryLegsFeet,
                InventoryType.ArmoryEar or InventoryType.ArmoryNeck => AgentMateriaAttach.FilterCategory.ArmouryNeckEars,
                InventoryType.ArmoryWrist or InventoryType.ArmoryRings => AgentMateriaAttach.FilterCategory.ArmouryWristRing,
                InventoryType.EquippedItems => AgentMateriaAttach.FilterCategory.Equipped,
                _ => AgentMateriaAttach.FilterCategory.None
            };
        }
    }

    private unsafe uint? GetUsableMateria()
    {
        var agent = AgentMateriaAttach.Instance();
        if (agent is null) throw new Exception($"Agent is null somehow");
        foreach (var materia in agent->Data->MateriaSorted)
            if (materia.Value->ItemLevel <= item.GameData.Value.LevelItem.RowId)
                return materia.Value->Item->ItemId;
        return null;
    }
}
