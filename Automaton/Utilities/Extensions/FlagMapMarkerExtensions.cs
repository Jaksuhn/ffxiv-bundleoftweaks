using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Automaton.Utilities.Extensions;
public static class FlagMapMarkerExtensions
{
    public static unsafe Vector3 ToVector3(this FlagMapMarker flag) => AgentMap.Instance()->IsFlagMarkerSet ? Service.Navmesh.PointOnFloor(new(flag.XFloat, 1024, flag.YFloat), false, 5) ?? Vector3.NaN : Vector3.NaN;
}
