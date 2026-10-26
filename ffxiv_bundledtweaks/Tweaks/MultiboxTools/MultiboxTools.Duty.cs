using clib.Ui;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.DutyState;
using FFXIVClientStructs.FFXIV.Client.Game.Event;

namespace ComplexTweaks.Tweaks;

public partial class MultiboxTools {
    private sealed class DutyCommands {
        public string StartCommand = string.Empty;
        public string EndCommand = string.Empty;
        public bool AutoLeaveOnEnd;
        public int TimeToWait;
    }

    private void DrawDutySection() {
        ImGui.DrawSection("Duty Start/End");

        DutyCommands draft;
        bool active;
        lock (_dutyLock) {
            draft = _dutyDraft;
            active = _dutyOverride != null;
        }

        DrawDutyEditor(draft);

        if (ImGui.Button("Push"))
            PushDuty();
        ImGui.SameLine();
        if (ImGui.Button("Clear"))
            ClearDuty();
        if (active) {
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(Color.Green, "Active");
        }
    }

    private void PushDuty() {
        DutyCommands snapshot;
        lock (_dutyLock)
            snapshot = CloneDuty(_dutyDraft);

        ApplyDuty(snapshot, updateDraft: false);
        if (!HasPeers()) {
            ModuleMessage("Duty settings applied locally.");
            return;
        }

        Log("Pushing duty commands");
        ModuleMessage("Pushed duty settings.");
        Dispatch(new DutyMessage(null, DutyWire.From(snapshot)));
    }

    private void ClearDuty() {
        ApplyDuty(null, updateDraft: false);
        ModuleMessage("Cleared duty settings.");
        if (HasPeers())
            Dispatch(new DutyMessage(null, null));
    }

    private void ClearDutyFor(ClientSnapshot peer) {
        var self = peer.SessionId == _sessionId;
        if (!self && !HasPeers()) {
            ModuleMessage("No other clients connected.");
            return;
        }

        Dispatch(new DutyMessage(peer.SessionId, null));
        ModuleMessage(self ? "Cleared duty settings." : $"Cleared duty settings for {peer.DisplayName}.");
    }

    private void ApplyDuty(DutyCommands? config, bool updateDraft) {
        lock (_dutyLock) {
            if (config == null) {
                _dutyOverride = null;
            }
            else {
                _dutyOverride = CloneDuty(config);
                if (updateDraft)
                    _dutyDraft = CloneDuty(config);
            }
        }

        lock (_statusLock)
            _localSnapshot = _localSnapshot with { Duty = config != null };
        _nextStatusTick = 0;

        if (config == null)
            _ = IFramework.Get().Run(() => {
                if (Automation.Running)
                    Automation.Stop();
            });
    }

    private DutyCommands? CurrentDutyOverride() {
        lock (_dutyLock)
            return _dutyOverride;
    }

    private void OnDutyStart(IDutyStateEventArgs _) {
        if (CurrentDutyOverride() is { } cfg)
            RunDutyCommand(cfg.StartCommand);
    }

    private void OnDutyComplete(IDutyStateEventArgs _) {
        if (CurrentDutyOverride() is not { } cfg)
            return;

        _dutyTerritory = IPlayerState.Get().Territory.RowId;
        RunDutyCommand(cfg.EndCommand);

        if (!cfg.AutoLeaveOnEnd)
            return;

        var wait = cfg.TimeToWait;
        Automation.Start(AutoTask.From(async t => {
            await t.DelayMs(wait * 1000);
            EventFramework.LeaveCurrentContent(true);
        }, name: "MultiboxAutoLeaveDuty"));
    }

    private void OnDutyTerritoryChanged(uint id) {
        if (CurrentDutyOverride() != null && id != _dutyTerritory && Automation.Running)
            Automation.Stop();
    }

    private void RunDutyCommand(string command) {
        if (string.IsNullOrWhiteSpace(command))
            return;

        var cmd = command.Trim();
        if (!cmd.StartsWith('/'))
            cmd = "/" + cmd;

        try {
            IChatGui.Get().ExecuteCommand(cmd);
        }
        catch (Exception ex) {
            Error(ex, $"Failed to run duty command: {cmd}");
            ModuleMessage($"Failed to run duty command: {cmd}");
        }
    }

    private static DutyCommands CloneDuty(DutyCommands config) => new() {
        StartCommand = config.StartCommand,
        EndCommand = config.EndCommand,
        AutoLeaveOnEnd = config.AutoLeaveOnEnd,
        TimeToWait = config.TimeToWait,
    };

    private static void DrawDutyEditor(DutyCommands config) {
        ImGui.InputTextWithHint("##DutyStart", "/command", ref config.StartCommand, 512);
        ImGui.TextColoredWrapped(Colors.Grey, "Command to run when the duty starts.");

        ImGui.InputTextWithHint("##DutyEnd", "/command", ref config.EndCommand, 512);
        ImGui.TextColoredWrapped(Colors.Grey, "Command to run when the duty ends.");

        ImGui.Checkbox("Auto Leave##DutyEnd", ref config.AutoLeaveOnEnd);
        if (config.AutoLeaveOnEnd)
            ImGui.SliderInt("Leave after (s)##Duty", ref config.TimeToWait, 0, 100);
    }
}
