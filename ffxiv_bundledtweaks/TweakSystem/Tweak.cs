using Dalamud.Bindings.ImGui;
using Dalamud.Game.Command;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Hooking;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Utility.Signatures;
using ECommons.Automation.NeoTaskManager;
using ECommons.EzHookManager;
using ECommons.Reflection;
using ECommons.SimpleGui;
using System.Reflection;

namespace ComplexTweaks.TweakSystem;

public abstract partial class Tweak : ITweak {
    public Tweak() {
        CachedType = GetType();
        InternalName = CachedType.Name;
        IncompatibilityWarnings = [.. CachedType.GetCustomAttributes<IncompatibilityWarningAttribute>()];

        var tweakAttr = CachedType.GetCustomAttribute<TweakAttribute>();
        Outdated = tweakAttr?.Outdated ?? false;
        Disabled = tweakAttr?.Disabled ?? false;
        DisabledReason = tweakAttr?.DisabledReason;
        IsDebug = tweakAttr?.Debug ?? false;
        Requirements = Service.IPC.GetMany([.. CachedType.GetCustomAttributes<RequiresAttribute>().SelectMany(r => r.Id.ToArray()).Distinct()]);

        try {
            EzSignatureHelper.Initialize(this);
            Svc.Hook.InitializeFromAttributes(this);
        }
        catch (SignatureException ex) {
            Error(ex, "SignatureException, flagging as outdated");
            Outdated = true;
            LastInternalException = ex;
            return;
        }

        try {
            SetupHooks();
        }
        catch (Exception ex) {
            Error(ex, "Unexpected error during SetupHooks");
            LastInternalException = ex;
            return;
        }

        TaskManager = new();
        Ready = true;
    }

    public Type CachedType { get; init; }
    public string InternalName { get; init; }
    public IncompatibilityWarningAttribute[] IncompatibilityWarnings { get; init; }

    public BaseIPC[] Requirements { get; }

    public abstract string Name { get; }
    public abstract string Description { get; }
    public bool IsDebug { get; }

    public bool Outdated { get; protected set; }
    public bool Ready { get; protected set; }
    public bool Enabled { get; protected set; }
    public bool Disabled { get; protected set; }
    public string? DisabledReason { get; protected set; }

    protected TaskManager TaskManager = null!;

    protected Type? CachedConfigType { get; set; }
    protected Type? CachedWindowType { get; set; }
    protected Window? _window;

    private readonly Dictionary<TweakEvent, List<Action<Type, EventArgs>>> _eventHandlers = [];

    protected virtual object? GetConfigObject() => null;

    public TConfig? GetConfig<TConfig>() where TConfig : class {
        if (CachedConfigType == typeof(TConfig)) {
            var config = GetConfigObject();
            if (config is TConfig typedConfig)
                return typedConfig;
        }
        return null;
    }

    protected TWindow? Window<TWindow>() where TWindow : Window => _window is TWindow window ? window : EzConfigGui.GetWindow<TWindow>();

    protected IEnumerable<MethodInfo> CommandHandlers
        => CachedType
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(mi => mi.GetCustomAttribute<CommandHandlerAttribute>() != null);

    protected IEnumerable<MethodInfo> EventHandlers
        => CachedType
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(mi => mi.GetCustomAttribute<TweakEventAttribute>() != null);

    public virtual void SetupAddressHooks() { }
    public virtual void SetupVTableHooks() { }
    public virtual void SetupHooks() { }

    public virtual void Enable() { }
    public virtual void Disable() { }
    public virtual void Dispose() { }
    public virtual void DrawConfig() {
        var config = GetConfigObject();
        if (CachedConfigType != null && config != null) {
            var configFields = CachedConfigType.GetFields()
                .Select(fieldInfo => (FieldInfo: fieldInfo, Attribute: fieldInfo.GetCustomAttribute<BaseConfigAttribute>()))
                .Where((tuple) => tuple.Attribute != null)
                .Cast<(FieldInfo, BaseConfigAttribute)>();

            if (configFields.Any()) {
                ImGui.DrawSection("Configuration");

                foreach (var (field, attr) in configFields) {
                    var hasDependency = !string.IsNullOrEmpty(attr.DependsOn);
                    var isDisabled = hasDependency && (bool?)CachedConfigType.GetField(attr.DependsOn)?.GetValue(config) == false;

                    using var id = ImRaii.PushId(field.Name);
                    using var indent = ImGui.ConfigIndent(hasDependency);
                    using var disabled = ImRaii.Disabled(isDisabled);

                    attr.Draw(this, config, field);
                }
            }
        }

        DrawCommands();
    }
    public virtual void OnConfigChange(string fieldName) { }
}

