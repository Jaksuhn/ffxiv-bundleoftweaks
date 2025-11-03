using FFXIVClientStructs.FFXIV.Component.GUI;
using System.Runtime.InteropServices;
using ValueType = FFXIVClientStructs.FFXIV.Component.GUI.ValueType;

namespace ComplexTweaks.Utilities.Extensions;
public static class AtkValueExtensions
{
    public static unsafe string ValueString(this AtkValue v) => v.Type switch
    {
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
}
