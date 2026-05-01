using System.Reflection;

namespace ComplexTweaks.TweakSystem;

internal sealed class TweakEventController(Tweak owner) {
    private readonly List<(MethodInfo Method, FrameworkUpdateAttribute Attribute)> _frameworkMethods = [.. owner.CachedType
        .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
        .Where(mi => mi.GetCustomAttribute<FrameworkUpdateAttribute>() is not null)
        .Select(mi => (Method: mi, Attribute: mi.GetCustomAttribute<FrameworkUpdateAttribute>()!))];

    private readonly Dictionary<MethodInfo, IFramework.OnUpdateDelegate> _frameworkHandlers = [];

    private bool AreFrameworkUpdateHandlersEnabled { get; set; }

    internal void EnableHandlers() {
        EnableFrameworkUpdateHandlers();
    }

    internal void DisableHandlers() {
        DisableFrameworkUpdateHandlers();
    }

    private void EnableFrameworkUpdateHandlers() {
        foreach (var (method, attribute) in _frameworkMethods) {
            if (IsConfigEnabled(attribute.ConfigFieldName))
                EnableFrameworkUpdateHandler(method);
        }

        AreFrameworkUpdateHandlersEnabled = true;
    }

    private void DisableFrameworkUpdateHandlers() {
        if (!AreFrameworkUpdateHandlersEnabled)
            return;

        foreach (var (method, _) in _frameworkMethods)
            DisableFrameworkUpdateHandler(method);

        AreFrameworkUpdateHandlersEnabled = false;
    }

    internal void OnConfigChange(string fieldName) {
        foreach (var (method, attribute) in _frameworkMethods) {
            if (attribute.ConfigFieldName != fieldName)
                continue;

            if (IsConfigEnabled(attribute.ConfigFieldName))
                EnableFrameworkUpdateHandler(method);
            else
                DisableFrameworkUpdateHandler(method);
        }
    }

    private bool IsConfigEnabled(string? configFieldName) {
        if (string.IsNullOrEmpty(configFieldName))
            return true;

        var configType = owner.CachedConfigTypeInternal;
        if (configType == null)
            return false;

        var config = owner.GetConfigObjectInternal();
        if (config == null)
            return false;

        return (configType.GetField(configFieldName)?.GetValue(config) as bool?)
            ?? throw new InvalidOperationException($"Configuration field {configFieldName} in {configType.Name} not found.");
    }

    private void EnableFrameworkUpdateHandler(MethodInfo methodInfo) {
        if (_frameworkHandlers.ContainsKey(methodInfo))
            return;

        var parameters = methodInfo.GetParameters();
        if (parameters.Length != 1 || parameters[0].ParameterType != typeof(IFramework)) {
            owner.Error($"Framework update method {methodInfo.Name} in {owner.CachedType.Name} must have exactly one parameter: (IFramework)");
            return;
        }

        var handler = methodInfo.CreateDelegate<IFramework.OnUpdateDelegate>(owner);
        _frameworkHandlers[methodInfo] = handler;
        Svc.Framework.Update += handler;
    }

    private void DisableFrameworkUpdateHandler(MethodInfo methodInfo) {
        if (!_frameworkHandlers.TryGetValue(methodInfo, out var handler))
            return;

        Svc.Framework.Update -= handler;
        _frameworkHandlers.Remove(methodInfo);
    }
}

