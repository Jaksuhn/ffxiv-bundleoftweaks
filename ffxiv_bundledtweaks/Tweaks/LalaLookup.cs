using Dalamud.Game.Gui.ContextMenu;
using Lumina.Excel.Sheets;
using NetStone;
using NetStone.Search.Character;
using System.Threading.Tasks;

namespace ComplexTweaks.Tweaks;

[Tweak]
public class LalaLookup : Tweak
{
    public override string Name => "Lalachievements Lookup";
    public override string Description => "Adds a context menu entry to lookup a character on lalachievements";

    private LodestoneClient _client = null!;

    public override void Enable()
    {
        Svc.ContextMenu.OnMenuOpened += OnOpenContextMenu;
    }

    public override void Disable()
    {
        Svc.ContextMenu.OnMenuOpened -= OnOpenContextMenu;
    }

    private void OnOpenContextMenu(IMenuOpenedArgs menuOpenedArgs)
    {
        if (!IsMenuValid(menuOpenedArgs)) return;

        menuOpenedArgs.AddMenuItem(new MenuItem
        {
            PrefixChar = 'C',
            Name = "Search on Lala",
            OnClicked = Search,
        });
    }

    private static bool IsMenuValid(IMenuOpenedArgs menuOpenedArgs)
    {
        if (menuOpenedArgs.Target is not MenuTargetDefault menuTargetDefault) return false;

        switch (menuOpenedArgs.AddonName)
        {
            case null: // Nameplate/Model menu
            case "LookingForGroup":
            case "PartyMemberList":
            case "FriendList":
            case "FreeCompany":
            case "SocialList":
            case "ContactList":
            case "ChatLog":
            case "_PartyList":
            case "LinkShell":
            case "CrossWorldLinkshell":
            case "ContentMemberList": // Eureka/Bozja/...
            case "BeginnerChatList":
                return menuTargetDefault.TargetName != string.Empty && menuTargetDefault is { TargetContentId: not 0 }
                       && (GetSheet<World>()?.FirstOrDefault(x => x.RowId == menuTargetDefault.TargetHomeWorld.RowId).IsPublic ?? false);
            default:
                break;
        }

        return false;
    }

    private void Search(IMenuItemClickedArgs menuItemClickedArgs)
    {
        //if (!IsMenuValid(menuItemClickedArgs)) return;
        _ = SearchPlayerFromMenu(menuItemClickedArgs);
    }

    private async Task SearchPlayerFromMenu(IMenuItemClickedArgs menuArgs)
    {
        if (menuArgs.Target is not MenuTargetDefault menuTargetDefault) return;

        var playerName = menuTargetDefault.TargetName;
        var world = GetSheet<World>()?.FirstOrDefault(x => x.RowId == menuTargetDefault.TargetHomeWorld.RowId);
        if (world is not { IsPublic: true })
        {
            ModuleMessage($"Unable to find world for {playerName}");
            return;
        }

        try
        {
            _client = await LodestoneClient.GetClientAsync();
            var searchResponse = await _client.SearchCharacter(new CharacterSearchQuery
            {
                CharacterName = playerName,
                World = world.Value.Name.ToString(),
            });

            var lodestoneCharacter = searchResponse?.Results.FirstOrDefault(entry => string.Equals(entry.Name, playerName, StringComparison.OrdinalIgnoreCase));
            if (lodestoneCharacter is not null)
                Dalamud.Utility.Util.OpenLink($"https://lalachievements.com/char/{lodestoneCharacter.Id}/");
            else
                ModuleMessage($"Unable to find lodestone ID for {playerName}");
        }
        catch (Exception e) { e.Log(); }
    }
}
