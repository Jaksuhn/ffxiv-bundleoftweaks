using ECommons.EzIpcManager;

namespace Automaton.IPC;

#nullable disable
#pragma warning disable CS8632
public class BossModIPC
{
    public const string Name = "BossMod";
    public const string Repo = "https://puni.sh/api/repository/veyn";
    public BossModIPC() => EzIPC.Init(this, Name);
    public static bool Installed => Utils.HasPlugin(Name);

    /// <remarks> string name </remarks>
    [EzIPC("Presets.%m", true)] public readonly Func<string, string?> Get;

    /// <remarks> string presetSerialized, bool overwrite </remarks>
    [EzIPC("Presets.%m", true)] public readonly Func<string, bool, bool> Create;

    /// <remarks> string name </remarks>
    [EzIPC("Presets.%m", true)] public readonly Func<string, bool> Delete;

    [EzIPC("Presets.%m", true)] public readonly Func<string> GetActive;

    /// <remarks> string name </remarks>
    [EzIPC("Presets.%m", true)] public readonly Func<string, bool> SetActive;
    [EzIPC("Presets.%m", true)] public readonly Func<bool> ClearActive;
    [EzIPC("Presets.%m", true)] public readonly Func<bool> GetForceDisabled;
    [EzIPC("Presets.%m", true)] public readonly Func<bool> SetForceDisabled;

    /// <remarks> string presetName, string moduleTypeName, string trackName, string value </remarks>
    [EzIPC("Presets.%m", true)] public readonly Func<string, string, string, string, bool> AddTransientStrategy;

    /// <remarks> string presetName, string moduleTypeName, string trackName, string value, int oid </remarks>
    [EzIPC("Presets.%m", true)] public readonly Func<string, string, string, string, int, bool> AddTransientStrategyTargetEnemyOID;

    /// <remarks> string presetName, string moduleTypeName, string trackName </remarks>
    [EzIPC("Presets.%m", true)] public readonly Func<string, string, string, bool> ClearTransientStrategy;

    /// <remarks> string presetName, string moduleTypeName </remarks>
    [EzIPC("Presets.%m", true)] public readonly Func<string, string, bool> ClearTransientModuleStrategies;

    /// <remarks> string presetName </remarks>
    [EzIPC("Presets.%m", true)] public readonly Func<string, bool> ClearTransientPresetStrategies;

    static class Modules
    {
        const string AutoFarm = "BossMod.Autorotation.MiscAI.AutoFarm";
    }
}
