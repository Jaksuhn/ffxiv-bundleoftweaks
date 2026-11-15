using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using System.Reflection;

namespace ComplexTweaks.Tasks;

public static class TaskRegistry {
    private static readonly Lazy<Dictionary<string, Type>> _tasks = new(()
        => typeof(TaskRegistry).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && t.IsAssignableTo(typeof(TaskBase)) && t.IsDefined(typeof(RunnableTaskAttribute)))
            .ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase));

    public static void Run(string name, string args) {
        if (name.IsEmpty) {
            Print(_tasks.Value.Count == 0 ? "No tasks available." : $"Tasks: {string.Join(", ", _tasks.Value.Keys.Order())}");
            return;
        }

        if (!_tasks.Value.TryGetValue(name, out var type)) {
            Print($"Unknown task \"{name}\".");
            return;
        }

        var missing = IPCRegistry.Get().GetMissing(type);
        if (missing.Length > 0) {
            Print($"{type.Name} requires: {string.Join(", ", missing.Select(ipc => ipc.Name))}");
            return;
        }

        TaskBase task;
        try {
            task = (TaskBase)(type.GetConstructor([typeof(string)]) is { } withArgs ? withArgs.Invoke([args]) : Activator.CreateInstance(type)!);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is { } inner) {
            Print($"{type.Name}: {inner.Message}");
            return;
        }
        Svc.Automation.Start(task);
    }

    private static void Print(string message) => IChatGui.Get().Print(new XivChatEntry {
        Message = new SeStringBuilder().AddUiForeground("[Tasks] ", 62).Append(message).Build()
    });
}
