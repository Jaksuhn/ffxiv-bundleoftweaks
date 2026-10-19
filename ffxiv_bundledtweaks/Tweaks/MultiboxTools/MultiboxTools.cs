using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ComplexTweaks.Tweaks;

public class MultiboxToolsConfig {
    [BoolConfig(Label = "Act as Hub", Description = "Enable on exactly one instance to mark this account as the controller. Other instances connect as clients.")]
    public bool ActAsHub;
}

public partial class MultiboxTools : Tweak<MultiboxToolsConfig> {
    public override string Name => "Multibox Tools";
    public override string Description => $"Connect to other local {Svc.Interface.Manifest.Name} instances and manage them.";

    private readonly int _pid = Environment.ProcessId;
    private readonly Lock _lock = new();
    private readonly List<HubClient> _clients = [];
    private List<PeerInfo> _clientPeers = [];

    private readonly Lock _statusLock = new();
    private readonly Lock _dutyLock = new();
    private ClientSnapshot _localSnapshot = new(0, 0, "Not Logged In", 0, string.Empty, 0, false, false, false, false, false);
    private long _nextStatusTick;
    private volatile bool _forceStatus;

    private CancellationTokenSource? _cts;
    private Task? _networkTask;
    private Stream? _hubStream;
    private ulong _localCid;
    private string _localName = "Not Logged In";
    private ushort _localWorldId;
    private string _localWorld = string.Empty;
    private const int CommandHistoryLimit = 3;
    private readonly List<string> _commandHistory = [];
    private string _commandInput = string.Empty;
    private ClientSnapshot _drawSnap;
    private DutyCommands _dutyDraft = new();
    private DutyCommands? _dutyOverride;
    private uint _dutyTerritory;

    public override void OnEnable() {
        DiscoverCommands();
        IClientState.Get().Login += OnLogin;
        IFramework.Get().Update += OnFramework;
        IDutyState.Get().DutyStarted += OnDutyStart;
        IDutyState.Get().DutyCompleted += OnDutyComplete;
        IClientState.Get().TerritoryChanged += OnDutyTerritoryChanged;
        StartNetwork();
    }

    public override void OnDisable() {
        IClientState.Get().Login -= OnLogin;
        IFramework.Get().Update -= OnFramework;
        IDutyState.Get().DutyStarted -= OnDutyStart;
        IDutyState.Get().DutyCompleted -= OnDutyComplete;
        IClientState.Get().TerritoryChanged -= OnDutyTerritoryChanged;
        lock (_dutyLock)
            _dutyOverride = null;
        if (Automation.Running)
            Automation.Stop();
        StopNetwork();
    }

    [CommandHandler("/broadcast", "Send a command to all other MultiboxTools clients on this PC")]
    private void OnBroadcast(string command, string arguments) {
        if (arguments.IsEmpty) {
            ModuleMessage("Usage: /broadcast <command>");
            return;
        }

        SendCommand(null, arguments, remember: true);
    }

    public override void DrawConfig() {
        _drawSnap = LocalSnapshot();

        ImGui.DrawSection("Connection");

        var actAsHub = Config.ActAsHub;
        if (ImGui.Checkbox("Act as Hub", ref actAsHub)) {
            Config.ActAsHub = actAsHub;
            StartNetwork();
        }
        ImGuiComponents.HelpMarker("Enable on exactly one instance to mark this account as the controller. Others leave this unchecked and connect as clients.");

        ImGui.DrawSection("Connected Clients");

        var iconBtnSize = GetIconButtonSize();
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var hasPeers = (Config.ActAsHub ? GetHubClients().Count : GetPeers().Count) > 0;

        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - iconBtnSize.X * 2 - spacing * 2);
        if (ImGui.InputTextWithHint("##MultiboxCommand", "/li inn", ref _commandInput, 512, ImGuiInputTextFlags.EnterReturnsTrue))
            SendCommand(null, _commandInput, remember: true);

        ImGui.SameLine();
        if (ImGui.IconButton(FontAwesomeIcon.Share, "broadcast", "Broadcast command to all", disabled: !hasPeers))
            SendCommand(null, _commandInput, remember: true);

        ImGui.SameLine();
        DrawCommandHistory();

        DrawCommands(ToolbarContext(hasPeers), beforeSend: null);

        ImGui.Spacing();

        if (Config.ActAsHub)
            DrawHubClients();
        else
            DrawClientPeers();

        DrawDutySection();
    }

    private void DrawCommandHistory() {
        if (ImGui.IconButton(FontAwesomeIcon.History, "cmdHistory", "Recent commands", disabled: _commandHistory.Count == 0))
            ImGui.OpenPopup("##MultiboxCommandHistory");

        using var popup = ImRaii.Popup("##MultiboxCommandHistory");
        if (!popup)
            return;

        for (var i = 0; i < _commandHistory.Count; i++) {
            var cmd = _commandHistory[i];
            if (!ImGui.Selectable($"{cmd}##cmdHist{i}"))
                continue;

            _commandInput = cmd;
            ImGui.CloseCurrentPopup();
        }
    }

    private void RememberCommand(string command) {
        _commandHistory.Remove(command);
        _commandHistory.Insert(0, command);
        if (_commandHistory.Count > CommandHistoryLimit)
            _commandHistory.RemoveAt(CommandHistoryLimit);
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

    private bool HasPeers()
        => (Config.ActAsHub ? GetHubClients().Count : GetPeers().Count) > 0;
}
