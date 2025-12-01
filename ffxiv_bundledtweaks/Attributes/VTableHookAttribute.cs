namespace ComplexTweaks.Attributes;

[AttributeUsage(AttributeTargets.Method)]
public class VTableHookAttribute<T>(int VTableIndex) : Attribute {
    public int VTableIndex { get; } = VTableIndex;
}
