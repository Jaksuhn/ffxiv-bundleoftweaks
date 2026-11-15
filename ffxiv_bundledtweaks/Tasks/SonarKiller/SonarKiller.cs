using ComplexTweaks.Tweaks;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;
using System.Threading.Tasks;

namespace ComplexTweaks.Tasks.SonarKiller;

[RunnableTask]
[Requires(Ipc.BossMod | Ipc.Navmesh)]
public sealed class SonarKiller : KillFlag {
    private readonly int maxLevel;

    public SonarKiller(string args) : base("") {
        if (!SonarReader.IsLoaded)
            throw new InvalidOperationException("Sonar is not loaded.");
        maxLevel = int.TryParse(args.Trim(), out var lvl) ? lvl : int.MaxValue;
    }

    protected override async Task Execute() {
        using var scope = BeginScope(nameof(SonarKiller));
        var attempted = new HashSet<string>();
        while (NextHunt(attempted) is { } hunt) {
            attempted.Add(hunt.Key);
            Status = $"#{attempted.Count}: {hunt.Name} (lv{hunt.Level})";
            Log($"Next hunt: {hunt.Name} [w{hunt.WorldId} t{hunt.TerritoryId}] {hunt.Position}");
            try {
                if (hunt.WorldId != IPlayerState.Get().CurrentWorld.RowId)
                    await TravelToWorld(hunt.WorldId);
                SetFlag(hunt);
                if (!await MoveToFlagAndKill()) {
                    Log($"{hunt.Name} not found at flag, removing from Sonar");
                    SonarReader.Remove(hunt.Key);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException) {
                Warning($"Skipping {hunt.Name}: {ex.Message}");
            }
        }
        Log($"No more hunts. Attempted {attempted.Count}.");
    }

    private SonarReader.HuntEntry? NextHunt(HashSet<string> attempted) {
        var world = IPlayerState.Get().CurrentWorld.RowId;
        var dataCenter = IPlayerState.Get().CurrentWorld.Value.DataCenter.RowId;
        var territory = IClientState.Get().TerritoryType;
        var pos = IObjectTable.Get().LocalPlayer?.Position ?? default;
        return SonarReader.GetHunts("A")
            .Where(h => h.Alive && h.Level <= maxLevel && !attempted.Contains(h.Key))
            .Where(h => World.GetRow(h.WorldId).DataCenter.RowId == dataCenter)
            .OrderBy(h => h.WorldId != world)
            .ThenBy(h => h.WorldId)
            .ThenBy(h => h.TerritoryId != territory)
            .ThenBy(h => h.TerritoryId)
            .ThenBy(h => Vector3.DistanceSquared(h.Position, pos))
            .Cast<SonarReader.HuntEntry?>()
            .FirstOrDefault();
    }

    private async Task TravelToWorld(uint worldId) {
        using var scope = BeginScope(nameof(TravelToWorld));
        if (IClientState.Get().TerritoryType is not (129 or 130 or 132))
            await TeleportTo(129, Coords.AetherytePosition(8));
        await WaitWhile(() => IObjectTable.Get().LocalPlayer.IsBusy, "WaitForTravelReady");
        Status = $"Travelling to {World.GetRow(worldId).Name}";
        unsafe {
            AgentWorldTravel.Instance()->Travel((ushort)worldId);
        }
        await WaitUntil(() => IPlayerState.Get().CurrentWorld.RowId == worldId && IObjectTable.Get().LocalPlayer.Interactable, "WaitForWorldTravel", timeout: TimeSpan.FromMinutes(5));
    }

    private static unsafe void SetFlag(SonarReader.HuntEntry hunt)
        => AgentMap.Instance()->SetFlagMapMarker(hunt.TerritoryId, TerritoryType.GetRow(hunt.TerritoryId).Map.RowId, hunt.Position);
}
