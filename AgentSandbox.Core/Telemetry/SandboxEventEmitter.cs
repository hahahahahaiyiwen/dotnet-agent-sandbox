namespace AgentSandbox.Core.Telemetry;

internal sealed class SandboxEventEmitter(
    Action<Action<ISandboxObserver>> notifyObservers,
    Action<SandboxEvent>? onEmit = null) : ISandboxEventEmitter
{
    public void Emit(SandboxEvent sandboxEvent)
    {
        onEmit?.Invoke(sandboxEvent);

        notifyObservers(observer =>
        {
            observer.OnEvent(sandboxEvent);
            switch (sandboxEvent)
            {
                case CommandExecutedEvent command:
                    observer.OnCommandExecuted(command);
                    break;
                case FileChangedEvent fileChanged:
                    observer.OnFileChanged(fileChanged);
                    break;
                case SkillInvokedEvent skillInvoked:
                    observer.OnSkillInvoked(skillInvoked);
                    break;
                case SandboxLifecycleEvent lifecycle:
                    observer.OnLifecycleEvent(lifecycle);
                    break;
                case SandboxErrorEvent error:
                    observer.OnError(error);
                    break;
            }
        });
    }
}