public abstract partial class Tweak // Internal
{
    private bool Disposed { get; set; }
    internal Exception? LastInternalException { get; set; }

    protected IEnumerable<PropertyInfo> Hooks => CachedType
        .GetProperties(BindingFlags.NonPublic | BindingFlags.Instance)
        .Where(prop =>
            prop.PropertyType.IsGenericType &&
            prop.PropertyType.GetGenericTypeDefinition() == typeof(Hook<>)
        );

    protected IEnumerable<FieldInfo> EzHooks => CachedType
        .GetFields(ReflectionHelper.AllFlags)
        .Where(f => f.FieldType.IsGenericType && f.FieldType?.GetGenericTypeDefinition() == typeof(EzHook<>));

    protected void CallHooks(string methodName) {
        foreach (var property in Hooks) {
            var hook = property.GetValue(this);
            if (hook == null) continue;

            Debug($"Calling {methodName} on {property.Name}");
            typeof(Hook<>)
                .MakeGenericType(property.PropertyType.GetGenericArguments().First())
                .GetMethod(methodName)?
                .Invoke(hook, null);
        }

        if (methodName is "Enable" or "Disable") // EzHook doesn't have Dispose
        {
            foreach (var field in EzHooks) {
                if (field.GetValue(this) is { } hook)
                    hook?.GetType()?.GetMethod(methodName)?.Invoke(hook, null);
            }
        }
    }

    internal virtual void EnableInternal() {
        if (!Ready || Outdated || Disabled) return;
        if (Requirements.Any(r => !r.IsLoaded)) {
            // TODO: append a button to re-enable
            ModuleMessage("Feature not enabled due to missing dependencies. Please install them then re-enable this feature.");
            return;
        }

        if (CachedWindowType != null && _window == null) {
            try {
                var getWindowMethod = typeof(EzConfigGui).GetMethod("GetWindow", [])?.MakeGenericMethod(CachedWindowType);
                if (getWindowMethod?.Invoke(null, null) is Window existingWindow)
                    _window = existingWindow;
                else {
                    var constructor = CachedWindowType.GetConstructor([CachedType]);
                    if (constructor != null)
                        _window = (Window?)constructor.Invoke([this]);
                    else {
                        constructor = CachedWindowType.GetConstructor([]);
                        _window = constructor != null
                            ? (Window?)constructor.Invoke([])
                            : throw new InvalidOperationException($"Window type {CachedWindowType.Name} must have either a parameterless constructor or a constructor that takes {CachedType.Name}.");
                    }

                    if (_window != null)
                        EzConfigGui.WindowSystem.AddWindow(_window);
                }
            }
            catch (Exception ex) {
                Error(ex, $"Failed to create window {CachedWindowType.Name}");
                LastInternalException = ex;
                return;
            }
        }

        try {
            EnableCommands();
        }
        catch (Exception ex) {
            Error(ex, "Unexpected error during Enable (Commands)");
            LastInternalException = ex;
        }

        try {
            if (EventHandlers.Any(eh => eh.GetCustomAttribute<TweakEventAttribute>()?.AutoEnable != false))
                EnableEventHandlers();
        }
        catch (Exception ex) {
            Error(ex, "Unexpected error during Enable (Event Handlers)");
            LastInternalException = ex;
        }

        try {
            CallHooks("Enable");
        }
        catch (Exception ex) {
            Error(ex, "Unexpected error during Enable (Hooks)");
            LastInternalException = ex;
            return;
        }

        try {
            Enable();
        }
        catch (Exception ex) {
            Error(ex, "Unexpected error during Enable");
            LastInternalException = ex;
            return;
        }

        LastInternalException = null;
        Enabled = true;
    }

