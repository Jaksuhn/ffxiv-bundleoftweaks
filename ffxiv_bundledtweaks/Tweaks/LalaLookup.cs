using Dalamud.Game.Gui.ContextMenu;
using Lumina.Excel.Sheets;
using NetStone;
using NetStone.Search.Character;
using System.Threading.Tasks;

namespace ComplexTweaks.Tweaks;

public class AchvLookupConfig {
    [BoolConfig] public bool EnableLala = true;
    [BoolConfig] public bool EnableFfxivCollect = true;
}

public class LalaLookup : Tweak<AchvLookupConfig> {
    public override string Name => "Achievement Profile Lookup";
    public override string Description => "Adds a context menu entry to lookup a character on lalachievements or ffxivcollect";

    private LodestoneClient _client = null!;

    public override void OnEnable() => IContextMenu.Get().OnMenuOpened += OnOpenContextMenu;
    public override void OnDisable() => IContextMenu.Get().OnMenuOpened -= OnOpenContextMenu;

    private enum LinkTarget {
        Lala,
        FfxivCollect
    }

    private void OnOpenContextMenu(IMenuOpenedArgs menuOpenedArgs) {
        if (!IsMenuValid(menuOpenedArgs)) return;

        if (Config.EnableLala) {
            menuOpenedArgs.AddMenuItem(new MenuItem {
                PrefixChar = 'C',
                Name = "Search on Lala",
                OnClicked = (a) => _ = Search(a, LinkTarget.Lala),
            });
        }
        if (Config.EnableFfxivCollect) {
            menuOpenedArgs.AddMenuItem(new MenuItem {
                PrefixChar = 'C',
                Name = "Search on FFXIVCollect",
                OnClicked = (a) => _ = Search(a, LinkTarget.FfxivCollect),
            });
        }
    }

    private static bool IsMenuValid(IMenuOpenedArgs menuOpenedArgs) {
        if (menuOpenedArgs.Target is not MenuTargetDefault menuTargetDefault) return false;

        switch (menuOpenedArgs.AddonName) {
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
                return menuTargetDefault.TargetName != string.Empty && menuTargetDefault is { TargetContentId: not 0 } && (World.FirstOrNull(x => x.RowId == menuTargetDefault.TargetHomeWorld.RowId)?.IsPublic ?? false);
            default:
                break;
        }

        return false;
    }

    private async Task Search(IMenuItemClickedArgs menuItemClickedArgs, LinkTarget target) {
        var id = await SearchPlayerFromMenu(menuItemClickedArgs);
        if (id is null) return;

        switch (target) {
            case LinkTarget.Lala:
                Dalamud.Utility.Util.OpenLink($"https://lalachievements.com/char/{id}/");
                break;
            case LinkTarget.FfxivCollect:
                Dalamud.Utility.Util.OpenLink($"https://ffxivcollect.com/characters/{id}/");
                break;
        }
    }

    private async Task<string?> SearchPlayerFromMenu(IMenuItemClickedArgs menuArgs) {
        if (menuArgs.Target is not MenuTargetDefault menuTargetDefault) return null;

        var playerName = menuTargetDefault.TargetName;
        var world = World.FirstOrNull(x => x.RowId == menuTargetDefault.TargetHomeWorld.RowId);
        if (world is not { IsPublic: true }) {
            ModuleMessage($"Unable to find world for {playerName}");
            return null;
        }

        try {
            _client = await LodestoneClient.GetClientAsync();
            var searchResponse = await _client.SearchCharacter(new CharacterSearchQuery {
                CharacterName = playerName,
                World = world.Value.Name.ToString(),
            });

            var lodestoneCharacter = searchResponse?.Results.FirstOrDefault(entry => string.Equals(entry.Name, playerName, StringComparison.OrdinalIgnoreCase));
            if (lodestoneCharacter is { Id: { } id })
                return id;
            else {
                ModuleMessage($"Unable to find lodestone ID for {playerName}");
                return null;
            }
        }
        catch (Exception e) { Error(e, "Error looking up character on lodestone"); return null; }
    }
}
