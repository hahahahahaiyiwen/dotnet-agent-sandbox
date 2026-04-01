using AgentSandbox.Core.Shell;
using System.Collections;

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
            Metadata = CloneMetadata(record.Metadata),
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

    private static IReadOnlyDictionary<string, object?>? CloneMetadata(IReadOnlyDictionary<string, object?>? source)
    {
        if (source is null)
        {
            return null;
        }

        var clone = new Dictionary<string, object?>(source.Count, StringComparer.Ordinal);
        foreach (var entry in source)
        {
            clone[entry.Key] = CloneMetadataValue(entry.Value);
        }

        return clone;
    }

    private static object? CloneMetadataValue(object? value)
    {
        if (value is null || value is string)
        {
            return value;
        }

        var valueType = value.GetType();
        if (valueType.IsValueType)
        {
            return value;
        }

        if (value is IReadOnlyDictionary<string, object?> readOnlyDictionary)
        {
            return CloneMetadata(readOnlyDictionary);
        }

        if (value is IDictionary<string, object?> dictionary)
        {
            var clonedDictionary = new Dictionary<string, object?>(dictionary.Count, StringComparer.Ordinal);
            foreach (var entry in dictionary)
            {
                clonedDictionary[entry.Key] = CloneMetadataValue(entry.Value);
            }

            return clonedDictionary;
        }

        if (value is IDictionary legacyDictionary)
        {
            var clonedDictionary = new Dictionary<string, object?>(legacyDictionary.Count, StringComparer.Ordinal);
            foreach (DictionaryEntry entry in legacyDictionary)
            {
                if (entry.Key is not string key)
                {
                    return value;
                }

                clonedDictionary[key] = CloneMetadataValue(entry.Value);
            }

            return clonedDictionary;
        }

        if (value is Array array)
        {
            var elementType = valueType.GetElementType() ?? typeof(object);
            var clonedArray = Array.CreateInstance(elementType, array.Length);
            for (var i = 0; i < array.Length; i++)
            {
                clonedArray.SetValue(CloneMetadataValue(array.GetValue(i)), i);
            }

            return clonedArray;
        }

        if (value is IList list)
        {
            var clonedList = new List<object?>(list.Count);
            foreach (var item in list)
            {
                clonedList.Add(CloneMetadataValue(item));
            }

            return clonedList;
        }

        if (value is IEnumerable enumerable)
        {
            var clonedSequence = new List<object?>();
            foreach (var item in enumerable)
            {
                clonedSequence.Add(CloneMetadataValue(item));
            }

            return clonedSequence;
        }

        if (value is ICloneable cloneable)
        {
            return cloneable.Clone();
        }

        return value;
    }
}
