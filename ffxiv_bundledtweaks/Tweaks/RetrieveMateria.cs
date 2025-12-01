using ComplexTweaks.Tasks;
using Dalamud.Game.Gui.ContextMenu;

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
}
