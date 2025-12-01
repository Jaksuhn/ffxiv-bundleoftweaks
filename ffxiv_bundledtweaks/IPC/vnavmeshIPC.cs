using ECommons.EzIpcManager;
using System.Threading;

namespace ComplexTweaks.IPC;

#nullable disable
[Ipc(Ipc.Navmesh)]
public class NavmeshIPC : BaseIPC {
    public override string Name => "vnavmesh";
    public override string Repo => Veyn;
    public NavmeshIPC() => EzIPC.Init(this, Name);

    [EzIPC("Nav.%m")] public readonly Func<bool> IsReady;
    [EzIPC("Nav.%m")] public readonly Func<float> BuildProgress;
    [EzIPC("Nav.%m")] public readonly Func<bool> Reload;
    [EzIPC("Nav.%m")] public readonly Func<bool> Rebuild;
    /// <summary> Vector3 from, Vector3 to, bool fly </summary>
    [EzIPC("Nav.%m")] public readonly Func<Vector3, Vector3, bool, List<Vector3>> Pathfind;
    /// <summary> Vector3 from, Vector3 to, bool fly, float range </summary>
    [EzIPC("Nav.%m")] public readonly Func<Vector3, Vector3, bool, float, List<Vector3>> PathfindWithTolerance;
    /// <summary> Vector3 from, Vector3 to, bool fly, CancellationToken cancel </summary>
    [EzIPC("Nav.%m")] public readonly Func<Vector3, Vector3, bool, CancellationToken, List<Vector3>> PathfindCancelable;
    [EzIPC("Nav.%m")] public readonly Action PathfindCancelAll;
    [EzIPC("Nav.%m")] public readonly Func<bool> NavPathfindInProgress;
    [EzIPC("Nav.%m")] public readonly Func<int> PathfindNumQueued;
    [EzIPC("Nav.%m")] public readonly Func<bool> IsAutoLoad;
    [EzIPC("Nav.%m")] public readonly Action<bool> SetAutoLoad;
    /// <summary> Vector3 startingPos, string filename, float pixelSize </summary>
    [EzIPC("Nav.%m")] public readonly Func<Vector3, string, float, bool> BuildBitmap;
    /// <summary> Vector3 startingPos, string filename, float pixelSize, Vector3 minBounds, Vector3 maxBounds </summary>
    [EzIPC("Nav.%m")] public readonly Func<Vector3, string, float, Vector3, Vector3, bool> BuildBitmapBounded;

    /// <summary> Vector3 p, float halfExtentXZ, float halfExtentY </summary>
    [EzIPC("Query.Mesh.%m")] public readonly Func<Vector3, float, float, Vector3?> NearestPoint;
    /// <summary> Vector3 p, bool allowUnlandable, float halfExtentXZ (default 5) </summary>
    [EzIPC("Query.Mesh.%m")] public readonly Func<Vector3, bool, float, Vector3?> PointOnFloor;

    /// <summary> List<Vector3> waypoints, bool fly </summary>
    [EzIPC("Path.%m")] public readonly Action<List<Vector3>, bool> MoveTo;
    [EzIPC("Path.%m")] public readonly Action Stop;
    [EzIPC("Path.%m")] public readonly Func<bool> IsRunning;
    [EzIPC("Path.%m")] public readonly Func<int> NumWaypoints;
    [EzIPC("Path.%m")] public readonly Func<List<Vector3>> ListWaypoints;
    [EzIPC("Path.%m")] public readonly Func<bool> GetMovementAllowed;
    [EzIPC("Path.%m")] public readonly Action<bool> SetMovementAllowed;
    [EzIPC("Path.%m")] public readonly Func<bool> GetAlignCamera;
    [EzIPC("Path.%m")] public readonly Action<bool> SetAlignCamera;
    [EzIPC("Path.%m")] public readonly Func<float> GetTolerance;
    [EzIPC("Path.%m")] public readonly Action<float> SetTolerance;

    /// <summary> Vector3 dest, bool fly </summary>
    [EzIPC("SimpleMove.%m")] public readonly Func<Vector3, bool, bool> PathfindAndMoveTo;
    /// <summary> Vector3 dest, bool fly, float range </summary>
    [EzIPC("SimpleMove.%m")] public readonly Func<Vector3, bool, float, bool> PathfindAndMoveCloseTo;
    [EzIPC("SimpleMove.%m")] public readonly Func<bool> PathfindInProgress;

    [EzIPC("Window.%m")] public readonly Func<bool> IsOpen;
    [EzIPC("Window.%m")] public readonly Action<bool> SetOpen;

    [EzIPC("DTR.%m")] public readonly Func<bool> IsShown;
    [EzIPC("DTR.%m")] public readonly Action<bool> SetShown;
}
