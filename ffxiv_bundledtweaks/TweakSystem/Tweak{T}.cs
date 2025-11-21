using ComplexTweaks.Configuration;
using Dalamud.Interface.Windowing;

namespace ComplexTweaks.TweakSystem;

public abstract class Tweak<T> : Tweak
{
    private static readonly Type WindowType = typeof(Window);
    private static readonly Type TweakConfigsType = typeof(TweakConfigs);

    public Tweak() : base()
    {
        var type = typeof(T);

        if (WindowType.IsAssignableFrom(type))
            CachedWindowType = type;
        else if (IsConfigType(type))
        {
            CachedConfigType = type;
            Config = (T)(TweakConfigsType
                .GetProperties()?
                .FirstOrDefault(pi => pi!.PropertyType == type, null)?
                .GetValue(C.Tweaks)
                ?? throw new InvalidOperationException($"Configuration for {type.Name} not found."))!;
        }
        else
            throw new InvalidOperationException($"Type {type.Name} must be either a Window (inheriting from {WindowType.Name}) or a Configuration type registered in {TweakConfigsType.Name}.");
    }

    private static bool IsConfigType(Type type) => TweakConfigsType.GetProperties().Any(pi => pi.PropertyType == type);

    public T Config { get; init; } = default!;

    protected override object? GetConfigObject() => CachedConfigType != null ? Config : null;
}

public abstract class Tweak<TConfig, TWindow> : Tweak where TWindow : Window
{
    private static readonly Type WindowType = typeof(Window);
    private static readonly Type TweakConfigsType = typeof(TweakConfigs);

    public Tweak() : base()
    {
        var configType = typeof(TConfig);
        var windowType = typeof(TWindow);

        if (!IsConfigType(configType))
            throw new InvalidOperationException($"Type {configType.Name} ({nameof(TConfig)}) must be a Configuration type registered in {TweakConfigsType.Name}.");

        if (!WindowType.IsAssignableFrom(windowType))
            throw new InvalidOperationException($"Type {windowType.Name} ({nameof(TWindow)}) must be a Window (inheriting from {WindowType.Name}).");

        CachedConfigType = configType;
        CachedWindowType = windowType;

        Config = (TConfig)(TweakConfigsType
            .GetProperties()?
            .FirstOrDefault(pi => pi!.PropertyType == configType, null)?
            .GetValue(C.Tweaks)
            ?? throw new InvalidOperationException($"Configuration for {configType.Name} not found."))!;
    }

    private static bool IsConfigType(Type type) => TweakConfigsType.GetProperties().Any(pi => pi.PropertyType == type);

    public TConfig Config { get; init; } = default!;

    protected override object? GetConfigObject() => Config;
}
