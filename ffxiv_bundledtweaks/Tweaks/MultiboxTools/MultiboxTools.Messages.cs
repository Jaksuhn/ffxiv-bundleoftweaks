using System.IO;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace ComplexTweaks.Tweaks;

public partial class MultiboxTools {
    private const int MBTVersion = 3;

    private enum RemoteAction {
        Leave,
        Logout,
        Exit,
    }

    [JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
    [JsonDerivedType(typeof(StatusMessage), "status")]
    [JsonDerivedType(typeof(PeersMessage), "peers")]
    [JsonDerivedType(typeof(CmdMessage), "cmd")]
    [JsonDerivedType(typeof(ActionMessage), "action")]
    [JsonDerivedType(typeof(VbmMessage), "vbm")]
    [JsonDerivedType(typeof(DutyMessage), "duty")]
    [JsonDerivedType(typeof(GearsetRequestMessage), "gearsetRequest")]
    [JsonDerivedType(typeof(GearsetListMessage), "gearsetList")]
    [JsonDerivedType(typeof(EquipGearsetMessage), "equipGearset")]
    private abstract record MultiboxMessage;

    private sealed record StatusMessage(ClientSnapshot Snapshot) : MultiboxMessage;
    private sealed record PeersMessage(List<ClientSnapshot> Peers) : MultiboxMessage;

    private abstract record RoutedMessage(int? TargetId) : MultiboxMessage; // null is a broadcast
    private sealed record CmdMessage(int? TargetId, string Command) : RoutedMessage(TargetId);
    private sealed record ActionMessage(int? TargetId, RemoteAction Action) : RoutedMessage(TargetId);
    private sealed record VbmMessage(int? TargetId, string Preset, bool Enabled) : RoutedMessage(TargetId);
    private sealed record DutyMessage(int? TargetId, DutyWire? Duty) : RoutedMessage(TargetId);
    private sealed record GearsetRequestMessage(int? TargetId, int ReplyTo) : RoutedMessage(TargetId);
    private sealed record GearsetListMessage(int? TargetId, int Owner, List<GearsetInfo> Gearsets) : RoutedMessage(TargetId);
    private sealed record EquipGearsetMessage(int? TargetId, byte Index, byte JobId) : RoutedMessage(TargetId);

    private sealed record GearsetInfo(byte Index, byte JobId, string Name, short ItemLevel);

    private sealed record DutyWire(string? StartCommand, string? EndCommand, bool AutoLeaveOnEnd, int TimeToWait) {
        public DutyCommands ToConfig() => new() {
            StartCommand = StartCommand ?? string.Empty,
            EndCommand = EndCommand ?? string.Empty,
            AutoLeaveOnEnd = AutoLeaveOnEnd,
            TimeToWait = TimeToWait,
        };

        public static DutyWire From(DutyCommands config) => new(config.StartCommand, config.EndCommand, config.AutoLeaveOnEnd, config.TimeToWait);
    }

    private sealed record ClientSnapshot(int SessionId, int Version, ulong Cid, string Name, ushort WorldId, string World, byte JobId, bool Dead, bool RezReady, bool VbmDefault, bool VbmAi, bool Duty) {
        public static readonly ClientSnapshot Empty = new(0, MBTVersion, 0, "Not Logged In", 0, string.Empty, 0, false, false, false, false, false);

        [JsonIgnore]
        public string DisplayName => World.IsEmpty ? Name : $"{Name}@{World}";
        public bool PresetOn(string preset) => preset == VbmDefaultPreset ? VbmDefault : preset == VbmAiPreset && VbmAi;
    }

    private sealed class MessageWriter : IDisposable {
        private const int MaxQueued = 64;

        private readonly Stream _stream;
        private readonly CancellationTokenSource _cts = new();
        private readonly Channel<byte[]> _queue = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(MaxQueued) {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });
        private int _disposed;

        public MessageWriter(Stream stream) {
            _stream = stream;
            var token = _cts.Token;
            _ = Task.Run(() => PumpAsync(token));
        }

        public void Send(MultiboxMessage message) => _queue.Writer.TryWrite(Frame(message));

        private async Task PumpAsync(CancellationToken ct) {
            try {
                await foreach (var frame in _queue.Reader.ReadAllAsync(ct)) {
                    await _stream.WriteAsync(frame, ct);
                    await _stream.FlushAsync(ct);
                }
            }
            catch { } // peer gone, the read loop handles the disconnect
        }

        public void Dispose() {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            _queue.Writer.TryComplete();
            _cts.Cancel();
            try { _stream.Dispose(); }
            catch { }
        }
    }

    private sealed class HubClient(Stream stream) : IDisposable {
        private readonly MessageWriter _writer = new(stream);
        public ClientSnapshot Snapshot { get; set; } = ClientSnapshot.Empty;
        public int SessionId => Snapshot.SessionId;

        public void Send(MultiboxMessage message) => _writer.Send(message);

        public void Dispose() => _writer.Dispose();
    }
}
