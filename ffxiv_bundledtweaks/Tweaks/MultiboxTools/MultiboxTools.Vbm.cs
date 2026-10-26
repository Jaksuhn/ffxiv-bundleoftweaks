namespace ComplexTweaks.Tweaks;

public partial class MultiboxTools {
    private const string VbmDefaultPreset = "VBM Default";
    private const string VbmAiPreset = "VBM AI";

    private bool EveryoneOn(string preset) {
        var localKnown = BossModIPC.Get().IsLoaded;
        if (localKnown && !_drawSnap.PresetOn(preset))
            return false;
        return _drawPeers.Count == 0 ? localKnown : _drawPeers.All(p => p.PresetOn(preset));
    }

    private void SendVbm(int? targetId, string preset, bool enabled) {
        var hasPeers = HasPeers();
        if (targetId is null) {
            TogglePreset(preset, enabled, quiet: hasPeers);
            if (!hasPeers)
                return;
        }

        if (targetId != _sessionId)
            Log(targetId is null ? $"Broadcasting {(enabled ? "enable" : "disable")} {preset}" : $"Sending {(enabled ? "enable" : "disable")} {preset} to {targetId}");

        Dispatch(new VbmMessage(targetId, preset, enabled));
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
