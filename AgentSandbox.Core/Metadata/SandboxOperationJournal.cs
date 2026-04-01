using AgentSandbox.Core.Shell;

namespace AgentSandbox.Core.Metadata;

internal sealed class SandboxOperationJournal
{
    private readonly List<SandboxOperationRecord> _records = [];
    private readonly SandboxOperationJournalOptions _options;

    public SandboxOperationJournal(SandboxOperationJournalOptions? options = null)
    {
        _options = options ?? new SandboxOperationJournalOptions();
    }

    public void Append(SandboxOperationRecord record)
    {
        var storedRecord = record with
        {
            Metadata = record.Metadata is null ? null : new Dictionary<string, object?>(record.Metadata),
            ShellResult = record.ShellResult is null ? null : CloneShellResult(record.ShellResult)
        };

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

        _records.Add(storedRecord);
    }

    public IReadOnlyList<ShellResult> GetCommandHistoryProjection()
    {
        return _records
            .Where(static r => r.Category == "shell" && r.ShellResult is not null)
            .Select(r => CloneShellResult(r.ShellResult!))
            .ToList();
    }

    public int CountByCategory(string category)
    {
        return _records.Count(r => string.Equals(r.Category, category, StringComparison.Ordinal));
    }

    public void Clear() => _records.Clear();

    private static ShellResult CloneShellResult(ShellResult source)
    {
        return new ShellResult
        {
            Stdout = source.Stdout,
            Stderr = source.Stderr,
            ExitCode = source.ExitCode,
            Command = source.Command,
            Duration = source.Duration
        };
    }
}
