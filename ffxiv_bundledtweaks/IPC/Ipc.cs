namespace ComplexTweaks.IPC;

[Flags]
public enum Ipc {
    None = 0,
    AutoRetainer = 1 << 0,
    BossMod = 1 << 1,
    Deliveroo = 1 << 2,
    Gearsetter = 1 << 3,
    Lifestream = 1 << 4,
    Navmesh = 1 << 5,
    Questionable = 1 << 6,
    Dropbox = 1 << 7,
    Visibility = 1 << 8,
    PandorasBox = 1 << 9,
    QoLBar = 1 << 10
}

