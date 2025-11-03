namespace ComplexTweaks.Utilities.Extensions;
public static class EnumExtensions
{
    public static Ipc[] ToArray(this Ipc flags)
        => flags == Ipc.None ? [] : [.. Enum.GetValues<Ipc>().Where(flag => flag != Ipc.None && flags.HasFlag(flag))];
}
