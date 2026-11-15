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
            Log($"Next hunt: {hunt.Name} [{hunt.TerritoryId}] {hunt.Position}");
            SetFlag(hunt);
            try {
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
        var territory = IClientState.Get().TerritoryType;
        var pos = IObjectTable.Get().LocalPlayer?.Position ?? default;
        return SonarReader.GetHunts("A")
            .Where(h => h.Alive && h.Level <= maxLevel && h.WorldId == world && !attempted.Contains(h.Key))
            .OrderBy(h => h.TerritoryId != territory)
            .ThenBy(h => h.TerritoryId)
            .ThenBy(h => Vector3.DistanceSquared(h.Position, pos))
            .Cast<SonarReader.HuntEntry?>()
            .FirstOrDefault();
    }

    private static unsafe void SetFlag(SonarReader.HuntEntry hunt)
        => AgentMap.Instance()->SetFlagMapMarker(hunt.TerritoryId, TerritoryType.GetRow(hunt.TerritoryId).Map.RowId, hunt.Position);
}
