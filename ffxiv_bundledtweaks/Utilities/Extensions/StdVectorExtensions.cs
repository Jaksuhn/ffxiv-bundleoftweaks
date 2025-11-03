using FFXIVClientStructs.STD;

namespace ComplexTweaks.Utilities.Extensions;
public static class StdVectorExtensions
{
    public static List<T> ToList<T>(this StdVector<T> stdVector) where T : unmanaged
    {
        var list = new List<T>();
        var size = stdVector.LongCount;

        unsafe
        {
            var current = stdVector.First;
            for (var i = 0; i < size; i++)
            {
                list.Add(current[i]);
            }
        }

        return list;
    }
}
