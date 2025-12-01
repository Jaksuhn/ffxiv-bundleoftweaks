namespace Automaton.Events;

public class DiedEventArgs : EventArgs {
    public ConditionFlag Flag { get; set; }
    public bool Value { get; set; }
}

public class DiedEventTracker : ITweakEvent {
    private bool _wasDead;

    public TweakEvent[] Events => [TweakEvent.Died];

    public void RegisterHandlers(TweakEventManager manager) => manager.RegisterConditionChangeHandler(TweakEvent.Died, OnConditionChange);

    private void OnConditionChange(ConditionFlag flag, bool value) {
        if (flag != ConditionFlag.Unconscious) return;

        if (value && !_wasDead) {
            _wasDead = true;
            Service.TweakEventManager.Invoke(TweakEvent.Died, typeof(DiedEventTracker), new DiedEventArgs {
                Flag = flag,
                Value = value
            });
        }
        else if (!value && _wasDead)
            _wasDead = false;
    }
}