    public bool CanBeEnabled() => Ready && !Outdated && !Disabled && Requirements.All(r => r.IsLoaded);

    internal virtual void DisableInternal(bool isDisposing = false) {
        if (!Enabled) return;

        try {
            DisableCommands();
        }
        catch (Exception ex) {
            Error(ex, "Unexpected error during Disable (Commands)");
            LastInternalException = ex;
        }

        try {
            if (AreEventHandlersEnabled)
                DisableEventHandlers();
        }
        catch (Exception ex) {
            Error(ex, "Unexpected error during Disable (Event Handlers)");
            LastInternalException = ex;
        }

        if (!isDisposing) {
            try {
                CallHooks("Disable");
            }
            catch (Exception ex) {
                Error(ex, "Unexpected error during Disable (Hooks)");
                LastInternalException = ex;
            }
        }

        try {
            Disable();
        }
        catch (Exception ex) {
            Error(ex, "Unexpected error during Disable");
            LastInternalException = ex;
        }

        if (_window != null && CachedWindowType != null) {
            try {
                EzConfigGui.WindowSystem.RemoveWindow(_window);
                _window = null;
            }
            catch (Exception ex) {
                Error(ex, $"Failed to remove window {CachedWindowType.Name}");
            }
        }

        Enabled = false;
    }

    internal virtual void DisposeInternal() {
        if (Disposed)
            return;

        DisableInternal(true);

        try {
            CallHooks("Dispose");
        }
        catch (Exception ex) {
            Error(ex, "Unexpected error during Dispose (Hooks)");
            LastInternalException = ex;
        }

        try {
            Dispose();
        }
        catch (Exception ex) {
            Error(ex, "Unexpected error during Dispose");
            LastInternalException = ex;
        }

        Ready = false;
        Disposed = true;
    }

    internal virtual void OnConfigChangeInternal(string fieldName) {
        foreach (var methodInfo in CommandHandlers) {
            var attr = methodInfo.GetCustomAttribute<CommandHandlerAttribute>()!;
            if (attr.ConfigFieldName != fieldName)
                continue;

            var enabled = string.IsNullOrEmpty(attr.ConfigFieldName);

            if (!string.IsNullOrEmpty(attr.ConfigFieldName) && CachedConfigType != null) {
                var config = GetConfigObject();
                if (config != null)
                    enabled |= (CachedConfigType.GetField(attr.ConfigFieldName)?.GetValue(config) as bool?)
                        ?? throw new InvalidOperationException($"Configuration field {attr.ConfigFieldName} in {CachedConfigType.Name} not found.");
            }

            if (enabled && methodInfo.GetCustomAttributes<RequiresAttribute>().SelectMany(r => r.Id.ToArray()).Distinct().ToArray() is { Length: > 0 } reqs) {
                if (!Service.IPC.AreAllLoaded(reqs)) {
                    var missing = Service.IPC.GetMissing(reqs);
                    Warning($"Cannot enable command(s) [{string.Join(", ", attr.Commands)}]: missing dependencies: {string.Join(", ", missing.Select(ipc => ipc.Name))}");
                    enabled = false;
                }
            }

            if (enabled)
                foreach (var c in attr.Commands)
                    EnableCommand(c, attr.HelpMessage, methodInfo, attr);
            else
                foreach (var c in attr.Commands)
                    DisableCommand(c);
        }

        try {
            OnConfigChange(fieldName);
        }
        catch (Exception ex) {
            Error(ex, "Unexpected error during OnConfigChange");
            LastInternalException = ex;
            return;
        }
    }

