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
    public bool AllowNetwork;
}

public partial class MultiboxTools : Tweak<MultiboxToolsConfig> {
    public override string Name => "Multibox Tools";
    public override string Description => $"Connect to other local {Svc.Interface.Manifest.Name} instances and manage them.";

    private readonly int _sessionId = Random.Shared.Next(1, int.MaxValue);
    private readonly Lock _lock = new();
    private readonly List<HubClient> _clients = [];
    private List<ClientSnapshot> _clientPeers = [];

    private readonly Lock _statusLock = new();
    private readonly Lock _dutyLock = new();
    private ClientSnapshot _localSnapshot = ClientSnapshot.Empty;
    private long _nextStatusTick;
    private volatile bool _forceStatus;

    private CancellationTokenSource? _cts;
    private Task? _networkTask;
    private CancellationTokenSource? _lanCts;
    private Task? _lanTask;
    private MessageWriter? _hubWriter;
    private bool _hubStreamIsTcp;
    private const int CommandHistoryLimit = 3;
    private readonly List<string> _commandHistory = [];
    private string _commandInput = string.Empty;
    private volatile bool _hubMissing;
    private long _hubSearchStart;
    private ClientSnapshot _drawSnap = ClientSnapshot.Empty;
    private List<ClientSnapshot> _drawPeers = [];
    private DutyCommands _dutyDraft = new();
    private DutyCommands? _dutyOverride;
    private uint _dutyTerritory;

    public override void OnEnable() {
        lock (_statusLock)
            _localSnapshot = ClientSnapshot.Empty with { SessionId = _sessionId };
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

    [CommandHandler("/broadcast", "Send a command to all other connected MultiboxTools clients")]
    private void OnBroadcast(string command, string arguments) {
        if (arguments.IsEmpty) {
            ModuleMessage("Usage: /broadcast <command>");
            return;
        }

        SendCommand(null, arguments, remember: true);
    }

    public override void DrawConfig() {
        _drawSnap = LocalSnapshot();
        _drawPeers = GetPeers();
        var hasPeers = _drawPeers.Count > 0;

        ImGui.DrawSection("Connection");

        var actAsHub = Config.ActAsHub;
        if (ImGui.Checkbox("Act as Hub", ref actAsHub)) {
            Config.ActAsHub = actAsHub;
            StartNetwork();
        }
        ImGuiComponents.HelpMarker("Enable on exactly one instance. Other instances on this PC connect on their own.");

        var allowNetwork = Config.AllowNetwork;
        if (ImGui.Checkbox("Allow Network", ref allowNetwork)) {
            Config.AllowNetwork = allowNetwork;
            SetLanEnabled(allowNetwork);
        }
        ImGuiComponents.HelpMarker("Opens a LAN connection and allows other CBT instances on other PCs in the local network to connect.");

        ImGui.DrawSection("Connected Clients");

        var iconBtnSize = GetIconButtonSize();
        var spacing = ImGui.GetStyle().ItemSpacing.X;

        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - iconBtnSize.X * 2 - spacing * 2);
        if (ImGui.InputTextWithHint("##MultiboxCommand", "/li inn", ref _commandInput, 512, ImGuiInputTextFlags.EnterReturnsTrue))
            SendCommand(null, _commandInput, remember: true);

        ImGui.SameLine();
        if (ImGui.IconButton(FontAwesomeIcon.Share, "broadcast", "Broadcast command to all\nShift-click to include this client", disabled: !hasPeers))
            SendCommand(null, _commandInput, remember: true, includeSelf: ImGui.GetIO().KeyShift);

        ImGui.SameLine();
        DrawCommandHistory();

        DrawCommands(new CmdCtx(this, null, Self: false, hasPeers), beforeSend: null);

        ImGui.Spacing();

        DrawPeers(hasPeers);

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

    private bool HasPeers() {
        lock (_lock)
            return Config.ActAsHub ? _clients.Any(c => c.SessionId != 0) : _clientPeers.Count > 0;
    }
}
