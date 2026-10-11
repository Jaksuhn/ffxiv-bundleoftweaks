using System.Reflection;
using System.Runtime.Loader;

namespace ComplexTweaks.TweakSystem.Packs;

public sealed class TweakPackLoadContext : AssemblyLoadContext {
    public TweakPackLoadContext() : base(isCollectible: true) {
        Resolving += (_, name) => Resolve(name);
    }

    protected override Assembly? Load(AssemblyName assemblyName) => Resolve(assemblyName);

    private static Assembly? Resolve(AssemblyName assemblyName) {
        var name = assemblyName.Name;
        if (name is null)
            return null;

        var host = GetLoadContext(typeof(Tweak).Assembly);
        if (Find(host, name) is { } hostAssembly)
            return hostAssembly;

        Assembly? fallback = null;
        foreach (var context in All) {
            var found = Find(context, name);
            if (found is null)
                continue;
            if (found.GetName().Version == assemblyName.Version)
                return found;
            fallback ??= found;
        }

        return fallback;
    }

    private static Assembly? Find(AssemblyLoadContext? context, string name)
        => context?.Assemblies.FirstOrDefault(assembly => string.Equals(assembly.GetName().Name, name, StringComparison.Ordinal));
}
