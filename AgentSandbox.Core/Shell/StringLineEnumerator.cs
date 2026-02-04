namespace AgentSandbox.Core.Shell;

/// <summary>
/// Extension methods for zero-allocation string line enumeration.
/// </summary>
public static class StringLineExtensions
{
    /// <summary>
    /// Enumerates lines in a string without allocating a string[] array.
    /// Each iteration yields a ReadOnlySpan into the original string.
    /// </summary>
    /// <param name="text">The text to enumerate lines from.</param>
    /// <returns>A line enumerator.</returns>
    public static LineEnumerator EnumerateLines(this string text) => new(text.AsSpan());

    /// <summary>
    /// Enumerates lines in a span without allocating a string[] array.
    /// </summary>
    public static LineEnumerator EnumerateLines(this ReadOnlySpan<char> text) => new(text);
}

/// <summary>
/// Represents a line with its 1-based line number and content span.
/// </summary>
public ref struct LineEntry
{
    /// <summary>1-based line number.</summary>
    public int LineNumber;
    
    /// <summary>The line content (without newline characters).</summary>
    public ReadOnlySpan<char> Line;
    
    /// <summary>Deconstructs the entry into line number and line content.</summary>
    public readonly void Deconstruct(out int lineNumber, out ReadOnlySpan<char> line)
    {
        lineNumber = LineNumber;
        line = Line;
    }
}

/// <summary>
/// A ref struct enumerator for iterating over lines in a string without allocation.
/// </summary>
public ref struct LineEnumerator
{
    private ReadOnlySpan<char> _remaining;
    private LineEntry _current;

    /// <summary>
    /// Creates a new line enumerator.
    /// </summary>
    public LineEnumerator(ReadOnlySpan<char> text)
    {
        _remaining = text;
        _current = default;
    }

    /// <summary>
    /// Gets the enumerator (for foreach support).
    /// </summary>
    public readonly LineEnumerator GetEnumerator() => this;

    /// <summary>
    /// Gets the current line entry.
    /// </summary>
    public readonly LineEntry Current => _current;

    /// <summary>
    /// Moves to the next line.
    /// </summary>
    /// <returns>True if there is another line, false if enumeration is complete.</returns>
    public bool MoveNext()
    {
        if (_remaining.IsEmpty && _current.LineNumber > 0)
            return false;

        // Handle empty string case - return one empty line
        if (_remaining.IsEmpty && _current.LineNumber == 0)
        {
            _current.LineNumber = 1;
            _current.Line = default;
            return true;
        }

        _current.LineNumber++;
        var idx = _remaining.IndexOf('\n');
        if (idx < 0)
        {
            _current.Line = _remaining;
            _remaining = default;
        }
        else
        {
            // Handle \r\n line endings
            _current.Line = idx > 0 && _remaining[idx - 1] == '\r'
                ? _remaining[..(idx - 1)]
                : _remaining[..idx];
            _remaining = _remaining[(idx + 1)..];
        }
        return true;
    }
}
