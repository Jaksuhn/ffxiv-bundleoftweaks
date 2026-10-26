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
                return _hubWriter != null;
        }
    }

    private void StartNetwork() {
        StopNetwork();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        var hub = Config.ActAsHub;
        _networkTask = Task.Run(() => hub ? RunHubAsync(token) : RunClientAsync(token), token);
        if (Config.AllowNetwork)
            StartLan();
    }

    private void StopNetwork() {
        StopLan();
        _cts?.Cancel();

        CloseHubStream();
        ClearClients();

        WaitQuietly(_networkTask);

        _cts?.Dispose();
        _cts = null;
        _networkTask = null;

        lock (_lock)
            _clientPeers = [];
    }

    private void WaitQuietly(Task? task) {
        try {
            task?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException ex) when (ex.InnerExceptions.All(e => e is OperationCanceledException)) { }
        catch (AggregateException ex) {
            Warning(ex, "Multibox network task failed");
        }
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
        var hub = Config.ActAsHub;
        _lanTask = Task.Run(() => hub ? RunLanHubAsync(token) : ListenBeaconAsync(token), token);
    }

    private void StopLan() {
        _lanCts?.Cancel();
        CloseHubStream(tcpOnly: true);
        WaitQuietly(_lanTask);
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
            bool connected;
            try {
                connected = await TryPipeSessionAsync(ct);
                if (!connected && Config.AllowNetwork)
                    connected = await TryTcpSessionAsync(ct);
            }
            catch (OperationCanceledException) {
                return;
            }
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
        var writer = new MessageWriter(stream);
        lock (_lock) {
            _hubWriter = writer;
            _hubStreamIsTcp = tcp;
        }

        try {
            _forceStatus = true;
            _nextStatusTick = 0;

            while (!ct.IsCancellationRequested && await ReadFrameAsync(stream, ct) is { } frame) {
                if (Parse(frame) is { } message)
                    HandleClientMessage(message);
            }
        }
        finally {
            lock (_lock) {
                if (_hubWriter == writer) {
                    _hubWriter = null;
                    _hubStreamIsTcp = false;
                }
                _clientPeers = [];
            }
            writer.Dispose();
            _hubSearchStart = Environment.TickCount64;
        }
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
                pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, MaxClients, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
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
            if (!ct.IsCancellationRequested && _clients.Count < MaxClients) {
                _clients.Add(client);
                accepted = true;
            }
        }

        if (!accepted) {
            client.Dispose();
            return;
        }

        try {
            while (!ct.IsCancellationRequested && await ReadFrameAsync(stream, ct) is { } frame) {
                if (Parse(frame) is { } message)
                    HandleHubMessage(message, client);
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
            client.Dispose();
            if (!ct.IsCancellationRequested)
                PushPeers();
        }
    }

    private void HandleHubMessage(MultiboxMessage message, HubClient client) {
        switch (message) {
            case StatusMessage status:
                if (client.SessionId == 0 && status.Snapshot.SessionId != 0)
                    Log($"Registered {status.Snapshot.DisplayName} (CID {status.Snapshot.Cid}, protocol v{status.Snapshot.Version})");
                client.Snapshot = status.Snapshot;
                PushPeers();
                break;

            case RoutedMessage routed when client.SessionId != 0:
                HubRoute(client.SessionId, routed);
                break;
        }
    }

    private void HandleClientMessage(MultiboxMessage message) {
        switch (message) {
            case PeersMessage peers:
                lock (_lock)
                    _clientPeers = [.. peers.Peers.OrderBy(p => p.Name)];
                break;
            case RoutedMessage routed:
                RunLocal(routed);
                break;
        }
    }

    private void Dispatch(RoutedMessage message) {
        if (message.TargetId == _sessionId)
            RunLocal(message);
        else if (Config.ActAsHub)
            HubRoute(_sessionId, message);
        else
            WriteHub(message);
    }

    private void HubRoute(int fromId, RoutedMessage message) {
        var target = message.TargetId;
        if (target == _sessionId || (target is null && fromId != _sessionId))
            RunLocal(message);
        if (target == _sessionId)
            return;

        foreach (var client in SnapshotClients().Where(c => c.SessionId != 0 && c.SessionId != fromId && (target is null || c.SessionId == target)))
            client.Send(message);
    }

    private void RunLocal(RoutedMessage message) {
        switch (message) {
            case CmdMessage cmd:
                ExecuteRemoteCommand(cmd.Command);
                break;
            case ActionMessage action:
                ExecuteRemoteAction(action.Action);
                break;
            case VbmMessage vbm:
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

        var clients = SnapshotClients().Where(c => c.SessionId != 0).ToList();
        var all = clients.Select(c => c.Snapshot).Prepend(LocalSnapshot()).ToList();
        foreach (var client in clients)
            client.Send(new PeersMessage([.. all.Where(p => p.SessionId != client.SessionId)]));
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
        MessageWriter? writer;
        lock (_lock)
            writer = _hubWriter;
        writer?.Send(message);
    }

    private void CloseHubStream(bool tcpOnly = false) {
        MessageWriter? writer;
        lock (_lock) {
            if (tcpOnly && !_hubStreamIsTcp)
                return;
            writer = _hubWriter;
            _hubWriter = null;
            _hubStreamIsTcp = false;
        }
        writer?.Dispose();
    }

    private static byte[] Frame(MultiboxMessage message) {
        var payload = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
        var frame = new byte[payload.Length + 4];
        BitConverter.TryWriteBytes(frame, payload.Length);
        payload.CopyTo(frame, 4);
        return frame;
    }

    private static async Task<byte[]?> ReadFrameAsync(Stream stream, CancellationToken ct) {
        var lengthBuffer = new byte[4];
        if (!await ReadExactAsync(stream, lengthBuffer, ct))
            return null;

        var length = BitConverter.ToInt32(lengthBuffer);
        if (length is <= 0 or > MaxMessageBytes)
            throw new InvalidDataException($"Invalid multibox message length: {length}");

        var payload = new byte[length];
        return await ReadExactAsync(stream, payload, ct) ? payload : null;
    }

    private MultiboxMessage? Parse(byte[] payload) {
        try {
            return JsonSerializer.Deserialize<MultiboxMessage>(payload, JsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException) {
            Debug($"Skipping unreadable message, peer is on another version: {ex.Message}");
            return null;
        }
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
