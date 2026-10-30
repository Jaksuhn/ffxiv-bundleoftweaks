namespace ComplexTweaks.IPC;

public abstract class BaseIPC {
    public abstract Ipc Id { get; }
    public abstract string Name { get; }
    public abstract string Repo { get; }
    public bool IsLoaded => Svc.Interface.InstalledPlugins.Any(p => p.InternalName == Name && p.IsLoaded);

    protected static string Dynamis => "https://puni.sh/api/repository/";
    protected static string Punish => "https://love.puni.sh/ment.json";
    protected static string Nightmare => "https://github.com/NightmareXIV/MyDalamudPlugins/raw/main/pluginmaster.json";
    protected static string Kawaii => Dynamis + "kawaii";
    protected static string Veyn => Dynamis + "veyn";
    protected static string Vera => Dynamis + "vera";
    protected static string Croizat => Dynamis + "croizat";
}
