using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using System.Reflection;

namespace ComplexTweaks.Tweaks;

public partial class MultiboxTools {
    private interface IMultiboxCommand {
        int Order { get; }
        bool HubOnly { get; }
        bool BeforeSend { get; }
        FontAwesomeIcon Icon { get; }
        string Id { get; }
        string Tooltip(CmdCtx ctx);
        bool Disabled(CmdCtx ctx) => false;
        bool Active(CmdCtx ctx) => false;
        void Execute(CmdCtx ctx);

        void Draw(CmdCtx ctx, string id) {
            if (ImGui.IconButton(Icon, id, Tooltip(ctx), disabled: Disabled(ctx), active: Active(ctx)))
                Execute(ctx);
        }
    }

    private readonly record struct CmdCtx(MultiboxTools Host, ClientSnapshot? Peer, bool Self, bool HasPeers) {
        public bool Toolbar => Peer is null;
        public int? TargetId => Peer?.SessionId;
    }
    private IMultiboxCommand[] _commands = [];

    private void DiscoverCommands() {
        _commands = [.. GetType()
            .GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
            .Where(t => t is { IsAbstract: false, IsInterface: false } && typeof(IMultiboxCommand).IsAssignableFrom(t))
            .Select(t => (IMultiboxCommand)Activator.CreateInstance(t, nonPublic: true)!)
            .OrderBy(c => c.Order)];
    }

    private void EnsureCommands() {
        if (_commands.Length == 0)
            DiscoverCommands();
    }

    private int RowButtonCount(bool hub) {
        EnsureCommands();
        return _commands.Count(c => hub || !c.HubOnly) + 1;
    }

    private bool DrawCommands(CmdCtx ctx, bool? beforeSend) {
        EnsureCommands();
        var drawn = false;
        foreach (var cmd in _commands) {
            if (cmd.HubOnly && !Config.ActAsHub)
                continue;
            if (beforeSend is { } leading && cmd.BeforeSend != leading)
                continue;

            if (drawn)
                ImGui.SameLine();
            drawn = true;

            var id = ctx.Peer is { } peer ? $"{cmd.Id}{peer.SessionId}" : $"{cmd.Id}All";
            cmd.Draw(ctx, id);
        }

        return drawn;
    }

    private void DrawRowActions(ClientSnapshot peer, bool self, bool hasPeers) {
        var ctx = new CmdCtx(this, peer, self, hasPeers);
        if (DrawCommands(ctx, beforeSend: true))
            ImGui.SameLine();

        if (ImGui.IconButton(FontAwesomeIcon.PaperPlane, $"send{peer.SessionId}", "Send command"))
            SendCommand(peer.SessionId, _commandInput, remember: true);

        ImGui.SameLine();
        DrawCommands(ctx, beforeSend: false);
    }

    private sealed class VbmCommand : IMultiboxCommand {
        public int Order => 0;
        public bool HubOnly => false;
        public bool BeforeSend => true;
        public FontAwesomeIcon Icon => FontAwesomeIcon.Crosshairs;
        public string Id => "vbm";
        public string Tooltip(CmdCtx ctx) => ctx.Toolbar ? "VBM presets for all" : "VBM presets";
        public void Execute(CmdCtx ctx) { }

        public void Draw(CmdCtx ctx, string id) {
            if (ImGui.IconButton(Icon, id, Tooltip(ctx)))
                ImGui.OpenPopup(id);

            using var popup = ImRaii.Popup(id);
            if (!popup)
                return;

            DrawPreset(ctx, VbmDefaultPreset, "VBM Default");
            DrawPreset(ctx, VbmAiPreset, "VBM AI");
        }

        private static void DrawPreset(CmdCtx ctx, string preset, string label) {
            var on = ctx.Peer is { } peer ? peer.PresetOn(preset) : ctx.Host.EveryoneOn(preset);
            if (ImGui.MenuItem(label, on))
                ctx.Host.SendVbm(ctx.TargetId, preset, !on);
        }
    }

    private sealed class LootCommand : IMultiboxCommand {
        private static readonly LootMode[] Modes = [LootMode.Off, LootMode.Need, LootMode.Greed, LootMode.Pass];

        public int Order => 5;
        public bool HubOnly => false;
        public bool BeforeSend => true;
        public FontAwesomeIcon Icon => FontAwesomeIcon.Dice;
        public string Id => "loot";
        public string Tooltip(CmdCtx ctx) => ctx.Toolbar ? "LazyLoot mode for all" : Disabled(ctx) ? "LazyLoot not loaded" : "LazyLoot mode";
        public bool Disabled(CmdCtx ctx) => ctx.Peer is { Loot: LootMode.Unknown };
        public void Execute(CmdCtx ctx) { }

        public void Draw(CmdCtx ctx, string id) {
            if (ImGui.IconButton(Icon, id, Tooltip(ctx), disabled: Disabled(ctx)))
                ImGui.OpenPopup(id);

            using var popup = ImRaii.Popup(id);
            if (!popup)
                return;

            foreach (var mode in Modes) {
                var on = ctx.Peer is { } peer ? peer.Loot == mode : ctx.Host.EveryoneLoot(mode);
                if (ImGui.MenuItem(mode.ToString(), on))
                    ctx.Host.SendLoot(ctx.TargetId, mode);
            }
        }
    }