    protected virtual void EnableCommands() {
        foreach (var methodInfo in CommandHandlers) {
            var attr = methodInfo.GetCustomAttribute<CommandHandlerAttribute>()!;
            var enabled = string.IsNullOrEmpty(attr.ConfigFieldName);

            if (!string.IsNullOrEmpty(attr.ConfigFieldName) && CachedConfigType != null) {
                var config = GetConfigObject();
                if (config != null)
                    enabled |= (CachedConfigType.GetField(attr.ConfigFieldName)?.GetValue(config) as bool?)
                        ?? throw new InvalidOperationException($"Configuration field {attr.ConfigFieldName} in {CachedConfigType.Name} not found.");
            }

            if (enabled && methodInfo.GetCustomAttributes<RequiresAttribute>().SelectMany(r => r.Id.ToArray()).Distinct().ToArray() is { Length: > 0 } reqs) {
                if (!Service.IPC.AreAllLoaded(reqs)) {
                    var missing = Service.IPC.GetMissing(reqs);
                    var missingNames = missing.Length > 0 ? string.Join(", ", missing.Select(ipc => ipc.Name)) : "one or more required IPCs are not registered";
                    Warning($"Cannot enable command(s) [{string.Join(", ", attr.Commands)}]: missing dependencies: {missingNames}");
                    continue;
                }
            }

            if (enabled)
                foreach (var c in attr.Commands)
                    EnableCommand(c, attr.HelpMessage, methodInfo, attr);
        }
    }

    protected virtual void DisableCommands() {
        foreach (var methodInfo in CommandHandlers) {
            var attr = methodInfo.GetCustomAttribute<CommandHandlerAttribute>()!;
            var enabled = string.IsNullOrEmpty(attr.ConfigFieldName);

            if (!string.IsNullOrEmpty(attr.ConfigFieldName) && CachedConfigType != null) {
                var config = GetConfigObject();
                if (config != null)
                    enabled |= (CachedConfigType.GetField(attr.ConfigFieldName)?.GetValue(config) as bool?)
                        ?? throw new InvalidOperationException($"Configuration field {attr.ConfigFieldName} in {CachedConfigType.Name} not found.");
            }

            if (enabled)
                foreach (var c in attr.Commands)
                    DisableCommand(c);
        }
    }

    public void EnableEventHandlers() {
        if (AreEventHandlersEnabled)
            return;

        foreach (var methodInfo in EventHandlers) {
            var attr = methodInfo.GetCustomAttribute<TweakEventAttribute>()!;
            var parameters = methodInfo.GetParameters();

            if (parameters.Length != 2 || parameters[0].ParameterType != typeof(Type) || parameters[1].ParameterType != typeof(EventArgs)) {
                Error($"Event handler method {methodInfo.Name} in {CachedType.Name} must have exactly two parameters: (Type, EventArgs)");
                continue;
            }

            void handler(Type senderType, EventArgs args) {
                try {
                    methodInfo.Invoke(this, [senderType, args]);
                }
                catch (Exception ex) {
                    Error(ex, $"Error invoking event handler {methodInfo.Name}");
                }
            }

            foreach (var eventEnum in attr.Events) {
                Service.TweakEventManager.Subscribe(eventEnum, handler);
                if (!_eventHandlers.ContainsKey(eventEnum))
                    _eventHandlers[eventEnum] = [];
                _eventHandlers[eventEnum].Add(handler);
            }
        }

        AreEventHandlersEnabled = true;
    }

    public void DisableEventHandlers() {
        if (!AreEventHandlersEnabled)
            return;

        foreach (var (eventName, handlers) in _eventHandlers)
            foreach (var handler in handlers)
                Service.TweakEventManager.Unsubscribe(eventName, handler);
        _eventHandlers.Clear();
        AreEventHandlersEnabled = false;
    }

    public bool AreEventHandlersEnabled { get; private set; } = false;

    protected void DrawCommands() {
        var commandHandlers = CommandHandlers
        .Select(m => m.GetCustomAttribute<CommandHandlerAttribute>()!)
        .Where(attr =>
            // Show command if it has no config field dependency
            string.IsNullOrEmpty(attr.ConfigFieldName) ||
            // Or if the config field is enabled
            (CachedConfigType != null && GetConfigObject() != null && (bool?)CachedConfigType.GetField(attr.ConfigFieldName)?.GetValue(GetConfigObject()) == true))
        .Where(attr => attr.Commands.Any(cmd => Svc.Commands.Commands.ContainsKey(cmd)));

        if (commandHandlers.Any()) {
            ImGui.DrawSection("Available Commands");
            foreach (var attr in commandHandlers) {
                foreach (var cmd in attr.Commands.Where(Svc.Commands.Commands.ContainsKey)) {
                    var commandInfo = Svc.Commands.Commands[cmd];
                    ImGui.Text($"{cmd}");
                    if (!string.IsNullOrEmpty(commandInfo.HelpMessage)) {
                        ImGui.SameLine();
                        ImGui.TextColoredWrapped(Colors.Grey, commandInfo.HelpMessage);
                    }

                    if (attr.SubCommands.Count != 0) {
                        foreach (var subCmd in attr.SubCommands) {
                            using var subIndent = ImGui.ConfigIndent();
                            ImGui.Text($"{cmd} {subCmd.Subcommand}");
                            ImGui.SameLine();
                            ImGui.TextColoredWrapped(Colors.Grey, subCmd.HelpMessage);
                        }
                    }
                }
            }
        }
    }

