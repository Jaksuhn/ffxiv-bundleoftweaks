namespace Automaton.IPC;
public abstract class BaseIPC
{
    public abstract string Name { get; }
    public abstract string Repo { get; }
    public bool IsLoaded => Svc.PluginInterface.InstalledPlugins.Any(p => p.InternalName == Name && p.IsLoaded);

    public string Dynamis => "https://puni.sh/api/repository/";
    public string Punish => "https://love.puni.sh/ment.json";
    public string Main => string.Empty;
    public string Nightmare => "https://github.com/NightmareXIV/MyDalamudPlugins/raw/main/pluginmaster.json";
    public string Kawaii => Dynamis + "kawaii";
    public string Veyn => Dynamis + "veyn";
    public string Vera => Dynamis + "vera";
}
