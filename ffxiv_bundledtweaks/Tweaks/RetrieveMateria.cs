using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Inventory;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using System.Threading.Tasks;

namespace ComplexTweaks.Tweaks;

[Tweak]
internal class RetrieveMateria : Tweak {
    public override string Name => "Retrieve All Materia";
    public override string Description => "Adds a context menu item that will retrieve all materia from an item.";

    public override void Enable() => Svc.ContextMenu.OnMenuOpened += OnOpenContextMenu;
    public override void Disable() => Svc.ContextMenu.OnMenuOpened -= OnOpenContextMenu;

    private void OnOpenContextMenu(IMenuOpenedArgs args) {
        if (args is not { MenuType: ContextMenuType.Inventory, Target: MenuTargetInventory { TargetItem: { ItemId: not 0, Materia: var materia } } inv } || materia.ToArray().All(m => m == 0)) return;
        args.AddMenuItem(new MenuItem {
            PrefixChar = 'C',
            Name = "Retrieve All Materia",
            OnClicked = (a) => Service.Automation.Start(new RetrieveAllMateria(inv.TargetItem.Value)),
            IsEnabled = !Player.IsBusy,
        });
    }

    public sealed class RetrieveAllMateria(GameInventoryItem item) : TaskBase {
        protected override async Task Execute() {
            Status = $"Retrieving Materia";
            var materias = item.Materia.ToArray().Where(x => x != 0);
            foreach (var materia in materias) {
                unsafe { EventFramework.Instance()->MaterializeItem((InventoryItem*)item.Address, MaterializeEntryId.Retrieve); }
                await WaitUntilThenFalse(() => Svc.Condition[ConditionFlag.Occupied39], "RetrievingMateria");
            }
        }
    }
}
