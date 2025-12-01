using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.STD;
using System.Runtime.InteropServices;
using ValueType = FFXIVClientStructs.FFXIV.Component.GUI.ValueType;

namespace ComplexTweaks.Utilities.Extensions;

public static unsafe class ClientStructsExtensions {
    public static unsafe string ValueString(this AtkValue v) => v.Type switch {
        ValueType.Int => $"{v.Int}",
        ValueType.String => Marshal.PtrToStringUTF8(new IntPtr(v.String)) ?? string.Empty,
        ValueType.UInt => $"{v.UInt}",
        ValueType.Bool => $"{v.Byte != 0}",
        ValueType.Float => $"{v.Float}",
        ValueType.Vector => "[Vector]",
        ValueType.ManagedString => Marshal.PtrToStringUTF8(new IntPtr(v.String))?.TrimEnd('\0') ?? string.Empty,
        ValueType.ManagedVector => "[Managed Vector]",
        _ => $"Unknown Type: {v.Type}"
    };

    public static void ResetFlags(this ContentsFinder cf) {
        cf.IsExplorerMode = false;
        cf.IsLevelSync = false;
        cf.IsLimitedLevelingRoulette = false;
        cf.IsMinimalIL = false;
        cf.IsSilenceEcho = false;
        cf.IsUnrestrictedParty = false;
        cf.LootRules = ContentsFinder.LootRule.Normal;
    }

    public static unsafe uint* ToPtr(this StdVector<ContentsId> contentsIds) {
        var ids = contentsIds.Select(x => x.Id).ToList();
        var array = stackalloc uint[ids.Count];
        for (var i = 0; i < ids.Count; i++)
            array[i] = ids[i];
        return array;
    }

    public static List<T> ToList<T>(this StdVector<T> stdVector) where T : unmanaged {
        var list = new List<T>();
        var size = stdVector.LongCount;

        unsafe {
            var current = stdVector.First;
            for (var i = 0; i < size; i++) {
                list.Add(current[i]);
            }
        }

        return list;
    }

    public static unsafe Vector3 ToVector3(this FlagMapMarker flag) => AgentMap.Instance()->FlagMarkerCount > 0 ? Service.Navmesh.PointOnFloor(new(flag.XFloat, 1024, flag.YFloat), false, 5) ?? Vector3.NaN : Vector3.NaN;
}
