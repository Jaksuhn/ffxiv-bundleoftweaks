using AutoRetainerAPI.Configuration;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Game.Text;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Plugin.Ipc.Exceptions;
using System.Globalization;

namespace ComplexTweaks.Features;

[Tweak]
public class ARSwitcher : Tweak
{
    public override string Name => "AutoRetainer Character Switcher";
    public override string Description => "Adds a DTR element and commands to switch to the prev/next character in AutoRetainer.";

    private IDtrBarEntry _dtrBarEntry = null!;

    public override void Enable()
    {
        _dtrBarEntry = Svc.DtrBar.Get("Character Index", "Unknown Character Index");
        _dtrBarEntry.OnClick = (DtrInteractionEvent @event) =>
        {
            unsafe
            {
                var homeWorldId = Svc.ClientState.LocalPlayer?.HomeWorld.RowId;
                var currentWorldId = Svc.ClientState.LocalPlayer?.CurrentWorld.RowId;
                if (homeWorldId == currentWorldId)
                {
                    var target = FindCharacter(@event.ClickType is MouseClickType.Left ? 1 : -1);
                    SwitchCharacter(target);
                }
                else
                    Svc.Commands.ProcessCommand("/li");
            }
        };
        UpdateDtrBar();
        Svc.ClientState.Login += UpdateDtrBar;
    }

    public override void Disable()
    {
        _dtrBarEntry.Remove();
        Svc.ClientState.Login -= UpdateDtrBar;
    }

    private void UpdateDtrBar()
    {
        if (_dtrBarEntry.UserHidden)
            return;

        try
        {
            var currentWorld = Svc.ClientState.LocalPlayer?.CurrentWorld.Value.Name.ToString();
            var homeWorld = Svc.ClientState.LocalPlayer?.HomeWorld.Value.Name.ToString();
            var characterIds = Service.AutoRetainerApi.GetRegisteredCharacters() ?? [];
            var characterIdsOnHomeWorld = characterIds
                .Where(x => Service.AutoRetainerApi.GetOfflineCharacterData(x)?.World == homeWorld).ToList();

            SeIconChar seIconChar = SeIconChar.Instance1 + characterIdsOnHomeWorld.IndexOf(Svc.ClientState.LocalContentId);
            if (currentWorld == homeWorld)
            {
                _dtrBarEntry.Text = seIconChar.ToIconString();

                var previous = FindCharacter(-1, showError: false);
                var next = FindCharacter(1, showError: false);
                if (previous != null && next != null)
                    _dtrBarEntry.Tooltip = $"Prev: {previous.ToString(homeWorld)}\nNext: {next.ToString(homeWorld)}";
                else if (previous != null)
                    _dtrBarEntry.Tooltip = $"Prev: {previous.ToString(homeWorld)}";
                else if (next != null)
                    _dtrBarEntry.Tooltip = $"Next: {next.ToString(homeWorld)}";
                else
                    _dtrBarEntry.Tooltip = null;
            }
            else
            {
                _dtrBarEntry.Text = $"{homeWorld} {seIconChar.ToIconString()}";
                _dtrBarEntry.Tooltip = $"Return to {homeWorld}";
            }

            if (!_dtrBarEntry.Shown)
                _dtrBarEntry.Shown = true;
        }
        catch (IpcError)
        {
            _dtrBarEntry.Shown = false;
        }
    }

    private Target? FindCharacter(int direction, bool showError = true)
    {
        try
        {
            Verbose($"Switching characters ({direction})");

            var characterIds = Service.AutoRetainerApi.GetRegisteredCharacters();
            var index = characterIds.IndexOf(Svc.ClientState.LocalContentId);
            if (index < 0)
            {
                if (showError)
                    ModuleMessage("Current character not known.");
                return null;
            }

            OfflineCharacterData? target;
            do
            {
                index = (index + direction + characterIds.Count) % characterIds.Count;
                target = Service.AutoRetainerApi.GetOfflineCharacterData(characterIds[index]);
                if (target?.CID == Svc.ClientState.LocalContentId)
                {
                    if (showError)
                        ModuleMessage("No character to switch to found.");
                    return null;
                }

                if (target is { ExcludeRetainer: true, ExcludeWorkshop: true })
                    target = null;
            } while (target == null);

            return new Target(target.Name, target.World);
        }
        catch (IpcError)
        {
            ModuleMessage("Could not switch character, AutoRetainer API isn't available.");
            return null;
        }
    }