    private void EnableCommand(string command, string helpMessage, MethodInfo methodInfo, CommandHandlerAttribute attr) {
        var originalHandler = methodInfo.CreateDelegate<IReadOnlyCommandInfo.HandlerDelegate>(this);
        void handler(string cmd, string args) {
            if (methodInfo.GetCustomAttributes<RequiresAttribute>().SelectMany(r => r.Id.ToArray()).Distinct().ToArray() is { Length: > 0 } reqs) {
                if (!Service.IPC.AreAllLoaded(reqs)) {
                    var missing = Service.IPC.GetMissing(reqs);
                    ModuleMessage($"Command {cmd} requires: {string.Join(", ", missing.Select(ipc => ipc.Name))}");
                    return;
                }
            }

            originalHandler(cmd, args);
        }

        if (Svc.Commands.AddHandler(command, new CommandInfo(handler) { HelpMessage = helpMessage, DisplayOrder = 1 }))
            Log($"Added CommandHandler for {command}");
        else
            Warning($"Could not add CommandHandler for {command}");
    }

    private void DisableCommand(string command) {
        if (Svc.Commands.RemoveHandler(command))
            Log($"Removed CommandHandler for {command}");
        else
            Warning($"Could not remove CommandHandler for {command}");
    }
}

public abstract partial class Tweak // Logging
{
    public void Log(string messageTemplate)
        => Information(messageTemplate);

    public void Log(Exception exception, string messageTemplate)
        => Information(exception, messageTemplate);

    public void Verbose(string messageTemplate)
        => PluginLog.Verbose($"[{InternalName}] {messageTemplate}");

    public void Verbose(Exception exception, string messageTemplate)
        => exception.LogVerbose($"[{InternalName}] {messageTemplate}");

    public void Debug(string messageTemplate)
        => PluginLog.Debug($"[{InternalName}] {messageTemplate}");

    public void Debug(Exception exception, string messageTemplate)
        => exception.LogDebug($"[{InternalName}] {messageTemplate}");

    public void Information(string messageTemplate)
        => PluginLog.Information($"[{InternalName}] {messageTemplate}");

    public void Information(Exception exception, string messageTemplate)
        => exception.LogInfo($"[{InternalName}] {messageTemplate}");

    public void Warning(string messageTemplate)
        => PluginLog.Warning($"[{InternalName}] {messageTemplate}");

    public void Warning(Exception exception, string messageTemplate)
        => exception.LogWarning($"[{InternalName}] {messageTemplate}");

    public void Error(string messageTemplate)
        => PluginLog.Error($"[{InternalName}] {messageTemplate}");

    public void Error(Exception exception, string messageTemplate)
        => exception.Log($"[{InternalName}] {messageTemplate}");

    public void Fatal(string messageTemplate)
        => PluginLog.Fatal($"[{InternalName}] {messageTemplate}");

    public void Fatal(Exception exception, string messageTemplate)
        => exception.LogFatal($"[{InternalName}] {messageTemplate}");

    public void ModuleMessage(SeString messageTemplate) => ModuleMessage(messageTemplate.TextValue);
    public void ModuleMessage(string messageTemplate) {
        var message = new XivChatEntry {
            Message = new SeStringBuilder()
                .AddUiForeground($"[{Name}] ", 62)
                .Append(messageTemplate)
                .Build()
        };

        Svc.Chat.Print(message);
    }
}
