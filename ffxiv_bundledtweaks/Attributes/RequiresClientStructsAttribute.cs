namespace ComplexTweaks.Attributes;

[AttributeUsage(AttributeTargets.Class)]
public class RequiresClientStructsAttribute(ushort minVersion, ushort maxVersion = ushort.MaxValue) : Attribute {
    public uint MinVersion { get; } = minVersion;
    public uint MaxVersion { get; } = maxVersion;
}
