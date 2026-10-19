namespace ComplexTweaks.Tweaks;

public partial class MultiboxTools {
    private const string VbmDefaultPreset = "VBM Default";
    private const string VbmAiPreset = "VBM AI";

    private bool EveryoneOn(string preset) {
        var peers = GetPeers();
        var localKnown = BossModIPC.Get().IsLoaded;
        if (localKnown && !_drawSnap.PresetOn(preset))
            return false;
        return peers.Count == 0 ? localKnown && _drawSnap.PresetOn(preset) : peers.All(p => p.PresetOn(preset));
    }

    private void SendVbm(int? targetPid, string preset, bool enabled) {
        var quiet = targetPid is null && HasPeers();
        if (targetPid is null || targetPid == _pid)
            TogglePreset(preset, enabled, quiet);

        if (targetPid == _pid || !HasPeers())
            return;

        if (targetPid is null)
            Log($"Broadcasting {(enabled ? "enable" : "disable")} {preset}");
        else
            Log($"Sending {(enabled ? "enable" : "disable")} {preset} to {targetPid}");

        if (Config.ActAsHub)
            HubRouteVbm(_pid, targetPid, preset, enabled);
        else
            WriteHub(new VbmMessage(targetPid, preset, enabled));
    }

    private void HubRouteVbm(int fromPid, int? targetPid, string preset, bool enabled) {
        if (targetPid is null) {
            if (fromPid != _pid)
                TogglePreset(preset, enabled);
            foreach (var client in SnapshotClients().Where(c => c.Pid != fromPid && c.Pid != 0))
                client.Send(new RunVbmMessage(preset, enabled));
            return;
        }

        if (targetPid == _pid) {
            TogglePreset(preset, enabled);
            return;
        }

        SnapshotClients().FirstOrDefault(c => c.Pid == targetPid)?.Send(new RunVbmMessage(preset, enabled));
    }

    private void TogglePreset(string preset, bool enabled, bool quiet = false) {
        _ = IFramework.Get().Run(() => {
            try {
                var ipc = BossModIPC.Get();
                if (!ipc.IsLoaded) {
                    if (!quiet)
                        ModuleMessage("BossMod is not loaded.");
                    return;
                }

                if (ipc.GetActiveList().Contains(preset) == enabled) {
                    _nextStatusTick = 0;
                    return;
                }

                var ok = enabled ? ipc.Activate(preset) : ipc.Deactivate(preset);
                if (!ok && !quiet)
                    ModuleMessage($"Could not {(enabled ? "enable" : "disable")} {preset}.");
                _nextStatusTick = 0;
            }
            catch (Exception ex) {
                Error(ex, $"Failed to set {preset}");
                if (!quiet)
                    ModuleMessage($"Failed to set {preset}");
            }
        });
    }

    private static (bool Default, bool Ai) GetPresetStates() {
        var ipc = BossModIPC.Get();
        if (!ipc.IsLoaded)
            return (false, false);

        var active = ipc.GetActiveList();
        return (active.Contains(VbmDefaultPreset), active.Contains(VbmAiPreset));
    }
}