    private sealed class InviteCommand : IMultiboxCommand {
        public int Order => 20;
        public bool HubOnly => true;
        public bool BeforeSend => false;
        public FontAwesomeIcon Icon => FontAwesomeIcon.UserPlus;
        public string Id => "invite";

        public string Tooltip(CmdCtx ctx) {
            if (ctx.Peer is not { } peer)
                return "Invite all to party";
            if (ctx.Self)
                return "This client";
            if (IsInParty(peer.Cid))
                return "Already in party";
            return "Invite to party";
        }

        public bool Disabled(CmdCtx ctx)
            => ctx.Peer is not { } peer ? !ctx.HasPeers : ctx.Self || peer.Cid == 0 || peer.WorldId == 0 || IsInParty(peer.Cid);

        public void Execute(CmdCtx ctx) {
            if (ctx.Peer is not { } peer)
                ctx.Host.FormParty();
            else
                _ = IFramework.Get().Run(() => ctx.Host.TryInviteClient(peer));
        }
    }

    private sealed class NavCommand : IMultiboxCommand {
        public int Order => 25;
        public bool HubOnly => true;
        public bool BeforeSend => false;
        public FontAwesomeIcon Icon => FontAwesomeIcon.Running;
        public string Id => "nav";
        public string Tooltip(CmdCtx ctx) => ctx.Toolbar ? "All navmesh to controller" : "Navmesh to controller";

        public bool Disabled(CmdCtx ctx)
            => ctx.Peer is not { } peer ? !ctx.HasPeers : ctx.Self || peer.TerritoryId != IClientState.Get().TerritoryType;

        public void Execute(CmdCtx ctx) { }

        public void Draw(CmdCtx ctx, string id) {
            if (ImGui.IconButton(Icon, id, Tooltip(ctx), disabled: Disabled(ctx)))
                ImGui.OpenPopup(id);

            using var popup = ImRaii.Popup(id);
            if (!popup)
                return;

            if (ImGui.MenuItem("Walk")) {
                ctx.Host.SendNav(ctx.TargetId, fly: false);
                ImGui.CloseCurrentPopup();
            }

            if (ImGui.MenuItem("Fly")) {
                ctx.Host.SendNav(ctx.TargetId, fly: true);
                ImGui.CloseCurrentPopup();
            }
        }
    }

    private sealed class TravelCommand : IMultiboxCommand {
        public int Order => 30;
        public bool HubOnly => true;
        public bool BeforeSend => false;
        public FontAwesomeIcon Icon => FontAwesomeIcon.Globe;
        public string Id => "travel";
        public string Tooltip(CmdCtx ctx) => ctx.Toolbar ? "Travel all to controller" : "Travel to controller";
        public bool Disabled(CmdCtx ctx) => ctx.Toolbar ? !ctx.HasPeers : ctx.Self;
        public void Execute(CmdCtx ctx) { }

        public void Draw(CmdCtx ctx, string id) {
            if (ImGui.IconButton(Icon, id, Tooltip(ctx), disabled: Disabled(ctx)))
                ImGui.OpenPopup(id);

            using var popup = ImRaii.Popup(id);
            if (!popup)
                return;

            if (ImGui.MenuItem("Zone")) {
                ctx.Host.SendHubZone(ctx.TargetId);
                ImGui.CloseCurrentPopup();
            }

            if (ImGui.MenuItem("World")) {
                ctx.Host.SendHubWorld(ctx.TargetId);
                ImGui.CloseCurrentPopup();
            }
        }
    }

    private sealed class LeaveCommand : IMultiboxCommand {
        public int Order => 50;
        public bool HubOnly => false;
        public bool BeforeSend => false;
        public FontAwesomeIcon Icon => FontAwesomeIcon.DoorOpen;
        public string Id => "leave";
        public string Tooltip(CmdCtx ctx) => ctx.Toolbar ? "All leave duty" : "Leave duty";
        public bool Disabled(CmdCtx ctx) => ctx.Toolbar && !ctx.HasPeers;
        public void Execute(CmdCtx ctx) => ctx.Host.SendAction(ctx.TargetId, RemoteAction.Leave);
    }

    private sealed class PowerCommand : IMultiboxCommand {
        public int Order => 60;
        public bool HubOnly => false;
        public bool BeforeSend => false;
        public FontAwesomeIcon Icon => FontAwesomeIcon.PowerOff;
        public string Id => "power";
        public string Tooltip(CmdCtx ctx) => ctx.Toolbar ? "Logout or exit all" : "Logout or exit";
        public bool Disabled(CmdCtx ctx) => ctx.Toolbar && !ctx.HasPeers;
        public void Execute(CmdCtx ctx) { }

        public void Draw(CmdCtx ctx, string id) {
            if (ImGui.IconButton(Icon, id, Tooltip(ctx), disabled: Disabled(ctx)))
                ImGui.OpenPopup(id);

            using var popup = ImRaii.Popup(id);
            if (!popup)
                return;

            if (ImGui.MenuItem("Logout")) {
                ctx.Host.SendAction(ctx.TargetId, RemoteAction.Logout);
                ImGui.CloseCurrentPopup();
            }

            if (ImGui.MenuItem("Exit")) {
                ctx.Host.SendAction(ctx.TargetId, RemoteAction.Exit);
                ImGui.CloseCurrentPopup();
            }
        }
    }
}
