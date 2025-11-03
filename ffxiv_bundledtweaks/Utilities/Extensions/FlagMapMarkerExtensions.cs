using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace ComplexTweaks.Utilities.Extensions;
public static class FlagMapMarkerExtensions
{
    public static unsafe Vector3 ToVector3(this FlagMapMarker flag) => AgentMap.Instance()->FlagMarkerCount > 0 ? Service.Navmesh.PointOnFloor(new(flag.XFloat, 1024, flag.YFloat), false, 5) ?? Vector3.NaN : Vector3.NaN;
}
