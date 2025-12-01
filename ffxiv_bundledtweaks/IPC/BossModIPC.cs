using ECommons.EzIpcManager;

namespace ComplexTweaks.IPC;

#nullable disable
#pragma warning disable CS8632
[Ipc(Ipc.BossMod)]
public class BossModIPC : BaseIPC {
    public override string Name => "BossMod";
    public override string Repo => Veyn;
    public BossModIPC() => EzIPC.Init(this, Name);

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
    [EzIPC("Presets.%m", true)] public readonly Func<bool> Activate;
    [EzIPC("Presets.%m", true)] public readonly Func<bool> Deactivate;
    [EzIPC("Presets.%m", true)] public readonly Func<List<string>> GetActiveList;
    [EzIPC("Presets.%m", true)] public readonly Func<List<string>, bool> SetActiveList;

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

    public class Modules {
        public const string AutoFarm = "BossMod.Autorotation.MiscAI.AutoFarm";
    }
}
