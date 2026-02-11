using AgentSandbox.Core.Telemetry;

namespace AgentSandbox.Core;

/// <summary>
/// Manages observer subscriptions and event notifications.
/// Provides thread-safe observer pattern implementation.
/// </summary>
public class ObserverManager
{
    private readonly List<ISandboxObserver> _observers = new();
    private readonly object _observerLock = new();

    /// <summary>
    /// Subscribes an observer to receive sandbox events.
    /// </summary>
    /// <param name="observer">The observer to subscribe.</param>
    /// <returns>A disposable token that unsubscribes the observer when disposed.</returns>
    public IDisposable Subscribe(ISandboxObserver observer)
    {
        lock (_observerLock)
        {
            _observers.Add(observer);
        }

        return new ObserverUnsubscriber(this, observer);
    }

    /// <summary>
    /// Notifies all subscribed observers by invoking the provided action.
    /// Exceptions thrown by observers are swallowed to prevent cascading failures.
    /// </summary>
    /// <param name="action">The action to invoke for each observer.</param>
    public void NotifyObservers(Action<ISandboxObserver> action)
    {
        ISandboxObserver[] observers;
        lock (_observerLock)
        {
            if (_observers.Count == 0) return;
            observers = _observers.ToArray();
        }

        foreach (var observer in observers)
        {
            try
            {
                action(observer);
            }
            catch
            {
                // Don't let observer exceptions affect sandbox operation
            }
        }
    }

    /// <summary>
    /// Unsubscribes an observer from receiving events.
    /// </summary>
    /// <param name="observer">The observer to unsubscribe.</param>
    private void Unsubscribe(ISandboxObserver observer)
    {
        lock (_observerLock)
        {
            _observers.Remove(observer);
        }
    }

    /// <summary>
    /// Clears all subscribed observers.
    /// </summary>
    public void Clear()
    {
        lock (_observerLock)
        {
            _observers.Clear();
        }
    }

    private sealed class ObserverUnsubscriber : IDisposable
    {
        private readonly ObserverManager _manager;
        private readonly ISandboxObserver _observer;

        public ObserverUnsubscriber(ObserverManager manager, ISandboxObserver observer)
        {
            _manager = manager;
            _observer = observer;
        }

        public void Dispose() => _manager.Unsubscribe(_observer);
    }
}
