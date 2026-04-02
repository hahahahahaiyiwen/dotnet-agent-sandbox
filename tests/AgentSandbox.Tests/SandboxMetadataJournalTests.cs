using AgentSandbox.Core;
using AgentSandbox.Core.Capabilities;
using AgentSandbox.Core.Metadata;
using AgentSandbox.Core.Telemetry;
using System.Collections;

namespace AgentSandbox.Tests;

public class SandboxMetadataJournalTests
{
    [Fact]
    public void GetHistory_ProjectsOnlyShellOperations_InExecutionOrder()
    {
        var options = new SandboxOptions
        {
            Capabilities = [new JournalTestCapability()]
        };

        using var sandbox = new Sandbox(options: options);
        sandbox.Execute("echo first");
        sandbox.GetCapability<IJournalTestCapability>().EmitOperation();
        sandbox.Execute("pwd");

        var history = sandbox.GetHistory();

        Assert.Equal(2, history.Count);
        Assert.Equal("echo first", history[0].Command);
        Assert.Equal("pwd", history[1].Command);
    }

    [Fact]
    public void GetHistory_UsesJournalRetention_DropOldest()
    {
        var options = new SandboxOptions
        {
            Journal = new()
            {
                MaxEntries = 2,
                TruncationStrategy = SandboxOperationJournalTruncationStrategy.DropOldest
            }
        };

        using var sandbox = new Sandbox(options: options);
        sandbox.Execute("echo one");
        sandbox.Execute("echo two");
        sandbox.Execute("echo three");

        var history = sandbox.GetHistory();
        var stats = sandbox.GetStats();

        Assert.Equal(2, history.Count);
        Assert.Equal("echo two", history[0].Command);
        Assert.Equal("echo three", history[1].Command);
        Assert.Equal(2, stats.CommandCount);
    }

    [Fact]
    public void GetHistory_UsesJournalRetention_DropNewest()
    {
        var options = new SandboxOptions
        {
            Journal = new()
            {
                MaxEntries = 2,
                TruncationStrategy = SandboxOperationJournalTruncationStrategy.DropNewest
            }
        };

        using var sandbox = new Sandbox(options: options);
        sandbox.Execute("echo one");
        sandbox.Execute("echo two");
        sandbox.Execute("echo three");

        var history = sandbox.GetHistory();
        var stats = sandbox.GetStats();

        Assert.Equal(2, history.Count);
        Assert.Equal("echo one", history[0].Command);
        Assert.Equal("echo two", history[1].Command);
        Assert.Equal(2, stats.CommandCount);
    }

    [Fact]
    public void GetStats_IncludesCapabilityOperationCount_FromJournal()
    {
        var options = new SandboxOptions
        {
            Capabilities = [new JournalTestCapability()]
        };

        using var sandbox = new Sandbox(options: options);
        sandbox.Execute("echo one");
        var capability = sandbox.GetCapability<IJournalTestCapability>();
        capability.EmitOperation();
        capability.EmitOperation();

        var stats = sandbox.GetStats();

        Assert.Equal(1, stats.CommandCount);
        Assert.Equal(2, stats.CapabilityOperationCount);
    }

    [Fact]
    public void GetHistory_RemainsImmutable_WhenReturnedShellResultIsMutated()
    {
        using var sandbox = new Sandbox();
        sandbox.Execute("echo immutable");

        var history = sandbox.GetHistory();
        Assert.Single(history);
        history[0].Stdout = "tampered";
        history[0].Command = "tampered command";

        var refreshedHistory = sandbox.GetHistory();
        Assert.Single(refreshedHistory);
        Assert.NotEqual("tampered", refreshedHistory[0].Stdout);
        Assert.NotEqual("tampered command", refreshedHistory[0].Command);
    }

    [Fact]
    public void GetHistory_CapturesShellResultSnapshot_AtAppendTime()
    {
        using var sandbox = new Sandbox();
        var result = sandbox.Execute("echo snapshot");
        result.Stdout = "mutated outside";
        result.Command = "mutated command";

        var history = sandbox.GetHistory();

        Assert.Single(history);
        Assert.NotEqual("mutated outside", history[0].Stdout);
        Assert.NotEqual("mutated command", history[0].Command);
    }

