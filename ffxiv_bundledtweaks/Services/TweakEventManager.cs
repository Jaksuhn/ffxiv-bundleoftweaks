using System.Collections.Concurrent;

namespace ComplexTweaks.Services;

public interface ITweakEvent {
    TweakEvent[] Events { get; }
    void RegisterHandlers(TweakEventManager manager);
}

public enum TweakEvent {
    FateJoined,
    FateLeft,
    Died,
    Revived,
}

public class TweakEventManager {
    private readonly ConcurrentDictionary<TweakEvent, EventSubscription> _subscriptions = [];
    private readonly ConcurrentDictionary<TweakEvent, EventTracker> _trackers = [];
    private readonly Dictionary<Delegate, SharedHandlerInfo> _sharedHandlers = [];

    private class EventSubscription {
        public int SubscriberCount { get; set; }
        public List<Action<Type, EventArgs>> Handlers { get; } = [];
    }

    private class EventTracker {
        public Action<IFramework>? FrameworkUpdateHandler { get; set; }
        public Action<ConditionFlag, bool>? ConditionChangeHandler { get; set; }
        public bool IsActive { get; set; }

        public void InvokeFramework(IFramework framework) => FrameworkUpdateHandler?.Invoke(framework);
        public void InvokeCondition(ConditionFlag flag, bool value) => ConditionChangeHandler?.Invoke(flag, value);
    }

    private class SharedHandlerInfo {
        public HashSet<TweakEvent> Events { get; } = [];
        public int ActiveEventCount { get; set; }
        public bool IsRegistered { get; set; }
    }

    public TweakEventManager() {
        foreach (var type in typeof(TweakEventManager).Assembly.GetTypes().Where(t => typeof(ITweakEvent).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)) {
            try {
                if (Activator.CreateInstance(type) is ITweakEvent eventTracker)
                    eventTracker.RegisterHandlers(this);
            }
            catch (Exception ex) {
                Svc.Log.Error(ex, $"[{nameof(TweakEventManager)}] Failed to register event tracker {type.Name}");
            }
        }
    }

    public void Subscribe(TweakEvent eventEnum, Action<Type, EventArgs> handler) {
        var subscription = _subscriptions.AddOrUpdate(
            eventEnum,
            _ => new EventSubscription { SubscriberCount = 1 },
            (_, existing) => {
                existing.SubscriberCount++;
                return existing;
            });

        if (!subscription.Handlers.Contains(handler))
            subscription.Handlers.Add(handler);

        if (subscription.SubscriberCount == 1) // only activate on first subscriber so we don't duplicate trackers
            ActivateTracker(eventEnum);
    }

    public void Unsubscribe(TweakEvent eventEnum, Action<Type, EventArgs> handler) {
        if (!_subscriptions.TryGetValue(eventEnum, out var subscription))
            return;

        subscription.Handlers.Remove(handler);
        subscription.SubscriberCount--;

        if (subscription.SubscriberCount <= 0) {
            _subscriptions.TryRemove(eventEnum, out _);
            DeactivateTracker(eventEnum);
        }
    }

    public void Invoke(TweakEvent eventEnum, Type senderType, EventArgs args) {
        if (_subscriptions.TryGetValue(eventEnum, out var subscription)) {
            foreach (var handler in subscription.Handlers) {
                try {
                    handler(senderType, args);
                }
                catch (Exception ex) {
                    Svc.Log.Error(ex, $"Error invoking handler for event {eventEnum}");
                }
            }
        }
    }

    public void RegisterFrameworkUpdateHandler(TweakEvent eventEnum, Action<IFramework> handler) {
        var tracker = _trackers.GetOrAdd(eventEnum, _ => new EventTracker());
        tracker.FrameworkUpdateHandler = handler;

        if (!_sharedHandlers.ContainsKey(handler))
            _sharedHandlers[handler] = new SharedHandlerInfo();
        _sharedHandlers[handler].Events.Add(eventEnum);

        // If there are already subscribers, activate immediately
        if (_subscriptions.ContainsKey(eventEnum))
            ActivateTracker(eventEnum);
    }

    public void RegisterConditionChangeHandler(TweakEvent eventEnum, Action<ConditionFlag, bool> handler) {
        var tracker = _trackers.GetOrAdd(eventEnum, _ => new EventTracker());
        tracker.ConditionChangeHandler = handler;

        if (!_sharedHandlers.ContainsKey(handler))
            _sharedHandlers[handler] = new SharedHandlerInfo();
        _sharedHandlers[handler].Events.Add(eventEnum);

        // If there are already subscribers, activate immediately
        if (_subscriptions.ContainsKey(eventEnum))
            ActivateTracker(eventEnum);
    }

    private void ActivateTracker(TweakEvent eventEnum) {
        if (!_trackers.TryGetValue(eventEnum, out var tracker))
            return;

        if (tracker.FrameworkUpdateHandler != null) {
            if (_sharedHandlers.TryGetValue(tracker.FrameworkUpdateHandler, out var sharedInfo)) {
                sharedInfo.ActiveEventCount++;
                if (!sharedInfo.IsRegistered) {
                    Svc.Framework.Update += tracker.InvokeFramework;
                    sharedInfo.IsRegistered = true;
                }
            }
        }

        if (tracker.ConditionChangeHandler != null) {
            if (_sharedHandlers.TryGetValue(tracker.ConditionChangeHandler, out var sharedInfo)) {
                sharedInfo.ActiveEventCount++;
                if (!sharedInfo.IsRegistered) {
                    Svc.Condition.ConditionChange += tracker.InvokeCondition;
                    sharedInfo.IsRegistered = true;
                }
            }
        }

        tracker.IsActive = true;
    }

    private void DeactivateTracker(TweakEvent eventEnum) {
        if (!_trackers.TryGetValue(eventEnum, out var tracker))
            return;

        if (tracker.FrameworkUpdateHandler != null) {
            if (_sharedHandlers.TryGetValue(tracker.FrameworkUpdateHandler, out var sharedInfo)) {
                sharedInfo.ActiveEventCount--;
                if (sharedInfo.ActiveEventCount <= 0 && sharedInfo.IsRegistered) {
                    Svc.Framework.Update -= tracker.InvokeFramework;
                    sharedInfo.IsRegistered = false;
                }
            }
        }

        if (tracker.ConditionChangeHandler != null) {
            if (_sharedHandlers.TryGetValue(tracker.ConditionChangeHandler, out var sharedInfo)) {
                sharedInfo.ActiveEventCount--;
                if (sharedInfo.ActiveEventCount <= 0 && sharedInfo.IsRegistered) {
                    Svc.Condition.ConditionChange -= tracker.InvokeCondition;
                    sharedInfo.IsRegistered = false;
                }
            }
        }

        tracker.IsActive = false;
    }
}
