using Dalamud.Plugin.Ipc;
using System.Threading.Tasks;

namespace ComplexTweaks.IPC;

public sealed class BossModIPC : BaseIPC, IPluginService {
    public override Ipc Id => Ipc.BossMod;
    public override string Name => "BossMod";
    public override string Repo => Veyn;

    private readonly ICallGateSubscriber<string, string?> _get;
    private readonly ICallGateSubscriber<string, bool, bool> _create;
    private readonly ICallGateSubscriber<string> _getActive;
    private readonly ICallGateSubscriber<string, bool> _setActive;
    private readonly ICallGateSubscriber<bool> _clearActive;
    private readonly ICallGateSubscriber<List<string>> _getActiveList;
    private readonly ICallGateSubscriber<string, bool> _activate;
    private readonly ICallGateSubscriber<string, bool> _deactivate;
    private readonly ICallGateSubscriber<List<string>, bool> _setActiveList;
    private readonly ICallGateSubscriber<string, string, string, string, bool> _addTransientStrategy;
    private readonly ICallGateSubscriber<Vector3, float, bool, bool> _generate;
    private readonly ICallGateSubscriber<TaskStatus> _getGenerationStatus;
    private readonly ICallGateSubscriber<bool> _hasTempMap;
    private readonly ICallGateSubscriber<bool> _clearTempMap;
    private readonly ICallGateSubscriber<BitmapQuality?> _evaluateTempMapQuality;

    public BossModIPC() {
        _get = Svc.Interface.GetIpcSubscriber<string, string?>("BossMod.Presets.Get");
        _create = Svc.Interface.GetIpcSubscriber<string, bool, bool>("BossMod.Presets.Create");
        _getActive = Svc.Interface.GetIpcSubscriber<string>("BossMod.Presets.GetActive");
        _setActive = Svc.Interface.GetIpcSubscriber<string, bool>("BossMod.Presets.SetActive");
        _clearActive = Svc.Interface.GetIpcSubscriber<bool>("BossMod.Presets.ClearActive");
        _getActiveList = Svc.Interface.GetIpcSubscriber<List<string>>("BossMod.Presets.GetActiveList");
        _activate = Svc.Interface.GetIpcSubscriber<string, bool>("BossMod.Presets.Activate");
        _deactivate = Svc.Interface.GetIpcSubscriber<string, bool>("BossMod.Presets.Deactivate");
        _setActiveList = Svc.Interface.GetIpcSubscriber<List<string>, bool>("BossMod.Presets.SetActiveList");
        _addTransientStrategy = Svc.Interface.GetIpcSubscriber<string, string, string, string, bool>("BossMod.Presets.AddTransientStrategy");
        _generate = Svc.Interface.GetIpcSubscriber<Vector3, float, bool, bool>("BossMod.ObstacleMap.Generate");
        _getGenerationStatus = Svc.Interface.GetIpcSubscriber<TaskStatus>("BossMod.ObstacleMap.GetGenerationStatus");
        _hasTempMap = Svc.Interface.GetIpcSubscriber<bool>("BossMod.ObstacleMap.HasTempMap");
        _clearTempMap = Svc.Interface.GetIpcSubscriber<bool>("BossMod.ObstacleMap.ClearTempMap");
        _evaluateTempMapQuality = Svc.Interface.GetIpcSubscriber<BitmapQuality?>("BossMod.ObstacleMap.EvaluateTempMapQuality");
    }

    public string? Get(string name) => _get.HasFunction ? _get.InvokeFunc(name) : null;
    public bool Create(string presetSerialized, bool overwrite) => _create.HasFunction && _create.InvokeFunc(presetSerialized, overwrite);
    public string GetActive() => _getActive.HasFunction ? _getActive.InvokeFunc() : string.Empty;
    public bool SetActive(string name) => _setActive.HasFunction && _setActive.InvokeFunc(name);
    public bool ClearActive() => _clearActive.HasFunction && _clearActive.InvokeFunc();
    public List<string> GetActiveList() => _getActiveList.HasFunction ? _getActiveList.InvokeFunc() ?? [] : [];
    public bool Activate(string name) => _activate.HasFunction && _activate.InvokeFunc(name);
    public bool Deactivate(string name) => _deactivate.HasFunction && _deactivate.InvokeFunc(name);
    public bool SetActiveList(List<string> names) => _setActiveList.HasFunction && _setActiveList.InvokeFunc(names);
    public bool AddTransientStrategy(string presetName, string moduleTypeName, string trackName, string value)
        => _addTransientStrategy.HasFunction && _addTransientStrategy.InvokeFunc(presetName, moduleTypeName, trackName, value);

    public bool Generate(Vector3 centerWorld, float radius, bool writeToFile)
        => _generate.HasFunction && _generate.InvokeFunc(centerWorld, radius, writeToFile);
    public TaskStatus GetGenerationStatus() => _getGenerationStatus.HasFunction ? _getGenerationStatus.InvokeFunc() : TaskStatus.Canceled;
    public bool HasTempMap() => _hasTempMap.HasFunction && _hasTempMap.InvokeFunc();
    public bool ClearTempMap() => _clearTempMap.HasFunction && _clearTempMap.InvokeFunc();
    public BitmapQuality? EvaluateTempMapQuality() => _evaluateTempMapQuality.HasFunction ? _evaluateTempMapQuality.InvokeFunc() : null;

    public readonly record struct BitmapQuality(
        float BlockedFraction, // amount of cells blocked (higher = less navigable)
        float LargestPassableComponentFraction, // amount of valid cells clustered in one area (higher = more navigable)
        float TinyPassableComponentFraction, // amount of valid cells in tiny clusters (higher = more fragmented)
        float SpeckleFraction, // amount of isolated cells with no neighbors of the same type (higher = noiser)
        int PassableComponents // count of passable regions (higher = more fragmented)
    ) {
        public bool BlockedIdeal => BlockedFraction < 0.85f;
        public bool LargestCompIdeal => LargestPassableComponentFraction < 0.5f;
        public bool TinyCompIdeal => TinyPassableComponentFraction < 0.03f;
        public bool SpeckleIdeal => SpeckleFraction < 0.003f;
        public bool IsBad => !BlockedIdeal || !LargestCompIdeal || !TinyCompIdeal || !SpeckleIdeal;
        public override string ToString() => $"Blocked: {BlockedFraction:P1}/{BlockedIdeal}, LargestComp: {LargestPassableComponentFraction:P1}/{LargestCompIdeal}, TinyComp: {TinyPassableComponentFraction:P1}/{TinyCompIdeal}, Speckle: {SpeckleFraction:P1}/{SpeckleIdeal}, PassableComps: {PassableComponents}";
    }

    public class Modules {
        public const string AutoFarm = "BossMod.Autorotation.MiscAI.AutoFarm";
    }
}
