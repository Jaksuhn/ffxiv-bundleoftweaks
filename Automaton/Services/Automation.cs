using System.Threading;
using System.Threading.Tasks;

namespace Automaton.Services;

// base class for automation tasks
// all tasks are cancellable, and all continuations are executed on the main thread (in framework update)
// tasks also support progress reporting
// note: it's assumed that any created task will be executed (either by calling Run directly or by passing to Automation.Start)
public abstract class AutoTask
{
    // debug context scope
    protected readonly struct DebugContext : IDisposable
    {
        private readonly AutoTask _ctx;
        private readonly int _depth;

        public DebugContext(AutoTask ctx, string name)
        {
            _ctx = ctx;
            _depth = _ctx._debugContext.Count;
            _ctx._debugContext.Add(name);
            _ctx.Log("Scope enter");
        }

        public void Dispose()
        {
            _ctx.Log($"Scope exit (depth={_depth}, cur={_ctx._debugContext.Count - 1})");
            if (_depth < _ctx._debugContext.Count)
                _ctx._debugContext.RemoveRange(_depth, _ctx._debugContext.Count - _depth);
        }

        public void Rename(string newName)
        {
            _ctx.Log($"Transition to {newName} @ {_depth}");
            if (_depth < _ctx._debugContext.Count)
                _ctx._debugContext[_depth] = newName;
        }
    }

    public string Status { get; protected set; } = ""; // user-facing status string
    private readonly CancellationTokenSource _cts = new();
    private readonly List<string> _debugContext = [];

    public void Cancel() => _cts.Cancel();

    public void Run(Action completed, Action? OnCompleted = null)
    {
        Svc.Framework.Run(async () =>
        {
            var task = Execute();
            await task.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing); // we don't really care about cancelation...
            if (task.IsFaulted)
                PluginLog.Warning($"Task ended with error: {task.Exception}");
            completed();
            OnCompleted?.Invoke();
            _cts.Dispose();
        }, _cts.Token);
    }

    // implementations are typically expected to be async (coroutines)
    protected abstract Task Execute();

    protected CancellationToken CancelToken => _cts.Token;

    // wait for a few frames
    protected Task NextFrame(int numFramesToWait = 1) => Svc.Framework.DelayTicks(numFramesToWait, _cts.Token);

    /// <summary>
    /// Wait until condition function returns false, checking once every N frames
    /// </summary>
    protected async Task WaitWhile(Func<bool> condition, string scopeName, int checkFrequency = 1, bool logContinuously = false)
    {
        using var scope = BeginScope(scopeName);
        Log("waiting...");
        while (condition())
        {
            if (logContinuously)
                Log("waiting...");
            await NextFrame(checkFrequency);
        }
    }

    /// <summary>
    /// Wait until condition function returns true, checking once every N frames
    /// </summary>
    protected async Task WaitUntil(Func<bool> condition, string scopeName, int checkFrequency = 1, bool logContinuously = false) => await WaitWhile(() => !condition(), scopeName, checkFrequency, logContinuously);

    /// <summary>
    /// Wait until a condition function returns true, then wait until it returns false.
    /// </summary>
    /// <remarks> Meant for functions like checking if an ipc is busy then checking til it's not. </remarks>
    protected async Task WaitUntilThenFalse(Func<bool> condition, string scopeName, int checkFrequency = 1, bool logContinuously = false)
    {
        using var scope = BeginScope(scopeName);
        await WaitUntil(condition, scopeName, checkFrequency, logContinuously);
        await WaitWhile(condition, scopeName, checkFrequency, logContinuously);
    }

    /// <summary>
    /// Attempts to perform an action and wait for a success condition, retrying if the condition isn't met within the timeout.
    /// </summary>
    /// <param name="action">The action to perform</param>
    /// <param name="successCondition">Function that returns true when the action was successful</param>
    /// <param name="scopeName">Name for debug logging</param>
    /// <param name="timeoutFrames">Number of frames to wait for success before retrying</param>
    /// <param name="checkFrequency">How often to check the success condition</param>
    /// <param name="logContinuously">Whether to log waiting status continuously</param>
    /// <param name="maxRetries">Maximum number of retry attempts (0 for infinite)</param>
    protected async Task TryUntil(Action action, Func<bool> successCondition, string scopeName, int timeoutFrames = 60, int checkFrequency = 1, bool logContinuously = false, int maxRetries = 0)
    {
        using var scope = BeginScope(scopeName);
        var attempts = 0;
        while (maxRetries == 0 || attempts < maxRetries)
        {
            attempts++;
            Log($"Attempt {attempts}{(maxRetries > 0 ? $"/{maxRetries}" : "")}...");
            action();

            // Wait for success condition
            var success = false;
            for (var i = 0; i < timeoutFrames; i += checkFrequency)
            {
                if (successCondition())
                {
                    success = true;
                    break;
                }
                if (logContinuously)
                    Log("Waiting for success...");
                await NextFrame(checkFrequency);
            }

            if (success)
            {
                Log("Action succeeded");
                break;
            }

            if (maxRetries > 0 && attempts >= maxRetries)
            {
                Error($"Action failed after {maxRetries} attempts");
            }
            else
            {
                Log("Action timed out, retrying...");
            }
        }
    }

    protected void Log(string message) => PluginLog.Debug($"[{GetType().Name}] [{string.Join(" > ", _debugContext)}] {message}");
    protected void Warning(string message) => PluginLog.Warning($"[{GetType().Name}] [{string.Join(" > ", _debugContext)}] {message}");
    protected void WarningIf(bool condition, string message)
    {
        if (condition)
            Warning(message);
    }

    // start a new debug context; should be disposed, so usually should be assigned to RAII variable
    protected DebugContext BeginScope(string name) => new(this, name);

    // abort a task unconditionally
    protected void Error(string message)
    {
        Log($"Error: {message}");
        throw new Exception($"[{GetType().Name}] [{string.Join(" > ", _debugContext)}] {message}");
    }

    // abort a task if condition is true
    protected void ErrorIf(bool condition, string message)
    {
        if (condition)
            Error(message);
    }
}

// utility that allows concurrently executing only one task; starting a new task if one is already in progress automatically cancels olds one
public sealed class Automation : IDisposable
{
    public AutoTask? CurrentTask { get; private set; }
    public bool Running => CurrentTask != null;
    public string Name => CurrentTask?.GetType().Name ?? "None";
    public string Status => CurrentTask?.Status ?? "Idle";
    public void Dispose() => Stop();

    // stop executing any running task
    // this requires tasks to cooperate by checking the token
    public void Stop()
    {
        CurrentTask?.Cancel();
        CurrentTask = null;
        Service.Navmesh.Stop(); // I don't know why OnDispose methods aren't called from a cancel
    }

    // if any other task is running, it's cancelled
    public void Start(AutoTask task, Action? OnCompleted = null)
    {
        Stop();
        CurrentTask = task;
        task.Run(() =>
        {
            if (CurrentTask == task)
                CurrentTask = null;
            // else: some other task is now executing
        }, OnCompleted);
    }
}

public readonly record struct OnDispose(Action A) : IDisposable
{
    public void Dispose() => A();
}
