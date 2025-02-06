namespace Automaton.IPC;
public abstract class BaseIPC
{
    public abstract string Name { get; }
    public abstract string Repo { get; }
    public bool IsLoaded => Svc.PluginInterface.InstalledPlugins.Any(p => p.InternalName == Name && p.IsLoaded);
}
