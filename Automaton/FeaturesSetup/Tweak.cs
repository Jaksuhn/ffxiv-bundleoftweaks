using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Hooking;
using Dalamud.Utility.Signatures;
using ECommons.Automation.NeoTaskManager;
using ECommons.EzHookManager;
using ECommons.Reflection;
using System.Reflection;

namespace Automaton.FeaturesSetup;

public abstract partial class Tweak : ITweak
{
    public Tweak()
    {
        CachedType = GetType();
        InternalName = CachedType.Name;
        IncompatibilityWarnings = [.. CachedType.GetCustomAttributes<IncompatibilityWarningAttribute>()];

        var tweakAttr = CachedType.GetCustomAttribute<TweakAttribute>();
        Outdated = tweakAttr?.Outdated ?? false;
        Disabled = tweakAttr?.Disabled ?? false;
        DisabledReason = tweakAttr?.DisabledReason;
        IsDebug = tweakAttr?.Debug ?? false;
        Requirements = Service.IPC.GetMany([.. CachedType.GetCustomAttributes<RequiresAttribute>().SelectMany(r => r.Id.ToArray()).Distinct()]);

        try
        {
            EzSignatureHelper.Initialize(this);
            Svc.Hook.InitializeFromAttributes(this);
        }
        catch (SignatureException ex)
        {
            Error(ex, "SignatureException, flagging as outdated");
            Outdated = true;
            LastInternalException = ex;
            return;
        }

        try
        {
            SetupVTableHooks(); // before SetupAddressHooks!
        }
        catch (Exception ex)
        {
            Error(ex, "Unexpected error during SetupVTableHooks");
            LastInternalException = ex;
            return;
        }

        try
        {
            SetupAddressHooks();
        }
        catch (Exception ex)
        {
            Error(ex, "Unexpected error during SetupAddressHooks");
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

    public virtual void SetupAddressHooks() { }
    public virtual void SetupVTableHooks() { }

    public virtual void Enable() { }
    public virtual void Disable() { }
    public virtual void Dispose() { }
    public virtual void DrawConfig() { }
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

    protected void CallHooks(string methodName)
    {
        foreach (var property in Hooks)
        {
            var hook = property.GetValue(this);
            if (hook == null) continue;

            typeof(Hook<>)
                .MakeGenericType(property.PropertyType.GetGenericArguments().First())
                .GetMethod(methodName)?
                .Invoke(hook, null);
        }

        if (methodName is "Enable" or "Disable") // EzHook doesn't have Dispose
        {
            foreach (var field in EzHooks)
            {
                if (field.GetValue(this) is { } hook)
                    hook?.GetType()?.GetMethod(methodName)?.Invoke(hook, null);
            }
        }
    }

    internal virtual void EnableInternal()
    {
        if (!Ready || Outdated || Disabled) return;
        if (Requirements.Any(r => !r.IsLoaded))
        {
            // TODO: append a button to re-enable
            ModuleMessage("Feature not enabled due to missing dependencies. Please install them then re-enable this feature.");
            return;
        }

        try
        {
            EnableCommands();
        }
        catch (Exception ex)
        {
            Error(ex, "Unexpected error during Enable (Commands)");
            LastInternalException = ex;
        }

        try
        {
            CallHooks("Enable");
        }
        catch (Exception ex)
        {
            Error(ex, "Unexpected error during Enable (Hooks)");
            LastInternalException = ex;
            return;
        }

        try
        {
            Enable();
        }
        catch (Exception ex)
        {
            Error(ex, "Unexpected error during Enable");
            LastInternalException = ex;
            return;
        }

        LastInternalException = null;
        Enabled = true;
    }

    public bool CanBeEnabled() => Ready && !Outdated && !Disabled && Requirements.All(r => r.IsLoaded);

    internal virtual void DisableInternal(bool isDisposing = false)
    {
        if (!Enabled) return;

        try
        {
            DisableCommands();
        }
        catch (Exception ex)
        {
            Error(ex, "Unexpected error during Disable (Commands)");
            LastInternalException = ex;
        }

        if (!isDisposing)
        {
            try
            {
                CallHooks("Disable");
            }
            catch (Exception ex)
            {
                Error(ex, "Unexpected error during Disable (Hooks)");
                LastInternalException = ex;
            }
        }

        try
        {
            Disable();
        }
        catch (Exception ex)
        {
            Error(ex, "Unexpected error during Disable");
            LastInternalException = ex;
        }

        Enabled = false;
    }

    internal virtual void DisposeInternal()
    {
        if (Disposed)
            return;

        DisableInternal(true);

        try
        {
            CallHooks("Dispose");
        }
        catch (Exception ex)
        {
            Error(ex, "Unexpected error during Dispose (Hooks)");
            LastInternalException = ex;
        }

        try
        {
            Dispose();
        }
        catch (Exception ex)
        {
            Error(ex, "Unexpected error during Dispose");
            LastInternalException = ex;
        }

        Ready = false;
        Disposed = true;
    }

    internal virtual void OnConfigChangeInternal(string fieldName)
    {
        try
        {
            OnConfigChange(fieldName);
        }
        catch (Exception ex)
        {
            Error(ex, "Unexpected error during OnConfigChange");
            LastInternalException = ex;
            return;
        }
    }

    protected virtual void EnableCommands() { }
    protected virtual void DisableCommands() { }
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
    public void ModuleMessage(string messageTemplate)
    {
        var message = new XivChatEntry
        {
            Message = new SeStringBuilder()
                .AddUiForeground($"[{Name}] ", 62)
                .Append(messageTemplate)
                .Build()
        };

        Svc.Chat.Print(message);
    }
}
