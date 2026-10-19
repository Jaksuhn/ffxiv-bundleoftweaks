using Lumina.Excel.Sheets;
using System.IO;
using System.Text.Json.Serialization;
using System.Threading;

namespace ComplexTweaks.Tweaks;

public partial class MultiboxTools {
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
    [JsonDerivedType(typeof(StatusMessage), "status")]
    [JsonDerivedType(typeof(VbmMessage), "vbm")]
    [JsonDerivedType(typeof(RunVbmMessage), "runVbm")]
    [JsonDerivedType(typeof(DutyMessage), "duty")]
    private abstract record MultiboxMessage;

    private sealed record HelloMessage(int Pid, ulong Cid, string Name, ushort WorldId) : MultiboxMessage;
    private sealed record CmdMessage(int? TargetPid, string Command) : MultiboxMessage;
    private sealed record ExecMessage(string Command) : MultiboxMessage;
    private sealed record ActionMessage(int? TargetPid, RemoteAction Action) : MultiboxMessage;
    private sealed record RunActionMessage(RemoteAction Action) : MultiboxMessage;
    private sealed record PeersMessage(List<PeerInfo> Peers) : MultiboxMessage;
    private sealed record StatusMessage(int Pid, ulong Cid, string Name, ushort WorldId, byte JobId, bool Dead, bool RezReady, bool VbmDefault, bool VbmAi, bool Duty) : MultiboxMessage;
    private sealed record VbmMessage(int? TargetPid, string Preset, bool Enabled) : MultiboxMessage;
    private sealed record RunVbmMessage(string Preset, bool Enabled) : MultiboxMessage;
    private sealed record DutyMessage(DutyWire? Duty, int? TargetPid = null) : MultiboxMessage;

    private sealed record DutyWire(string? StartCommand, string? EndCommand, bool AutoLeaveOnEnd, int TimeToWait) {
        public DutyCommands ToConfig() => new() {
            StartCommand = StartCommand ?? string.Empty,
            EndCommand = EndCommand ?? string.Empty,
            AutoLeaveOnEnd = AutoLeaveOnEnd,
            TimeToWait = TimeToWait,
        };

        public static DutyWire From(DutyCommands config) => new(config.StartCommand, config.EndCommand, config.AutoLeaveOnEnd, config.TimeToWait);
    }

    private readonly record struct PeerInfo(int Pid, string Name, string World, byte JobId, bool Dead, bool RezReady, bool VbmDefault, bool VbmAi, bool Duty) {
        public string DisplayName => World.IsEmpty ? Name : $"{Name}@{World}";
        public bool PresetOn(string preset) => preset == VbmDefaultPreset ? VbmDefault : preset == VbmAiPreset && VbmAi;
    }

    private readonly record struct ClientSnapshot(int Pid, ulong Cid, string Name, ushort WorldId, string World, byte JobId, bool Dead, bool RezReady, bool VbmDefault, bool VbmAi, bool Duty) {
        public PeerInfo ToPeer() => new(Pid, Name, World, JobId, Dead, RezReady, VbmDefault, VbmAi, Duty);
        public bool PresetOn(string preset) => preset == VbmDefaultPreset ? VbmDefault : preset == VbmAiPreset && VbmAi;
    }

    private sealed class HubClient(Stream stream) : IDisposable {
        private readonly Lock _writeLock = new();
        public int Pid { get; set; }
        public ulong Cid { get; set; }
        public string Name { get; set; } = "Unknown";
        public ushort WorldId { get; set; }
        public byte JobId { get; set; }
        public bool Dead { get; set; }
        public bool RezReady { get; set; }
        public bool VbmDefault { get; set; }
        public bool VbmAi { get; set; }
        public bool Duty { get; set; }
        public string WorldName => World.GetRowOrNull(WorldId)?.Name.ToString() ?? WorldId.ToString();
        public string DisplayName => WorldId == 0 ? Name : $"{Name}@{WorldName}";
        public PeerInfo ToPeer() => new(Pid, Name, WorldName, JobId, Dead, RezReady, VbmDefault, VbmAi, Duty);

        public void Send(MultiboxMessage message) {
            lock (_writeLock) {
                try { WriteMessage(stream, message); }
                catch { } // ded
            }
        }

        public void Dispose() { }
    }
}
