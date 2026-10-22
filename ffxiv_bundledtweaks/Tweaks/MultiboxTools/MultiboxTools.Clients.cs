using clib.Ui;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using Lumina.Excel.Sheets;

namespace ComplexTweaks.Tweaks;

public partial class MultiboxTools {
    private const int StatusIntervalMs = 1500;
    private const uint ClassJobIconOffset = 62100;

    private void DrawHubClients() {
        var clients = GetHubClients();
        if (_drawSnap.Cid == 0 && clients.Count == 0) {
            ImGui.TextColoredWrapped(Colors.Grey, "Waiting for clients...");
            return;
        }

        if (clients.Count == 0)
            ImGui.TextColoredWrapped(Colors.Grey, "Waiting for clients...");

        using var table = ImRaii.Table("##MultiboxPeers", 2, ImGuiTableFlags.SizingStretchProp);
        if (!table)
            return;

        ImGui.TableSetupColumn("Client", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, GetActionBlockWidth(RowButtonCount(hub: true)));

        if (_drawSnap.Cid != 0)
            DrawHubRow(_drawSnap.ToPeer(), self: true, _drawSnap.Cid, _drawSnap.WorldId, null);

        foreach (var client in clients)
            DrawHubRow(client.ToPeer(), self: false, client.Cid, client.WorldId, client);
    }

    private void DrawHubRow(PeerInfo peer, bool self, ulong cid, ushort worldId, HubClient? client) {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        DrawClientLabel(peer, self);

        ImGui.TableNextColumn();
        DrawRowActions(peer, self, cid, worldId, client);
    }

    private void DrawClientPeers() {
        var peers = GetPeers();
        if (_drawSnap.Cid == 0 && peers.Count == 0) {
            DrawHubWait();
            return;
        }

        if (peers.Count == 0)
            DrawHubWait();

        using var table = ImRaii.Table("##MultiboxPeers", 2, ImGuiTableFlags.SizingStretchProp);
        if (!table)
            return;

        ImGui.TableSetupColumn("Client", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, GetActionBlockWidth(RowButtonCount(hub: false)));

        if (_drawSnap.Cid != 0)
            DrawClientRow(_drawSnap.ToPeer(), self: true);

        foreach (var peer in peers)
            DrawClientRow(peer, self: false);
    }

    private void DrawHubWait() {
        if (_hubMissing && !HubConnected)
            ImGui.TextColoredWrapped(Color.Red, "No hub found.");
        else
            ImGui.TextColoredWrapped(Colors.Grey, "Waiting for hub...");
    }

    private void DrawClientRow(PeerInfo peer, bool self) {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        DrawClientLabel(peer, self);

        ImGui.TableNextColumn();
        DrawRowActions(peer, self);
    }

    private void DrawClientLabel(PeerInfo peer, bool self) {
        if (peer.JobId != 0) {
            ImGui.Icon(ClassJobIconOffset + peer.JobId, new Vector2(GetIconButtonSize().Y));
            ImGui.SameLine();
        }

        ImGui.AlignTextToFramePadding();
        var displayName = self ? $"{peer.DisplayName} (You)" : peer.DisplayName;
        if (peer.Dead)
            ImGui.TextColored(Color.Red, displayName);
        else
            ImGui.TextUnformatted(displayName);

        if (peer.Dead) {
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            ImGui.Icon(FontAwesomeIcon.Skull, Color.Red, "Dead");
        }

        if (peer.RezReady) {
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            ImGui.Icon(FontAwesomeIcon.Medkit, Color.Green, "Raise available");
        }

        if (peer.Duty) {
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            ImGui.Icon(FontAwesomeIcon.Flag, Color.Gold, "Duty commands active\nRight-click to clear");
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                ClearDutyFor(peer);
        }
    }

    private void SendHubZone(int? targetPid) {
        if (GetNearestHubAetheryteName() is not { Length: > 0 } aetheryteName) {
            ModuleMessage("Could not find an aetheryte near the hub.");
            return;
        }

        SendCommand(targetPid, $"/li {aetheryteName}");
    }

    private void SendHubWorld(int? targetPid) {
        if (GetHubWorldName() is not { Length: > 0 } worldName) {
            ModuleMessage("Could not resolve the hub world.");
            return;
        }

        SendCommand(targetPid, $"/li {worldName}");
    }

    private static string? GetNearestHubAetheryteName() {
        if (IObjectTable.Get().LocalPlayer is not { Position: var pos })
            return null;

        if (Coords.FindClosestAetheryte(IClientState.Get().TerritoryType, pos, includeAethernet: false) is not { } aetheryteId)
            return null;

        if (Aetheryte.GetRowOrNull(Coords.FindPrimaryAetheryte(aetheryteId)) is not { PlaceName.Value.Name: var name } || name.IsEmpty)
            return null;

        return name.ToString();
    }

