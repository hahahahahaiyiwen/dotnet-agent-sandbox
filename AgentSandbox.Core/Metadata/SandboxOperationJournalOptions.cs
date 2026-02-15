namespace AgentSandbox.Core.Metadata;

public enum SandboxOperationJournalTruncationStrategy
{
    DropOldest,
    DropNewest
}

public class SandboxOperationJournalOptions
{
    private int? _maxEntries;

    public int? MaxEntries
    {
        get => _maxEntries;
        set
        {
            if (value is <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(MaxEntries), value, "MaxEntries must be greater than zero.");
            }

            _maxEntries = value;
        }
    }

    public SandboxOperationJournalTruncationStrategy TruncationStrategy { get; set; } =
        SandboxOperationJournalTruncationStrategy.DropOldest;
}
