using ECommons.EzIpcManager;

namespace ComplexTweaks.IPC;

#nullable disable
[Ipc(Ipc.Navmesh)]
public class NavmeshIPC : BaseIPC
{
    public override string Name => "vnavmesh";
    public override string Repo => Veyn;
    public NavmeshIPC() => EzIPC.Init(this, Name);

    [EzIPC("Nav.%m")] public readonly Func<bool> IsReady;
    [EzIPC("Nav.%m")] public readonly Func<float> BuildProgress;
    [EzIPC("Nav.%m")] public readonly Func<bool> Reload;
    [EzIPC("Nav.%m")] public readonly Func<bool> Rebuild;
    /// <summary> Vector3 from, Vector3 to, bool fly </summary>
    [EzIPC("Nav.%m")] public readonly Func<Vector3, Vector3, bool, Vector3> Pathfind;

    /// <summary> Vector3 dest, bool fly </summary>
    [EzIPC("SimpleMove.%m")] public readonly Func<Vector3, bool, bool> PathfindAndMoveTo;
    [EzIPC("SimpleMove.%m")] public readonly Func<bool> PathfindInProgress;

    [EzIPC("Path.%m")] public readonly Action Stop;
    [EzIPC("Path.%m")] public readonly Func<bool> IsRunning;
    [EzIPC("Path.%m")] public readonly Func<float> GetTolerance;

    /// <summary> Vector3 p, float halfExtentXZ, float halfExtentY </summary>
    [EzIPC("Query.Mesh.%m")] public readonly Func<Vector3, float, float, Vector3?> NearestPoint;
    /// <summary> Vector3 p, bool allowUnlandable, float halfExtentXZ (default 5) </summary>
    [EzIPC("Query.Mesh.%m")] public readonly Func<Vector3, bool, float, Vector3?> PointOnFloor;
}
