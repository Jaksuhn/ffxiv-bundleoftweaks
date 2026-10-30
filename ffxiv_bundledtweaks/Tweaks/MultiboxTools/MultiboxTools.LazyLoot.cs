namespace ComplexTweaks.Tweaks;

public partial class MultiboxTools {
    private enum LootMode {
        Unknown,
        Off,
        Need,
        Greed,
        Pass,
    }

    // this is such a hack. I should probably PR some ipc to it
    private static LootMode ReadLootMode() {
        if (IDtrBar.Get().Entries.FirstOrDefault(e => e.Title == "LazyLoot") is not { } entry)
            return LootMode.Unknown;

        return entry.Text?.TextValue.Trim() switch {
            "Needing" => LootMode.Need,
            "Greeding" => LootMode.Greed,
            "Passing" => LootMode.Pass,
            "FULF Disabled" => LootMode.Off,
            _ => LootMode.Unknown,
        };
    }

    private bool EveryoneLoot(LootMode mode) {
        var localKnown = _drawSnap.Loot != LootMode.Unknown;
        if (localKnown && _drawSnap.Loot != mode)
            return false;
        return _drawPeers.Count == 0 ? localKnown : _drawPeers.All(p => p.Loot == mode);
    }

    private void SendLoot(int? targetId, LootMode mode) {
        string[] commands = mode == LootMode.Off ? ["/fulf off"] : ["/fulf on", $"/fulf {mode.ToString().ToLowerInvariant()}"];
        var localOnly = targetId is null && !HasPeers();
        foreach (var cmd in commands) {
            if (localOnly)
                ExecuteRemoteCommand(cmd);
            else
                SendCommand(targetId, cmd, includeSelf: true);
        }
        _nextStatusTick = 0;
    }
}
