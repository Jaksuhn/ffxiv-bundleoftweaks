using System.Reflection;
using System.Runtime.Loader;

namespace ComplexTweaks.TweakSystem.Packs;

public sealed class TweakPackLoadContext : AssemblyLoadContext {
    public TweakPackLoadContext() : base(isCollectible: true) {
        Resolving += (_, name) => Resolve(name);
    }

    protected override Assembly? Load(AssemblyName assemblyName) => Resolve(assemblyName);

    private static Assembly? Resolve(AssemblyName assemblyName) {
        if (assemblyName.Name is null)
            return null;

        var host = GetLoadContext(typeof(Tweak).Assembly);
        return Find(host, assemblyName.Name) ?? Find(Default, assemblyName.Name);
    }

    private static Assembly? Find(AssemblyLoadContext? context, string name)
        => context?.Assemblies.FirstOrDefault(assembly => string.Equals(assembly.GetName().Name, name, StringComparison.Ordinal));
}
