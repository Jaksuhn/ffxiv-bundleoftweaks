namespace ComplexTweaks.Attributes;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class IncompatibilityWarningAttribute(string InternalName, params string[] ConfigNames) : Attribute {
    public string InternalName { get; } = InternalName;
    public string[] ConfigNames { get; } = ConfigNames;

    public bool IsLoaded => Svc.PluginInterface.InstalledPlugins.Any(p => p.InternalName == InternalName && p.IsLoaded);
}
