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

    private readonly record struct CmdCtx(MultiboxTools Host, int? TargetPid, PeerInfo Peer, bool HasPeer, bool Self, ulong Cid, ushort WorldId, HubClient? Client, bool HasPeers, bool Toolbar);
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

            var id = ctx.Toolbar ? $"{cmd.Id}All" : $"{cmd.Id}{ctx.Peer.Pid}";
            cmd.Draw(ctx, id);
        }

        return drawn;
    }

    private void DrawRowActions(PeerInfo peer, bool self, ulong cid = 0, ushort worldId = 0, HubClient? client = null) {
        var ctx = new CmdCtx(this, peer.Pid, peer, true, self, cid, worldId, client, HasPeers(), Toolbar: false);
        if (DrawCommands(ctx, beforeSend: true))
            ImGui.SameLine();

        if (ImGui.IconButton(FontAwesomeIcon.PaperPlane, $"send{peer.Pid}", "Send command"))
            SendCommand(peer.Pid, _commandInput, remember: true);

        ImGui.SameLine();
        DrawCommands(ctx, beforeSend: false);
    }

    private CmdCtx ToolbarContext(bool hasPeers)
        => new(this, null, default, false, false, 0, 0, null, hasPeers, Toolbar: true);

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
            var on = ctx.Toolbar ? ctx.Host.EveryoneOn(preset) : ctx.HasPeer && ctx.Peer.PresetOn(preset);
            if (ImGui.MenuItem(label, on))
                ctx.Host.SendVbm(ctx.TargetPid, preset, !on);
        }
    }

    private sealed class InviteCommand : IMultiboxCommand {
        public int Order => 20;
        public bool HubOnly => true;
        public bool BeforeSend => false;
        public FontAwesomeIcon Icon => FontAwesomeIcon.UserPlus;
        public string Id => "invite";

        public string Tooltip(CmdCtx ctx) {
            if (ctx.Toolbar)
                return "Invite all to party";
            if (ctx.Self)
                return "This client";
            if (IsInParty(ctx.Cid))
                return "Already in party";
            return "Invite to party";
        }

        public bool Disabled(CmdCtx ctx)
            => ctx.Toolbar ? !ctx.HasPeers : ctx.Self || IsInParty(ctx.Cid) || ctx.Cid == 0 || ctx.WorldId == 0;

        public void Execute(CmdCtx ctx) {
            if (ctx.Toolbar)
                ctx.Host.FormParty();
            else if (ctx.Client != null)
                _ = IFramework.Get().Run(() => ctx.Host.TryInviteClient(ctx.Client));
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
                ctx.Host.SendHubZone(ctx.TargetPid);
                ImGui.CloseCurrentPopup();
            }

            if (ImGui.MenuItem("World")) {
                ctx.Host.SendHubWorld(ctx.TargetPid);
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
        public void Execute(CmdCtx ctx) => ctx.Host.SendAction(ctx.TargetPid, RemoteAction.Leave);
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
                ctx.Host.SendAction(ctx.TargetPid, RemoteAction.Logout);
                ImGui.CloseCurrentPopup();
            }

            if (ImGui.MenuItem("Exit")) {
                ctx.Host.SendAction(ctx.TargetPid, RemoteAction.Exit);
                ImGui.CloseCurrentPopup();
            }
        }
    }
}
