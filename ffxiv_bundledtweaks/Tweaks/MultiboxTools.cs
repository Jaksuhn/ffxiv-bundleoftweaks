using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using Lumina.Excel.Sheets;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace ComplexTweaks.Tweaks;

public class MultiboxToolsConfig {
    [BoolConfig(Label = "Act as Hub", Description = "Enable on exactly one instance to mark this account as the controller. Other instances connect as clients.")]
    public bool ActAsHub;
}

public class MultiboxTools : Tweak<MultiboxToolsConfig> {
    public override string Name => "Multibox Tools";
    public override string Description => $"Connect to other local {Svc.Interface.Manifest.Name} instances and manage them.";

    private const string PipeName = "CBT.Multibox";
    private const int MaxClients = 8;
    private const int MaxMessageBytes = 1_048_576;

    private static readonly JsonSerializerOptions JsonOptions = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly int _pid = Environment.ProcessId;
    private readonly Lock _lock = new();
    private readonly List<HubClient> _clients = [];
    private List<PeerInfo> _clientPeers = [];

    private CancellationTokenSource? _cts;
    private Task? _networkTask;
    private Stream? _hubStream;
    private ulong _localCid;
    private string _localName = "Not Logged In";
    private ushort _localWorldId;
    private string _localWorld = string.Empty;
    private string _commandInput = string.Empty;

    public override void OnEnable() {
        RefreshIdentity();
        IClientState.Get().Login += OnLogin;
        StartNetwork();
    }

    public override void OnDisable() {
        IClientState.Get().Login -= OnLogin;
        StopNetwork();
    }

    [CommandHandler("/broadcast", "Send a command to all other MultiboxTools clients on this PC")]
    private void OnBroadcast(string command, string arguments) {
        if (arguments.IsEmpty) {
            ModuleMessage("Usage: /broadcast <command>");
            return;
        }

        SendCommand(null, arguments);
    }

    public override void DrawConfig() {
        ImGui.DrawSection("Connection");

        var actAsHub = Config.ActAsHub;
        if (ImGui.Checkbox("Act as Hub", ref actAsHub)) {
            Config.ActAsHub = actAsHub;
            StartNetwork();
        }
        ImGuiComponents.HelpMarker("Enable on exactly one instance to mark this account as the controller. Others leave this unchecked and connect as clients.");

        ImGui.DrawSection("Connected Clients");

        var iconBtnSize = GetIconButtonSize();
        var hasPeers = (Config.ActAsHub ? GetHubClients().Count : GetPeers().Count) > 0;

        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - iconBtnSize.X - ImGui.GetStyle().ItemSpacing.X);
        if (ImGui.InputTextWithHint("##MultiboxCommand", "/li inn", ref _commandInput, 512, ImGuiInputTextFlags.EnterReturnsTrue))
            SendCommand(null, _commandInput);

        ImGui.SameLine();
        if (ImGui.IconButton(FontAwesomeIcon.Share, "broadcast", "Broadcast command to all", disabled: !hasPeers))
            SendCommand(null, _commandInput);

        if (Config.ActAsHub) {
            if (ImGui.IconButton(FontAwesomeIcon.Users, "party", "Invite all to party", disabled: !hasPeers))
                FormParty();

            ImGui.SameLine();
            if (ImGui.IconButton(FontAwesomeIcon.MapMarkerAlt, "tpZoneAll", "Teleport all to controller", disabled: !hasPeers))
                SendHubZone(null);

            ImGui.SameLine();
            if (ImGui.IconButton(FontAwesomeIcon.Globe, "tpWorldAll", "Travel all to controller world", disabled: !hasPeers))
                SendHubWorld(null);

            ImGui.SameLine();
        }

        if (ImGui.IconButton(FontAwesomeIcon.DoorOpen, "leaveAll", "All leave duty", disabled: !hasPeers))
            SendAction(null, RemoteAction.Leave);

        ImGui.SameLine();
        if (ImGui.IconButton(FontAwesomeIcon.SignOutAlt, "logoutAll", "Logout all", disabled: !hasPeers))
            SendAction(null, RemoteAction.Logout);

        ImGui.SameLine();
        if (ImGui.IconButton(FontAwesomeIcon.PowerOff, "exitAll", "All exit game", disabled: !hasPeers))
            SendAction(null, RemoteAction.Exit);

        ImGui.Spacing();

