namespace ComplexTweaks.IPC;

/// <summary>
/// Associate an IPC class with an Id
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class IpcAttribute(Ipc id) : Attribute
{
    public Ipc Id { get; } = id;
}