    private static string? GetHubWorldName() {
        if (IPlayerState.Get().CurrentWorld is not { IsValid: true, Value.Name: var name })
            return null;

        var worldName = name.ToString();
        return worldName.IsEmpty ? null : worldName;
    }

    private void OnLogin() {
        _localSnapshot = ReadSnapshot();
        _forceStatus = true;
        _nextStatusTick = 0;
        if (Config.ActAsHub)
            PushPeers();
        else
            WriteHello();
    }

    private void OnFramework(IFramework _) {
        if (Environment.TickCount64 < _nextStatusTick)
            return;
        _nextStatusTick = Environment.TickCount64 + StatusIntervalMs;

        var snap = ReadSnapshot();
        var changed = _forceStatus || snap != LocalSnapshot();
        lock (_statusLock)
            _localSnapshot = snap;

        if (Config.ActAsHub) {
            if (!changed)
                return;
            _forceStatus = false;
            PushPeers();
            return;
        }

        if (!changed || !HubConnected)
            return;

        _forceStatus = false;
        WriteStatus(snap);
    }

    private ClientSnapshot LocalSnapshot() {
        lock (_statusLock)
            return _localSnapshot;
    }

    private ClientSnapshot ReadSnapshot() {
        RefreshIdentity();
        var (dead, rezReady) = ReadLife();
        var (vbmDefault, vbmAi) = GetPresetStates();
        bool duty;
        lock (_dutyLock)
            duty = _dutyOverride != null;
        return new(_pid, _localCid, _localName, _localWorldId, _localWorld, ReadJobId(), dead, rezReady, vbmDefault, vbmAi, duty);
    }

    private static byte ReadJobId() {
        var state = IPlayerState.Get();
        if (state is not { IsLoaded: true })
            return 0;

        var job = state.ClassJob;
        return job.IsValid ? (byte)job.RowId : (byte)0;
    }

    private static (bool Dead, bool RezReady) ReadLife() {
        if (IObjectTable.Get().LocalPlayer is not { } player)
            return (false, false);

        var dead = player.IsDead;
        return (dead, dead && player.ReviveState == ReviveState.Revivable);
    }

    private void WriteStatus(ClientSnapshot snap)
        => WriteHub(new StatusMessage(snap.Pid, snap.Cid, snap.Name, snap.WorldId, snap.JobId, snap.Dead, snap.RezReady, snap.VbmDefault, snap.VbmAi, snap.Duty));

    private void RefreshIdentity() {
        if (IPlayerState.Get() is { IsLoaded: true, ContentId: var cid, CharacterName: { IsEmpty: false } name, HomeWorld: { IsValid: true, RowId: var worldId, Value.Name: var world } }) {
            _localCid = cid;
            _localName = name;
            _localWorldId = (ushort)worldId;
            _localWorld = world.ToString();
        }
        else {
            _localCid = 0;
            _localName = "Not Logged In";
            _localWorldId = 0;
            _localWorld = string.Empty;
        }
    }

    private void WriteHello() => WriteHub(new HelloMessage(_pid, _localCid, _localName, _localWorldId));

    private void FormParty() {
        var clients = GetHubClients().Where(c => c.Cid != 0 && !IsInParty(c.Cid)).ToList();
        if (clients.Count == 0) {
            Log("Form Party: no inviteable clients");
            return;
        }

        _ = IFramework.Get().Run(() => {
            var invited = 0;
            foreach (var client in clients) {
                if (TryInviteClient(client))
                    invited++;
            }
            Log($"Form Party: invited {invited} client(s)");
        });
    }

    private unsafe bool TryInviteClient(HubClient client) {
        if (client.Cid == 0 || client.Name.IsEmpty || client.WorldId == 0) {
            Warning($"Cannot invite {client.DisplayName}: missing CID/name/world");
            return false;
        }

        if (IsInParty(client.Cid)) {
            Log($"Skipping invite for {client.DisplayName}: already in party");
            return false;
        }

        try {
            InfoProxyPartyInvite.Instance()->InviteToParty(client.Cid, client.Name, client.WorldId);
            Log($"Invited {client.DisplayName}");
            return true;
        }
        catch (Exception ex) {
            Error(ex, $"Failed to invite {client.DisplayName}");
            ModuleMessage($"Failed to invite {client.DisplayName}");
            return false;
        }
    }

    private static bool IsInParty(ulong cid)
        => cid != 0 && IPartyList.Get().Any(p => p.ContentId == cid);

