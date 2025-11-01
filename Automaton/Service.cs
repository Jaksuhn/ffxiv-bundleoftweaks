using ECommons.Automation.NeoTaskManager;
using System.Reflection;

namespace Automaton.Services;
public class Service
{
    public static Provider Provider { get; private set; } = null!;
    public static AutoRetainerApi AutoRetainerApi { get; private set; } = null!;
    public static AutoRetainerIPC AutoRetainerIPC { get; private set; } = null!;
    public static BossModIPC BossMod { get; private set; } = null!;
    public static GearsetterIPC Gearsetter { get; private set; } = null!;
    public static LifestreamIPC Lifestream { get; private set; } = null!;
    public static NavmeshIPC Navmesh { get; private set; } = null!;
    public static QuestionableIPC Questionable { get; private set; } = null!;

    public static AddonObserver AddonObserver { get; private set; } = null!;
    public static Automation Automation { get; private set; } = null!;
    public static IPCRegistry IPC { get; private set; } = null!;
    public static Memory Memory { get; private set; } = null!;
    public static TaskManager TaskManager { get; private set; } = null!;
}

public class IPCRegistry
{
    private readonly Dictionary<Ipc, BaseIPC>? _byId = [];

    public IPCRegistry()
    {
        foreach (var prop in typeof(Service).GetProperties().Where(prop => typeof(BaseIPC).IsAssignableFrom(prop.PropertyType)))
        {
            try
            {
                if (prop.GetValue(null) is BaseIPC ipc)
                    MapByEnum(ipc);
            }
            catch
            {
                Svc.Log.Warning($"[{nameof(IPCRegistry)}] Failed to register {prop.Name}");
            }
        }
    }

    private void MapByEnum(BaseIPC ipc)
    {
        if (_byId == null) return;
        if (ipc.GetType().GetCustomAttribute<IpcAttribute>(inherit: false) is { } attr)
            _byId[attr.Id] = ipc;
    }

    public BaseIPC? Get(Ipc id)
    {
        if (_byId == null)
            return null;
        return _byId.TryGetValue(id, out var ipc) ? ipc : null;
    }

    public BaseIPC[] GetMany(params Ipc[] ids)
    {
        if (_byId == null || ids.Length == 0)
            return [];
        return [.. ids.Select(Get).Where(ipc => ipc != null).Cast<BaseIPC>()];
    }

    public bool AreAllLoaded(params Ipc[] ids)
    {
        if (_byId == null || ids.Length == 0)
            return ids.Length == 0;

        if (ids.Any(id => !_byId.ContainsKey(id)))
            return false;

        var ipcs = GetMany(ids);
        return ipcs.Length == ids.Length && ipcs.All(ipc => ipc.IsLoaded);
    }

    public BaseIPC[] GetMissing(MethodInfo? method)
        => method == null ? [] : GetMissing([.. method.GetCustomAttributes<RequiresAttribute>().SelectMany(r => r.Id.ToArray()).Distinct().ToArray()]);

    public BaseIPC[] GetMissing(params Ipc[] ids)
    {
        if (_byId == null || ids.Length == 0)
            return [];

        var missing = new List<BaseIPC>();
        foreach (var id in ids)
        {
            if (!_byId.TryGetValue(id, out var ipc))
                continue;
            if (!ipc.IsLoaded)
                missing.Add(ipc);
        }

        return [.. missing];
    }
}