        if (Config.ActAsHub)
            DrawHubClients();
        else
            DrawClientPeers();
    }

    private void DrawHubClients() {
        var clients = GetHubClients();
        if (clients.Count == 0) {
            ImGui.TextColoredWrapped(Colors.Grey, "Waiting for clients...");
            return;
        }

        using var table = ImRaii.Table("##MultiboxPeers", 2, ImGuiTableFlags.SizingStretchProp);
        if (!table)
            return;

        ImGui.TableSetupColumn("Client", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, GetActionBlockWidth(7));

        foreach (var client in clients) {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(client.DisplayName);

            ImGui.TableNextColumn();
            if (ImGui.IconButton(FontAwesomeIcon.PaperPlane, $"send{client.Pid}", "Send command"))
                SendCommand(client.Pid, _commandInput);

            ImGui.SameLine();
            var inParty = IsInParty(client.Cid);
            if (ImGui.IconButton(FontAwesomeIcon.UserPlus, $"invite{client.Pid}", inParty ? "Already in party" : "Invite to party", disabled: inParty || client.Cid == 0 || client.WorldId == 0))
                _ = IFramework.Get().Run(() => TryInviteClient(client));

            ImGui.SameLine();
            if (ImGui.IconButton(FontAwesomeIcon.MapMarkerAlt, $"tpZone{client.Pid}", "Teleport to controller zone"))
                SendHubZone(client.Pid);

            ImGui.SameLine();
            if (ImGui.IconButton(FontAwesomeIcon.Globe, $"tpWorld{client.Pid}", "Travel to controller world"))
                SendHubWorld(client.Pid);

            ImGui.SameLine();
            if (ImGui.IconButton(FontAwesomeIcon.DoorOpen, $"leave{client.Pid}", "Leave duty"))
                SendAction(client.Pid, RemoteAction.Leave);

            ImGui.SameLine();
            if (ImGui.IconButton(FontAwesomeIcon.SignOutAlt, $"logout{client.Pid}", "Logout"))
                SendAction(client.Pid, RemoteAction.Logout);

            ImGui.SameLine();
            if (ImGui.IconButton(FontAwesomeIcon.PowerOff, $"exit{client.Pid}", "Exit game"))
                SendAction(client.Pid, RemoteAction.Exit);
        }
    }

    private void DrawClientPeers() {
        var peers = GetPeers();
        if (peers.Count == 0) {
            ImGui.TextColoredWrapped(Colors.Grey, "Waiting for hub...");
            return;
        }

        using var table = ImRaii.Table("##MultiboxPeers", 2, ImGuiTableFlags.SizingStretchProp);
        if (!table)
            return;

        ImGui.TableSetupColumn("Client", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, GetActionBlockWidth(4));

        foreach (var peer in peers) {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(peer.DisplayName);

            ImGui.TableNextColumn();
            if (ImGui.IconButton(FontAwesomeIcon.PaperPlane, $"send{peer.Pid}", "Send command"))
                SendCommand(peer.Pid, _commandInput);

            ImGui.SameLine();
            if (ImGui.IconButton(FontAwesomeIcon.DoorOpen, $"leave{peer.Pid}", "Leave duty"))
                SendAction(peer.Pid, RemoteAction.Leave);

            ImGui.SameLine();
            if (ImGui.IconButton(FontAwesomeIcon.SignOutAlt, $"logout{peer.Pid}", "Logout"))
                SendAction(peer.Pid, RemoteAction.Logout);

            ImGui.SameLine();
            if (ImGui.IconButton(FontAwesomeIcon.PowerOff, $"exit{peer.Pid}", "Exit game"))
                SendAction(peer.Pid, RemoteAction.Exit);
        }
    }

    private static Vector2 GetIconButtonSize() {
        using (ImRaii.PushFont(UiBuilder.IconFont))
            return ImGuiHelpers.GetButtonSize(FontAwesomeIcon.PowerOff.ToIconString());
    }

    private static float GetActionBlockWidth(int buttonCount) {
        var btn = GetIconButtonSize();
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var rightPad = ImGui.GetStyle().ItemSpacing.X + ImGui.GetStyle().FramePadding.X;
        return buttonCount * btn.X + (buttonCount - 1) * spacing + rightPad;
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
        RefreshIdentity();
        if (Config.ActAsHub)
            PushPeers();
        else
            WriteHello();
    }

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
        var clients = GetHubClients().Where(c => !IsInParty(c.Cid)).ToList();
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
        => [.. SnapshotClients().Where(c => c.Pid != 0 && c.Cid != 0).OrderBy(c => c.DisplayName)];

    private void StartNetwork() {
        StopNetwork();
        _cts = new CancellationTokenSource();
        _networkTask = Task.Run(() => Config.ActAsHub ? RunHubAsync(_cts.Token) : RunClientAsync(_cts.Token), _cts.Token);
    }

    private void StopNetwork() {
        _cts?.Cancel();

        CloseHubStream();
        ClearClients();

        _networkTask?.Wait(TimeSpan.FromSeconds(2));

        _cts?.Dispose();
        _cts = null;
        _networkTask = null;

        lock (_lock)
            _clientPeers = [];
    }

    private List<PeerInfo> GetPeers() {
        if (Config.ActAsHub) {
            lock (_lock)
                return [.. _clients.Where(c => c.Pid != 0).Select(c => c.ToPeer()).OrderBy(p => p.DisplayName)];
        }

        lock (_lock)
            return [.. _clientPeers];
    }

    private void SendCommand(int? targetPid, string? command) {
        var cmd = NormalizeCommand(command);
        if (cmd is null) {
            ModuleMessage("Enter a command starting with /");
            return;
        }

        if (!HasPeers()) {
            ModuleMessage("No other clients connected.");
            return;
        }

        if (targetPid is null)
            Log($"Broadcasting: {cmd}");
        else
            Log($"Sending to {targetPid}: {cmd}");

        if (Config.ActAsHub)
            HubRouteCommand(_pid, targetPid, cmd);
        else
            WriteHub(new CmdMessage(targetPid, cmd));
    }

    private void SendAction(int? targetPid, RemoteAction action) {
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

    private bool HasPeers()
        => (Config.ActAsHub ? GetHubClients().Count : GetPeers().Count) > 0;

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

    private async Task RunClientAsync(CancellationToken ct) {
        while (!ct.IsCancellationRequested) {
            NamedPipeClientStream? pipe = null;
            try {
                pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await pipe.ConnectAsync(1000, ct);
            }
            catch (OperationCanceledException) {
                if (pipe != null) await pipe.DisposeAsync();
                return;
            }
            catch {
                if (pipe != null) await pipe.DisposeAsync();
                try { await Task.Delay(500, ct); }
                catch (OperationCanceledException) { return; }
                continue;
            }

            try {
                await using (pipe) {
                    lock (_lock)
                        _hubStream = pipe;

                    WriteHello();

                    while (!ct.IsCancellationRequested && pipe.IsConnected) {
                        var message = await ReadMessageAsync(pipe, ct);
                        if (message is null)
                            break;
                        HandleClientMessage(message);
                    }
                }
            }
            catch (OperationCanceledException) {
                return;
            }
            catch (Exception ex) {
                if (!ct.IsCancellationRequested)
                    Warning(ex, "Multibox client error");
            }
            finally {
                CloseHubStream();
                lock (_lock)
                    _clientPeers = [];
            }

            try { await Task.Delay(250, ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task RunHubAsync(CancellationToken ct) {
        Log("Running as multibox hub");

        while (!ct.IsCancellationRequested) {
            NamedPipeServerStream? pipe = null;
            try {
                pipe = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    MaxClients,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await pipe.WaitForConnectionAsync(ct);
                var connected = pipe;
                pipe = null;
                _ = Task.Run(() => ServeClientAsync(connected, ct), ct);
            }
            catch (OperationCanceledException) {
                break;
            }
            catch (IOException) when (ct.IsCancellationRequested) {
                break;
            }
            catch (Exception ex) {
                if (!ct.IsCancellationRequested)
                    Warning(ex, "Multibox hub accept error");
                try { await Task.Delay(250, ct); }
                catch (OperationCanceledException) { break; }
            }
            finally {
                if (pipe != null)
                    await pipe.DisposeAsync();
            }
        }

        ClearClients();
        Log("Stopped multibox hub");
    }

    private async Task ServeClientAsync(NamedPipeServerStream pipe, CancellationToken ct) {
        var client = new HubClient(pipe);
        lock (_lock)
            _clients.Add(client);

        try {
            await using (pipe) {
                while (!ct.IsCancellationRequested && pipe.IsConnected) {
                    var message = await ReadMessageAsync(pipe, ct);
                    if (message is null)
                        break;
                    HandleHubMessage(message, client);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) {
            if (!ct.IsCancellationRequested)
                Warning(ex, "Error handling multibox client");
        }
        finally {
            lock (_lock)
                _clients.Remove(client);
            PushPeers();
        }
    }

    private void HandleHubMessage(MultiboxMessage message, HubClient client) {
        switch (message) {
            case HelloMessage hello:
                client.Pid = hello.Pid;
                client.Cid = hello.Cid;
                client.Name = hello.Name;
                client.WorldId = hello.WorldId;
                Log($"Registered {client.DisplayName} (CID {hello.Cid})");
                PushPeers();
                break;

            case CmdMessage cmd when client.Pid != 0:
                HubRouteCommand(client.Pid, cmd.TargetPid, cmd.Command);
                break;

            case ActionMessage action when client.Pid != 0:
                HubRouteAction(client.Pid, action.TargetPid, action.Action);
                break;
        }
    }

    private void HandleClientMessage(MultiboxMessage message) {
        switch (message) {
            case PeersMessage peers:
                lock (_lock)
                    _clientPeers = [.. peers.Peers.OrderBy(p => p.DisplayName)];
                break;
            case ExecMessage exec:
                ExecuteRemoteCommand(exec.Command);
                break;
            case RunActionMessage run:
                ExecuteRemoteAction(run.Action);
                break;
        }
    }

    private void PushPeers() {
        if (!Config.ActAsHub)
            return;

        var all = new List<PeerInfo> { new(_pid, _localName, _localWorld) };
        lock (_lock)
            all.AddRange(_clients.Where(c => c.Pid != 0).Select(c => c.ToPeer()));

        foreach (var client in SnapshotClients().Where(c => c.Pid != 0)) {
            var peers = all.Where(p => p.Pid != client.Pid).ToList();
            client.Send(new PeersMessage(peers));
        }
    }

    private List<HubClient> SnapshotClients() {
        lock (_lock)
            return [.. _clients];
    }

    private void ClearClients() {
        List<HubClient> clients;
        lock (_lock) {
            clients = [.. _clients];
            _clients.Clear();
        }
        foreach (var client in clients)
            client.Dispose();
    }

    private void WriteHub(MultiboxMessage message) {
        lock (_lock) {
            try {
                if (_hubStream is null)
                    return;
                WriteMessage(_hubStream, message);
            }
            catch (Exception ex) {
                Warning(ex, "Failed to send to multibox hub");
            }
        }
    }

    private void CloseHubStream() {
        lock (_lock)
            _hubStream = null;
    }

    private static void WriteMessage(Stream stream, MultiboxMessage message) {
        var payload = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
        Span<byte> length = stackalloc byte[4];
        BitConverter.TryWriteBytes(length, payload.Length);
        stream.Write(length);
        stream.Write(payload);
        stream.Flush();
    }

    private static async Task<MultiboxMessage?> ReadMessageAsync(Stream stream, CancellationToken ct) {
        var lengthBuffer = new byte[4];
        if (!await ReadExactAsync(stream, lengthBuffer, ct))
            return null;

        var length = BitConverter.ToInt32(lengthBuffer);
        if (length is <= 0 or > MaxMessageBytes)
            throw new InvalidDataException($"Invalid multibox message length: {length}");

        var payload = new byte[length];
        if (!await ReadExactAsync(stream, payload, ct))
            return null;

        return JsonSerializer.Deserialize<MultiboxMessage>(payload, JsonOptions) ?? throw new InvalidDataException("Failed to deserialize multibox message");
    }

    private static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct) {
        var offset = 0;
        while (offset < buffer.Length) {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), ct);
            if (read == 0)
                return false;
            offset += read;
        }
        return true;
    }

    private static string? NormalizeCommand(string? command) {
        if (string.IsNullOrWhiteSpace(command))
            return null;

        var cmd = command.Trim();
        return cmd.StartsWith('/') ? cmd : "/" + cmd;
    }

    private enum RemoteAction {
        Leave,
        Logout,
        Exit,
    }

    [JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
    [JsonDerivedType(typeof(HelloMessage), "hello")]
    [JsonDerivedType(typeof(CmdMessage), "cmd")]
    [JsonDerivedType(typeof(ExecMessage), "exec")]
    [JsonDerivedType(typeof(ActionMessage), "action")]
    [JsonDerivedType(typeof(RunActionMessage), "runAction")]
    [JsonDerivedType(typeof(PeersMessage), "peers")]
    private abstract record MultiboxMessage;

    private sealed record HelloMessage(int Pid, ulong Cid, string Name, ushort WorldId) : MultiboxMessage;
    private sealed record CmdMessage(int? TargetPid, string Command) : MultiboxMessage;
    private sealed record ExecMessage(string Command) : MultiboxMessage;
    private sealed record ActionMessage(int? TargetPid, RemoteAction Action) : MultiboxMessage;
    private sealed record RunActionMessage(RemoteAction Action) : MultiboxMessage;
    private sealed record PeersMessage(List<PeerInfo> Peers) : MultiboxMessage;

    private readonly record struct PeerInfo(int Pid, string Name, string World) {
        public string DisplayName => World.IsEmpty ? Name : $"{Name}@{World}";
    }

    private sealed class HubClient(Stream stream) : IDisposable {
        private readonly Lock _writeLock = new();
        public int Pid { get; set; }
        public ulong Cid { get; set; }
        public string Name { get; set; } = "Unknown";
        public ushort WorldId { get; set; }
        public string WorldName => World.GetRowOrNull(WorldId)?.Name.ToString() ?? WorldId.ToString();
        public string DisplayName => WorldId == 0 ? Name : $"{Name}@{WorldName}";
        public PeerInfo ToPeer() => new(Pid, Name, WorldName);

        public void Send(MultiboxMessage message) {
            lock (_writeLock) {
                try { WriteMessage(stream, message); }
                catch { } // ded
            }
        }

        public void Dispose() { }
    }
}
