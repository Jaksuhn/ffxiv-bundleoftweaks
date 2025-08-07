using ECommons.Automation;
using ECommons.ImGuiMethods;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Dalamud.Bindings.ImGui;
using Lumina.Excel.Sheets;

namespace Automaton.UI.Debug.Tabs;
internal unsafe class ToolsTab : DebugTab
{
    public override void Draw()
    {
        List<string> cantSpend = [];
        if (ImGui.Button("Spend Nuts"))
        {
            if (TryGetAddonByName<AtkUnitBase>("ShopExchangeCurrency", out var addon))
            {
                const uint nuts = 26533;
                var nutsAmt = InventoryManager.Instance()->GetInventoryItemCount(nuts);
                var nutsCost = 25;
                var freeslots = InventoryManager.Instance()->GetEmptySlotsInBag() + Inventory.GetEmptySlots([InventoryType.ArmoryRings]);
                var tobuy = (uint)Math.Min(nutsAmt / nutsCost, freeslots);
                Svc.Log.Info($"{InventoryManager.Instance()->GetEmptySlotsInBag()} {Inventory.GetEmptySlots([InventoryType.ArmoryRings])} {nutsAmt} {nutsAmt / nutsCost} {tobuy}");
                Callback.Fire(addon, true, 0, 49, tobuy);
            }
            else
                cantSpend.Add("ShopExchangeCurrency not open");
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip($"Buys the most amount of {GetRow<Item>(34922)?.Name}");
        cantSpend.ForEach(x => ImGuiEx.Text(EzColor.RedBright, x));

        if (ImGui.Button("Use all items"))
        {
            foreach (var c in Inventory.PlayerInventory)
            {
                var cont = InventoryManager.Instance()->GetInventoryContainer(c);
                for (var i = 0; i < cont->Size; ++i)
                {
                    var slot = cont->GetInventorySlot(i);
                    var item = GetRow<Item>(slot->ItemId)!;
                    if (item.Value.ItemSortCategory.Value.Param is 175 or 160)
                    {
                        Service.TaskManager.Enqueue(() => AgentInventoryContext.Instance()->UseItem(slot->ItemId));
                        Service.TaskManager.Enqueue(() => !Player.IsBusy);
                    }
                    //ActionManager.Instance()->UseAction(ActionType.Item, slot->ItemId);
                }
            }
        }

        if (ImGui.Button("hg"))
        {
            var player = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)GameObjectManager.Instance()->Objects.IndexSorted[0].Value;
            player->GetStatusManager()->SetStatus(20, 149, 5.0f, 0, (ulong)0xE0000000, true);
        }
    }
}
