using AgentSandbox.Core.Shell;

namespace AgentSandbox.Core.Metadata;

internal sealed class SandboxOperationJournal
{
    private readonly List<SandboxOperationRecord> _records = [];
    private readonly SandboxOperationJournalOptions _options;
    private readonly object _sync = new();

    public SandboxOperationJournal(SandboxOperationJournalOptions? options = null)
    {
        _options = options ?? new SandboxOperationJournalOptions();
    }

    public void Append(SandboxOperationRecord record)
    {
        lock (_sync)
        {
            if (_options.MaxEntries is int maxEntries && _records.Count >= maxEntries)
            {
                if (_options.TruncationStrategy == SandboxOperationJournalTruncationStrategy.DropNewest)
                {
                    return;
                }

                var removeCount = _records.Count - maxEntries + 1;
                if (removeCount > 0)
                {
                    _records.RemoveRange(0, removeCount);
                }
            }

            _records.Add(record);
        }
    }

    public IReadOnlyList<ShellResult> GetCommandHistoryProjection()
    {
        lock (_sync)
        {
            return _records
                .Where(static r => r.Category == "shell" && r.ShellResult is not null)
                .Select(r => CloneShellResult(r.ShellResult!))
                .ToList();
        }
    }

    public int CountByCategory(string category)
    {
        lock (_sync)
        {
            return _records.Count(r => string.Equals(r.Category, category, StringComparison.Ordinal));
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _records.Clear();
        }
    }

    private static ShellResult CloneShellResult(ShellResult result) => new()
    {
        Command = result.Command,
        Stdout = result.Stdout,
        Stderr = result.Stderr,
        ExitCode = result.ExitCode,
        Duration = result.Duration
    };
}