    [CommandHandler("/k+", "Switch to the next AR-enabled character.")]
    internal void NextCharacter(string command, string arguments) => SwitchCharacter(FindCharacter(1));

    [CommandHandler("/k-", "Switch to the previous AR-enabled character.")]
    internal void PreviousCharacter(string command, string arguments) => SwitchCharacter(FindCharacter(-1));

    [CommandHandler("/ks", $"Switch to a specific character,\n\t/ks [partial character name] - switch to the first character with a matching name.\n\t/ks [world name] [index] - switch to the Nth character on the specified world.")]
    internal void PickCharacter(string command, string arguments)
    {
        if (string.IsNullOrEmpty(arguments))
        {
            ModuleMessage("Usage: /ks <world/name> [index]");
            return;
        }

        try
        {
            var args = arguments.Split(' ', 2);
            if (args.Length < 2 || !int.TryParse(args[1], CultureInfo.InvariantCulture, out var index))
                index = 1;

            var targets = Service.AutoRetainerApi.GetRegisteredCharacters()
                .Select(characterId => Service.AutoRetainerApi.GetOfflineCharacterData(characterId))
                .Where(x => !x.ExcludeRetainer || !x.ExcludeWorkshop)
                .Select(x => new { x.Name, x.World })
                .ToList();

            var target = targets.Where(x => x.World.StartsWith(args[0], StringComparison.OrdinalIgnoreCase)).Skip(index - 1).FirstOrDefault() ?? targets.FirstOrDefault(x => x.Name.Contains(arguments, StringComparison.OrdinalIgnoreCase));
            if (target == null)
            {
                ModuleMessage($"No character found on world {args[0]} with #{index}.");
                return;
            }

            SwitchCharacter(new Target(target.Name, target.World));
        }
        catch (IpcError)
        {
            ModuleMessage("Could not switch character, AutoRetainer API isn't available.");
        }
    }

    private void SwitchCharacter(Target? target)
    {
        if (target == null)
            return;

        if (Svc.Condition[ConditionFlag.BoundByDuty] || Svc.Condition[ConditionFlag.BoundByDuty56] ||
            Svc.Condition[ConditionFlag.BoundByDuty95] || Svc.Condition[ConditionFlag.InDutyQueue] ||
            Svc.Condition[ConditionFlag.Occupied] || Svc.Condition[ConditionFlag.Occupied30] ||
            Svc.Condition[ConditionFlag.Occupied33] || Svc.Condition[ConditionFlag.Occupied38] ||
            Svc.Condition[ConditionFlag.Occupied39] || Svc.Condition[ConditionFlag.OccupiedInEvent] ||
            Svc.Condition[ConditionFlag.OccupiedSummoningBell] || Svc.Condition[ConditionFlag.OccupiedInQuestEvent] ||
            Svc.Condition[ConditionFlag.OccupiedInCutSceneEvent] || Svc.Condition[ConditionFlag.WatchingCutscene] ||
            Svc.Condition[ConditionFlag.WatchingCutscene78] || Svc.Condition[ConditionFlag.InCombat])
        {
            Svc.NotificationManager.AddNotification(new Notification
            {
                Title = $"{Plugin.Name} - {Name}",
                Content = "Can't switch characters (bound by duty or occupied)",
                Type = NotificationType.Error
            });
            return;
        }

        Svc.NotificationManager.AddNotification(new Notification
        {
            Title = $"{Plugin.Name} - {Name}",
            Content = $"Switch to {target}.",
            Type = NotificationType.Success,
        });
        Svc.Commands.ProcessCommand($"/ays relog {target}");
    }

    private sealed record Target(string Name, string World)
    {
        public override string ToString() => $"{Name}@{World}";
        public string ToString(string? currentWorld) => currentWorld != World ? ToString() : Name;
    }
}
