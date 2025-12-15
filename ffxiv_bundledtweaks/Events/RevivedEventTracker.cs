namespace Automaton.Events;

public class RevivedEventArgs : EventArgs {
    public ConditionFlag Flag { get; set; }
    public bool Value { get; set; }
}

public class RevivedEventTracker : ITweakEvent {
    private bool _wasUnconscious;

    public TweakEvent[] Events => [TweakEvent.Revived];

    public void RegisterHandlers(TweakEventManager manager) => manager.RegisterConditionChangeHandler(TweakEvent.Revived, OnConditionChange);

    private void OnConditionChange(ConditionFlag flag, bool value) {
        if (flag != ConditionFlag.Unconscious) return;

        if (value) {
            _wasUnconscious = true;
        }
        else if (!value && _wasUnconscious) {
            _wasUnconscious = false;
            Service.TweakEventManager.Invoke(TweakEvent.Revived, typeof(RevivedEventTracker), new RevivedEventArgs {
                Flag = flag,
                Value = value
            });
        }
    }
}

