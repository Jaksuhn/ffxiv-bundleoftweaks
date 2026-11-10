namespace ComplexTweaks.Attributes;

/// <summary>Hooks the virtual function with the same name as the hook method.</summary>
[AttributeUsage(AttributeTargets.Method)]
public class VTableHookAttribute<T> : Attribute {
    /// <summary>From <typeparamref name="T"/>'s static vtable.</summary>
    public VTableHookAttribute(int vtableIndex) => VTableIndex = vtableIndex;

    /// <summary>
    /// From the vtable of an interface <typeparamref name="T"/> inherits, read from <c>T.Instance()</c>.
    /// </summary>
    public VTableHookAttribute(string inheritedMember) => InheritedMember = inheritedMember;

    public int VTableIndex { get; }
    public string? InheritedMember { get; }
}
