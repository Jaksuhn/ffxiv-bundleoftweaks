namespace ComplexTweaks.Attributes;

[AttributeUsage(AttributeTargets.Assembly)]
public sealed class TweakPackAttribute(int api) : Attribute {
    public int Api { get; } = api;
}

public static class TweakPackApi {
    public const int Current = 1;
}
