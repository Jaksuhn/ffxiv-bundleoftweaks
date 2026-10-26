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

    private void DrawPeers(bool hasPeers) {
        var showSelf = _drawSnap.Cid != 0;
        if (!hasPeers) {
            if (Config.ActAsHub)
                ImGui.TextColoredWrapped(Colors.Grey, "Waiting for clients...");
            else
                DrawHubWait();
            if (!showSelf)
                return;
        }

        using var table = ImRaii.Table("##MultiboxPeers", 2, ImGuiTableFlags.SizingStretchProp);
        if (!table)
            return;

        ImGui.TableSetupColumn("Client", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, GetActionBlockWidth(RowButtonCount(Config.ActAsHub)));

        if (showSelf)
            DrawPeerRow(_drawSnap, self: true, hasPeers);

        foreach (var peer in _drawPeers)
            DrawPeerRow(peer, self: false, hasPeers);
    }

    private void DrawHubWait() {
        if (_hubMissing && !HubConnected)
            ImGui.TextColoredWrapped(Color.Red, "No hub found.");
        else
            ImGui.TextColoredWrapped(Colors.Grey, "Waiting for hub...");
    }

    private void DrawPeerRow(ClientSnapshot peer, bool self, bool hasPeers) {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        DrawClientLabel(peer, self);

        ImGui.TableNextColumn();
        DrawRowActions(peer, self, hasPeers);
    }

    private void DrawClientLabel(ClientSnapshot peer, bool self) {
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

        if (peer.Version != MBTVersion) {
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            ImGui.Icon(FontAwesomeIcon.ExclamationTriangle, Color.Yellow, $"Running a different {Svc.Interface.Manifest.Name} version (protocol v{peer.Version}, this is v{MBTVersion}).\nUpdate all clients so commands work reliably.");
        }

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

    private void SendHubZone(int? targetId) {
        if (GetNearestHubAetheryteName() is not { Length: > 0 } aetheryteName) {
            ModuleMessage("Could not find an aetheryte near the hub.");
            return;
        }

        SendCommand(targetId, $"/li {aetheryteName}");
    }

    private void SendHubWorld(int? targetId) {
        if (GetHubWorldName() is not { Length: > 0 } worldName) {
            ModuleMessage("Could not resolve the hub world.");
            return;
        }

        SendCommand(targetId, $"/li {worldName}");
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
        _forceStatus = true;
        _nextStatusTick = 0;
    }

    private void OnFramework(IFramework _) {
        if (Environment.TickCount64 < _nextStatusTick)
            return;
        _nextStatusTick = Environment.TickCount64 + StatusIntervalMs;

        var snap = ReadSnapshot();
        var changed = _forceStatus || snap != LocalSnapshot();
        lock (_statusLock)
            _localSnapshot = snap;

        if (!changed)
            return;

        if (Config.ActAsHub) {
            _forceStatus = false;
            PushPeers();
            return;
        }

        if (!HubConnected)
            return;

        _forceStatus = false;
        WriteHub(new StatusMessage(snap));
    }

    private ClientSnapshot LocalSnapshot() {
        lock (_statusLock)
            return _localSnapshot;
    }

    private ClientSnapshot ReadSnapshot() {
        var (dead, rezReady) = ReadLife();
        var (vbmDefault, vbmAi) = GetPresetStates();
        bool duty;
        lock (_dutyLock)
            duty = _dutyOverride != null;

        var snap = ClientSnapshot.Empty with {
            SessionId = _sessionId,
            JobId = (byte)IPlayerState.Get().ClassJob.RowId,
            Dead = dead,
            RezReady = rezReady,
            VbmDefault = vbmDefault,
            VbmAi = vbmAi,
            Duty = duty,
        };

        if (IPlayerState.Get() is { IsLoaded: true, ContentId: var cid, CharacterName: { IsEmpty: false } name, HomeWorld: { IsValid: true, RowId: var worldId, Value.Name: var world } })
            snap = snap with { Cid = cid, Name = name, WorldId = (ushort)worldId, World = world.ToString() };

        return snap;
    }

    private static (bool Dead, bool RezReady) ReadLife() {
        if (IObjectTable.Get().LocalPlayer is not { } player)
            return (false, false);

        var dead = player.IsDead;
        return (dead, dead && player.ReviveState == ReviveState.Revivable);
    }

    private void FormParty() {
        var peers = GetPeers().Where(p => p.Cid != 0 && !IsInParty(p.Cid)).ToList();
        if (peers.Count == 0) {
            Log("Form Party: no inviteable clients");
            return;
        }

        _ = IFramework.Get().Run(() => {
            var invited = 0;
            foreach (var peer in peers) {
                if (TryInviteClient(peer))
                    invited++;
            }
            Log($"Form Party: invited {invited} client(s)");
        });
    }

    private unsafe bool TryInviteClient(ClientSnapshot peer) {
        if (peer.Cid == 0 || peer.Name.IsEmpty || peer.WorldId == 0) {
            Warning($"Cannot invite {peer.DisplayName}: missing CID/name/world");
            return false;
        }

        if (IsInParty(peer.Cid)) {
            Log($"Skipping invite for {peer.DisplayName}: already in party");
            return false;
        }

        try {
            InfoProxyPartyInvite.Instance()->InviteToParty(peer.Cid, peer.Name, peer.WorldId);
            Log($"Invited {peer.DisplayName}");
            return true;
        }
        catch (Exception ex) {
            Error(ex, $"Failed to invite {peer.DisplayName}");
            ModuleMessage($"Failed to invite {peer.DisplayName}");
            return false;
        }
    }

    private static bool IsInParty(ulong cid)
        => cid != 0 && IPartyList.Get().Any(p => p.ContentId == cid);

    private List<ClientSnapshot> GetPeers() {
        lock (_lock) {
            return Config.ActAsHub
                ? [.. _clients.Where(c => c.SessionId != 0).Select(c => c.Snapshot).OrderBy(p => p.Name)]
                : [.. _clientPeers];
        }
    }

    private void SendCommand(int? targetId, string? command, bool remember = false, bool includeSelf = false) {
        var cmd = NormalizeCommand(command);
        if (cmd is null) {
            ModuleMessage("Enter a command starting with /");
            return;
        }

        var remote = targetId != _sessionId;
        if (remote && !HasPeers()) {
            ModuleMessage("No other clients connected.");
            return;
        }

        if (remember)
            RememberCommand(cmd);

        if (remote)
            Log(targetId is null ? $"Broadcasting: {cmd}" : $"Sending to {targetId}: {cmd}");

        Dispatch(new CmdMessage(targetId, cmd));

        if (includeSelf && targetId is null)
            ExecuteRemoteCommand(cmd);
    }

    private void SendAction(int? targetId, RemoteAction action) {
        var remote = targetId != _sessionId;
        if (remote && !HasPeers()) {
            ModuleMessage("No other clients connected.");
            return;
        }

        if (remote)
            Log(targetId is null ? $"Broadcasting action: {action}" : $"Sending {action} to {targetId}");

        Dispatch(new ActionMessage(targetId, action));
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