    private List<HubClient> GetHubClients()
        => [.. SnapshotClients().Where(c => c.Pid != 0).OrderBy(c => c.Name)];

    private List<PeerInfo> GetPeers() {
        if (Config.ActAsHub) {
            lock (_lock)
                return [.. _clients.Where(c => c.Pid != 0).Select(c => c.ToPeer()).OrderBy(p => p.Name)];
        }

        lock (_lock)
            return [.. _clientPeers];
    }

    private void SendCommand(int? targetPid, string? command, bool remember = false, bool includeSelf = false) {
        var cmd = NormalizeCommand(command);
        if (cmd is null) {
            ModuleMessage("Enter a command starting with /");
            return;
        }

        if (targetPid == _pid) {
            if (remember)
                RememberCommand(cmd);
            ExecuteRemoteCommand(cmd);
            return;
        }

        if (!HasPeers()) {
            ModuleMessage("No other clients connected.");
            return;
        }

        if (remember)
            RememberCommand(cmd);

        if (targetPid is null)
            Log($"Broadcasting: {cmd}");
        else
            Log($"Sending to {targetPid}: {cmd}");

        if (Config.ActAsHub)
            HubRouteCommand(_pid, targetPid, cmd);
        else
            WriteHub(new CmdMessage(targetPid, cmd));

        if (includeSelf && targetPid is null)
            ExecuteRemoteCommand(cmd);
    }

    private void SendAction(int? targetPid, RemoteAction action) {
        if (targetPid == _pid) {
            ExecuteRemoteAction(action);
            return;
        }

        if (!HasPeers()) {
            ModuleMessage("No other clients connected.");
            return;
        }

        if (targetPid is null)
            Log($"Broadcasting action: {action}");
        else
            Log($"Sending {action} to {targetPid}");

        if (Config.ActAsHub)
            HubRouteAction(_pid, targetPid, action);
        else
            WriteHub(new ActionMessage(targetPid, action));
    }

    private void HubRouteCommand(int fromPid, int? targetPid, string cmd) {
        if (targetPid is null) {
            if (fromPid != _pid)
                ExecuteRemoteCommand(cmd);
            foreach (var client in SnapshotClients().Where(c => c.Pid != fromPid))
                client.Send(new ExecMessage(cmd));
            return;
        }

        if (targetPid == _pid) {
            ExecuteRemoteCommand(cmd);
            return;
        }

        SnapshotClients().FirstOrDefault(c => c.Pid == targetPid)?.Send(new ExecMessage(cmd));
    }

    private void HubRouteAction(int fromPid, int? targetPid, RemoteAction action) {
        if (targetPid is null) {
            if (fromPid != _pid)
                ExecuteRemoteAction(action);
            foreach (var client in SnapshotClients().Where(c => c.Pid != fromPid))
                client.Send(new RunActionMessage(action));
            return;
        }

        if (targetPid == _pid) {
            ExecuteRemoteAction(action);
            return;
        }

        SnapshotClients().FirstOrDefault(c => c.Pid == targetPid)?.Send(new RunActionMessage(action));
    }

    private void ExecuteRemoteCommand(string cmd) {
        if (!cmd.StartsWith('/') || cmd.StartsWith("/broadcast", StringComparison.OrdinalIgnoreCase))
            return;

        _ = IFramework.Get().Run(() => {
            try {
                IChatGui.Get().ExecuteCommand(cmd);
                Log($"Remote: {cmd}");
            }
            catch (Exception ex) {
                Error(ex, $"Failed to execute remote command: {cmd}");
                ModuleMessage($"Failed to execute remote command: {cmd}");
            }
        });
    }

    private void ExecuteRemoteAction(RemoteAction action) {
        _ = IFramework.Get().Run(() => {
            try {
                unsafe {
                    switch (action) {
                        case RemoteAction.Leave:
                            EventFramework.LeaveCurrentContent(true);
                            break;
                        case RemoteAction.Logout:
                            AgentLobby.Instance()->HandleLogout(false, 60);
                            break;
                        case RemoteAction.Exit:
                            AgentLobby.Instance()->HandleLogout(true, 60);
                            break;
                    }
                }
                Log($"Remote action: {action}");
            }
            catch (Exception ex) {
                Error(ex, $"Failed to execute remote action: {action}");
                ModuleMessage($"Failed to execute {action}");
            }
        });
    }

    private static string? NormalizeCommand(string? command) {
        if (string.IsNullOrWhiteSpace(command))
            return null;

        var cmd = command.Trim();
        return cmd.StartsWith('/') ? cmd : "/" + cmd;
    }
}
