using ComplexTweaks.Tasks;
using Dalamud.Game.Gui.ContextMenu;

namespace ComplexTweaks.Tweaks;

[Tweak]
public class GettingTooAttached : Tweak {
    public override string Name => "Getting Too Attached";
    public override string Description => "Adds a context menu to items to loop through attaching and removing materia for the Getting Too Attached achievement";

    public override void Enable() => Svc.ContextMenu.OnMenuOpened += OnMenuOpened;
    public override void Disable() => Svc.ContextMenu.OnMenuOpened -= OnMenuOpened;

    private void OnMenuOpened(IMenuOpenedArgs args) {
        if (args is not { MenuType: ContextMenuType.Inventory }) return;
        if (args.Target is not MenuTargetInventory { TargetItem: { } item }) return;
        if (item.MateriaEntries.Count >= item.GameData.Value.MateriaSlotCount) return; // only want items that have a guaranteed meld slot

        args.AddMenuItem(new MenuItem {
            PrefixChar = 'C',
            Name = Name,
            OnClicked = (_) => Svc.Automation.Start(new LoopMelding(item)),
        });
    }
}
