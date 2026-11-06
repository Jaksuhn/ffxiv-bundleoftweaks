using FFXIVClientStructs.FFXIV.Client.Game.Control;

namespace ComplexTweaks.Tweaks;

public partial class MultiboxTools {
    private void SendNav(int? targetId, bool fly) {
        if (IObjectTable.Get().LocalPlayer is not { Position: var pos }) {
            ModuleMessage("Could not read the hub position.");
            return;
        }

        if (targetId != _sessionId && !HasPeers()) {
            ModuleMessage("No other clients connected.");
            return;
        }

        Log(targetId is null ? $"Broadcasting navmesh to {pos} (fly: {fly})" : $"Sending navmesh to {pos} (fly: {fly}) to {targetId}");
        Dispatch(new NavMessage(targetId, IClientState.Get().TerritoryType, pos.X, pos.Y, pos.Z, fly));
    }

    private void RunNav(NavMessage msg) {
        _ = IFramework.Get().Run(() => {
            try {
                var nav = NavmeshIPC.Get();
                if (!nav.IsLoaded) {
                    ModuleMessage("vnavmesh is not loaded.");
                    return;
                }

                if (nav.IsRunning())
                    nav.Stop();

                msg.GoToAsync();
            }
            catch (Exception ex) {
                Error(ex, "Failed to navmesh to hub");
                ModuleMessage("Failed to navmesh to hub");
            }
        });
    }
}
