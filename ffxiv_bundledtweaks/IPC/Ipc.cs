namespace ComplexTweaks.IPC;

[Flags]
public enum Ipc {
    None = 0,
    AutoRetainer = 1 << 0,
    BossMod = 1 << 1,
    Lifestream = 1 << 2,
    Navmesh = 1 << 3,
    Questionable = 1 << 4,
    TextAdvance = 1 << 5,
    GlamourLog = 1 << 6,
}
