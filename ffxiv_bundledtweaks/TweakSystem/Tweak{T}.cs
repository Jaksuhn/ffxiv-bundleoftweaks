using Dalamud.Interface.Windowing;

namespace ComplexTweaks.TweakSystem;

public abstract class Tweak<T> : Tweak where T : new() {
    private static readonly Type WindowType = typeof(Window);

    public Tweak() : base() {
        var type = typeof(T);

        if (WindowType.IsAssignableFrom(type))
            CachedWindowType = type;
        else {
            CachedConfigType = type;
            Config = ConfigService.Get().GetTweakConfig<T>(ConfigKey);
        }
    }

    public T Config { get; init; } = default!;

    protected override object? GetConfigObject() => CachedConfigType != null ? Config : null;
}

public abstract class Tweak<TConfig, TWindow> : Tweak where TConfig : new() where TWindow : Window {
    private static readonly Type WindowType = typeof(Window);

    public Tweak() : base() {
        var configType = typeof(TConfig);
        var windowType = typeof(TWindow);

        if (!WindowType.IsAssignableFrom(windowType))
            throw new InvalidOperationException($"Type {windowType.Name} ({nameof(TWindow)}) must be a Window (inheriting from {WindowType.Name}).");

        CachedConfigType = configType;
        CachedWindowType = windowType;
        Config = ConfigService.Get().GetTweakConfig<TConfig>(ConfigKey);
    }

    public TConfig Config { get; init; } = default!;

    protected override object? GetConfigObject() => Config;
}
