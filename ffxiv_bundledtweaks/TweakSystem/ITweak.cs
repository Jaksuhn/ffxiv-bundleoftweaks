namespace ComplexTweaks.TweakSystem;

public interface ITweak : IDisposable {
    Type CachedType { get; }
    string InternalName { get; }
    IncompatibilityWarningAttribute[] IncompatibilityWarnings { get; }

    string Name { get; }
    string Description { get; }

    bool Outdated { get; }
    bool Ready { get; }
    bool Enabled { get; }

    void SetupAddressHooks();
    void SetupVTableHooks();

    void Enable();
    void Disable();
    void DrawConfig();
    void OnConfigChange(string fieldName);
}
