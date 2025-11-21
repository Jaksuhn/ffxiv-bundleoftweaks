namespace ComplexTweaks.TweakSystem.Attributes;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequiresAttribute(Ipc id) : Attribute
{
    public Ipc Id { get; } = id;
}
