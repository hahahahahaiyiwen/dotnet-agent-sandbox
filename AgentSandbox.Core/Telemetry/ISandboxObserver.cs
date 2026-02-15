namespace AgentSandbox.Core.Telemetry;

/// <summary>
/// Observer interface for receiving sandbox events in real-time.
/// Implement this interface to subscribe to sandbox telemetry events.
/// </summary>
public interface ISandboxObserver
{
    /// <summary>
    /// Called when any sandbox event is emitted.
    /// </summary>
    void OnEvent(SandboxEvent e) { }

    /// <summary>
    /// Called when a command is executed.
    /// </summary>
    void OnCommandExecuted(CommandExecutedEvent e);

    /// <summary>
    /// Called when a filesystem change occurs.
    /// </summary>
    void OnFileChanged(FileChangedEvent e);

    /// <summary>
    /// Called when a skill is invoked.
    /// </summary>
    void OnSkillInvoked(SkillInvokedEvent e);

    /// <summary>
    /// Called when a sandbox lifecycle event occurs.
    /// </summary>
    void OnLifecycleEvent(SandboxLifecycleEvent e);

    /// <summary>
    /// Called when an error occurs.
    /// </summary>
    void OnError(SandboxErrorEvent e);
}

/// <summary>
/// Interface for sandboxes that support event observation.
/// </summary>
public interface IObservableSandbox
{
    /// <summary>
    /// Subscribes an observer to receive sandbox events.
    /// </summary>
    /// <param name="observer">The observer to subscribe.</param>
    /// <returns>A disposable that unsubscribes the observer when disposed.</returns>
    IDisposable Subscribe(ISandboxObserver observer);
}

/// <summary>
/// Base implementation of ISandboxObserver with virtual methods.
/// Inherit from this class and override only the methods you need.
/// </summary>
public abstract class SandboxObserverBase : ISandboxObserver
{
    public virtual void OnEvent(SandboxEvent e) { }
    public virtual void OnCommandExecuted(CommandExecutedEvent e) { }
    public virtual void OnFileChanged(FileChangedEvent e) { }
    public virtual void OnSkillInvoked(SkillInvokedEvent e) { }
    public virtual void OnLifecycleEvent(SandboxLifecycleEvent e) { }
    public virtual void OnError(SandboxErrorEvent e) { }
}

/// <summary>
/// Delegate-based observer for simple scenarios.
/// </summary>
public class DelegateSandboxObserver : ISandboxObserver
{
    private readonly Action<CommandExecutedEvent>? _onCommandExecuted;
    private readonly Action<FileChangedEvent>? _onFileChanged;
    private readonly Action<SkillInvokedEvent>? _onSkillInvoked;
    private readonly Action<SandboxLifecycleEvent>? _onLifecycleEvent;
    private readonly Action<SandboxErrorEvent>? _onError;
    private readonly Action<SandboxEvent>? _onEvent;

    public DelegateSandboxObserver(
        Action<SandboxEvent>? onEvent = null,
        Action<CommandExecutedEvent>? onCommandExecuted = null,
        Action<FileChangedEvent>? onFileChanged = null,
        Action<SkillInvokedEvent>? onSkillInvoked = null,
        Action<SandboxLifecycleEvent>? onLifecycleEvent = null,
        Action<SandboxErrorEvent>? onError = null)
    {
        _onEvent = onEvent;
        _onCommandExecuted = onCommandExecuted;
        _onFileChanged = onFileChanged;
        _onSkillInvoked = onSkillInvoked;
        _onLifecycleEvent = onLifecycleEvent;
        _onError = onError;
    }

    public void OnEvent(SandboxEvent e) => _onEvent?.Invoke(e);
    public void OnCommandExecuted(CommandExecutedEvent e) => _onCommandExecuted?.Invoke(e);
    public void OnFileChanged(FileChangedEvent e) => _onFileChanged?.Invoke(e);
    public void OnSkillInvoked(SkillInvokedEvent e) => _onSkillInvoked?.Invoke(e);
    public void OnLifecycleEvent(SandboxLifecycleEvent e) => _onLifecycleEvent?.Invoke(e);
    public void OnError(SandboxErrorEvent e) => _onError?.Invoke(e);
}
