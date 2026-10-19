using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace ComplexTweaks.Tweaks;

public partial class MultiboxTools {
    private const string PipeName = "CBT.Multibox";
    private const int MaxClients = 8;
    private const int MaxMessageBytes = 1_048_576;

    private static readonly JsonSerializerOptions JsonOptions = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private bool HubConnected {
        get {
            lock (_lock)
                return _hubStream != null;
        }
    }

    private void StartNetwork() {
        StopNetwork();
        _cts = new CancellationTokenSource();
        _networkTask = Task.Run(() => Config.ActAsHub ? RunHubAsync(_cts.Token) : RunClientAsync(_cts.Token), _cts.Token);
        if (Config.AllowNetwork)
            StartLan();
    }

    private void StopNetwork() {
        StopLan();
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

    private void SetLanEnabled(bool enabled) {
        if (enabled)
            StartLan();
        else
            StopLan();
    }

    private void StartLan() {
        StopLan();
        if (_cts is not { IsCancellationRequested: false } cts)
            return;

        _lanCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
        var token = _lanCts.Token;
        _lanTask = Task.Run(() => Config.ActAsHub ? RunLanHubAsync(token) : ListenBeaconAsync(token), token);
    }

    private void StopLan() {
        _lanCts?.Cancel();
        DisconnectTcpSession();
        _lanTask?.Wait(TimeSpan.FromSeconds(2));
        _lanCts?.Dispose();
        _lanCts = null;
        _lanTask = null;
        lock (_lock) {
            _beaconEndpoint = null;
            _beaconSeenTick = 0;
        }
    }

    private async Task RunClientAsync(CancellationToken ct) {
        _hubSearchStart = Environment.TickCount64;
        while (!ct.IsCancellationRequested) {
            var connected = await TryPipeSessionAsync(ct);
            if (!connected && Config.AllowNetwork)
                connected = await TryTcpSessionAsync(ct);
            if (ct.IsCancellationRequested)
                return;

            if (connected) {
                try { await Task.Delay(250, ct); }
                catch (OperationCanceledException) { return; }
                continue;
            }

            if (Environment.TickCount64 - _hubSearchStart >= 3000)
                _hubMissing = true;

            try { await Task.Delay(500, ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task<bool> TryPipeSessionAsync(CancellationToken ct) {
        var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try {
            await pipe.ConnectAsync(1000, ct);
        }
        catch (OperationCanceledException) {
            await pipe.DisposeAsync();
            throw;
        }
        catch {
            await pipe.DisposeAsync();
            return false;
        }

        try {
            await using (pipe)
                await RunSessionAsync(pipe, tcp: false, ct);
            return true;
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch (Exception ex) {
            if (!ct.IsCancellationRequested)
                Warning(ex, "Multibox client error");
            return true;
        }
    }

    private async Task RunSessionAsync(Stream stream, bool tcp, CancellationToken ct) {
        _hubMissing = false;
        lock (_lock) {
            _hubStream = stream;
            _hubStreamIsTcp = tcp;
        }

        try {
            WriteHello();
            _forceStatus = true;
            _nextStatusTick = 0;

            while (!ct.IsCancellationRequested) {
                var message = await ReadMessageAsync(stream, ct);
                if (message is null)
                    break;
                HandleClientMessage(message);
            }
        }
        finally {
            CloseHubStream();
            _hubSearchStart = Environment.TickCount64;
            lock (_lock)
                _clientPeers = [];
        }
    }

    private void DisconnectTcpSession() {
        Stream? stream;
        lock (_lock) {
            if (!_hubStreamIsTcp)
                return;
            stream = _hubStream;
            _hubStream = null;
            _hubStreamIsTcp = false;
        }

        try { stream?.Dispose(); }
        catch { }
    }

    private async Task RunHubAsync(CancellationToken ct) {
        Log("Running as multibox hub");
        try {
            await RunPipeHubAsync(ct);
        }
        catch (OperationCanceledException) { }
        ClearClients();
        Log("Stopped multibox hub");
    }

    private async Task RunPipeHubAsync(CancellationToken ct) {
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
    }

    private async Task ServeClientAsync(Stream stream, CancellationToken ct) {
        var client = new HubClient(stream);
        var accepted = false;
        lock (_lock) {
            if (_clients.Count < MaxClients) {
                _clients.Add(client);
                accepted = true;
            }
        }

        if (!accepted) {
            await stream.DisposeAsync();
            return;
        }

        try {
            await using (stream) {
                while (!ct.IsCancellationRequested) {
                    var message = await ReadMessageAsync(stream, ct);
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

            case StatusMessage status:
                client.Pid = status.Pid;
                client.Cid = status.Cid;
                client.Name = status.Name;
                client.WorldId = status.WorldId;
                client.JobId = status.JobId;
                client.Dead = status.Dead;
                client.RezReady = status.RezReady;
                client.VbmDefault = status.VbmDefault;
                client.VbmAi = status.VbmAi;
                client.Duty = status.Duty;
                PushPeers();
                break;

            case VbmMessage vbm when client.Pid != 0:
                HubRouteVbm(client.Pid, vbm.TargetPid, vbm.Preset, vbm.Enabled);
                break;

            case DutyMessage duty when client.Pid != 0:
                HubRouteDuty(client.Pid, duty.Duty, duty.TargetPid);
                break;
        }
    }

    private void HandleClientMessage(MultiboxMessage message) {
        switch (message) {
            case PeersMessage peers:
                lock (_lock)
                    _clientPeers = [.. peers.Peers.OrderBy(p => p.Name)];
                break;
            case ExecMessage exec:
                ExecuteRemoteCommand(exec.Command);
                break;
            case RunActionMessage run:
                ExecuteRemoteAction(run.Action);
                break;
            case RunVbmMessage vbm:
                TogglePreset(vbm.Preset, vbm.Enabled);
                break;
            case DutyMessage duty:
                ApplyDuty(duty.Duty?.ToConfig(), updateDraft: true);
                break;
        }
    }

    private void PushPeers() {
        if (!Config.ActAsHub)
            return;

        var all = new List<PeerInfo> { LocalSnapshot().ToPeer() };
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
        lock (_lock) {
            _hubStream = null;
            _hubStreamIsTcp = false;
        }
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
}
