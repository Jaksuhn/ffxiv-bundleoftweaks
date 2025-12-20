using Dalamud.Game.ClientState.Fates;

namespace ComplexTweaks.Utilities.Extensions;

public static unsafe class DalamudExtensions {
    public static string Stringify(this IFate fate) => $"[{fate.FateId}] {fate.Position} {fate.Progress}%% {TimeSpan.FromSeconds(fate.TimeRemaining):mm\\:ss} / {TimeSpan.FromSeconds(fate.Duration):mm\\:ss} [{fate.GameData.Value.Rule}]";
}
