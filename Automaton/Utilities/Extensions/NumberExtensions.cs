using System.Drawing;
using System.Globalization;

namespace Automaton.Utilities.Extensions;
public static class NumberExtensions
{
    #region uint
    public static uint Reverse(this uint value)
        => ((value & 0x000000FFu) << 24) | ((value & 0x0000FF00u) << 8) |
            ((value & 0x00FF0000u) >> 8) | ((value & 0xFF000000u) >> 24);

    public static uint ToHex(this uint i) => uint.Parse(i.ToString("X"), NumberStyles.HexNumber);
    #endregion

    #region int
    public static int Ms(this int i) => i * 1000;
    public static Vector2 Vec2(this int i) => new(i);
    #endregion

    #region vectors
    public static Vector2 ToVec2(this Point p) => new(p.X, p.Y);
    public static Point ToPoint(this Vector2 v) => new((int)Math.Round(v.X), (int)Math.Round(v.Y));
    public static Vector3 ToVector3(this (float X, float Y, float Z) t) => new(t.X, t.Y, t.Z);
    #endregion
}