    [Fact]
    public void JournalOptions_MaxEntries_Throws_WhenNotPositive()
    {
        var options = new SandboxOperationJournalOptions();
        Assert.Throws<ArgumentOutOfRangeException>(() => options.MaxEntries = 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => options.MaxEntries = -1);
    }

    [Fact]
    public void Journal_Append_ClonesNestedMetadataCollections()
    {
        var journal = new SandboxOperationJournal();
        var nestedList = new List<object?> { "alpha" };
        var nestedDictionary = new Dictionary<string, object?>
        {
            ["items"] = nestedList
        };
        var nestedArray = new object?[] { "first" };
        var metadata = new Dictionary<string, object?>
        {
            ["dict"] = nestedDictionary,
            ["array"] = nestedArray
        };

        journal.Append(new SandboxOperationRecord
        {
            Timestamp = DateTime.UtcNow,
            Category = "capability",
            Operation = "emit",
            Metadata = metadata
        });

        nestedList.Add("beta");
        nestedDictionary["new-key"] = "new-value";
        nestedArray[0] = "mutated";
        metadata["added"] = "outside";

        var storedRecord = GetSingleRecord(journal);
        var storedMetadata = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(storedRecord.Metadata);
        Assert.False(storedMetadata.ContainsKey("added"));

        var storedDict = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(storedMetadata["dict"]);
        Assert.False(storedDict.ContainsKey("new-key"));

        var storedList = Assert.IsAssignableFrom<IReadOnlyList<object?>>(storedDict["items"]);
        Assert.Equal(new object?[] { "alpha" }, storedList);

        var storedArray = Assert.IsType<object?[]>(storedMetadata["array"]);
        Assert.Equal("first", storedArray[0]);
    }

    [Fact]
    public void Journal_Append_ClonesMultiDimensionalArrayMetadata()
    {
        var journal = new SandboxOperationJournal();
        var nestedList = new List<object?> { "x" };
        var matrix = new object?[1, 2];
        matrix[0, 0] = nestedList;
        matrix[0, 1] = "stable";

        journal.Append(new SandboxOperationRecord
        {
            Timestamp = DateTime.UtcNow,
            Category = "capability",
            Operation = "emit",
            Metadata = new Dictionary<string, object?> { ["matrix"] = matrix }
        });

        nestedList.Add("y");
        matrix[0, 1] = "mutated";

        var storedRecord = GetSingleRecord(journal);
        var storedMetadata = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(storedRecord.Metadata);
        var storedMatrix = Assert.IsType<object?[,]>(storedMetadata["matrix"]);
        var storedList = Assert.IsAssignableFrom<IReadOnlyList<object?>>(storedMatrix[0, 0]);

        Assert.Equal(new object?[] { "x" }, storedList);
        Assert.Equal("stable", storedMatrix[0, 1]);
    }

    [Fact]
    public void Journal_Append_DoesNotEnumerateArbitrarySequences()
    {
        var journal = new SandboxOperationJournal();
        var sequence = new ThrowingEnumerable();
        var metadata = new Dictionary<string, object?> { ["sequence"] = sequence };

        journal.Append(new SandboxOperationRecord
        {
            Timestamp = DateTime.UtcNow,
            Category = "capability",
            Operation = "emit",
            Metadata = metadata
        });

        var storedRecord = GetSingleRecord(journal);
        var storedMetadata = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(storedRecord.Metadata);
        Assert.Same(sequence, storedMetadata["sequence"]);
    }

    private static SandboxOperationRecord GetSingleRecord(SandboxOperationJournal journal)
    {
        var records = journal.GetRecordsSnapshot();
        return Assert.Single(records);
    }

    private interface IJournalTestCapability
    {
        void EmitOperation();
    }

    private sealed class JournalTestCapability : ISandboxCapability, IJournalTestCapability
    {
        private ISandboxEventEmitter? _eventEmitter;
        public string Name => "journal-test";

        public void Initialize(ISandboxContext context)
        {
            _eventEmitter = context.EventEmitter;
        }

        public void EmitOperation()
        {
            _eventEmitter!.Emit(SandboxCapabilityEventHelper.CreateSuccessEvent(
                sandboxId: "journal-test",
                capabilityName: Name,
                operationType: "journal.test",
                operationName: "emit"));
        }
    }

    private sealed class ThrowingEnumerable : IEnumerable
    {
        public IEnumerator GetEnumerator() => throw new InvalidOperationException("Enumeration is not expected.");
    }
}
