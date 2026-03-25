using AgentSandbox.Core.Shell;

namespace AgentSandbox.Tests;

public class ShellLexerTests
{
    [Fact]
    public void TryTokenize_WhitespaceOnly_ReturnsNoTokens()
    {
        var success = ShellLexer.TryTokenize("   \t  ", out var tokens, out var error);

        Assert.True(success);
        Assert.Empty(tokens);
        Assert.True(error.Success);
    }

    [Fact]
    public void TryTokenize_QuotedEmptyTokens_ArePreserved()
    {
        var success = ShellLexer.TryTokenize("echo \"\" ''", out var tokens, out _);

        Assert.True(success);
        Assert.Equal(3, tokens.Count);
        Assert.Equal("echo", tokens[0].Value);
        Assert.Equal(string.Empty, tokens[1].Value);
        Assert.Equal(string.Empty, tokens[2].Value);
        Assert.True(tokens[1].WasQuoted);
        Assert.True(tokens[2].WasQuoted);
    }

    [Fact]
    public void TryTokenize_AndAndWithoutSpaces_TokenizesOperator()
    {
        var success = ShellLexer.TryTokenize("echo a&&echo b", out var tokens, out _);

        Assert.True(success);
        Assert.Equal(["echo", "a", "&&", "echo", "b"], tokens.Select(t => t.Value).ToArray());
        Assert.Equal(ShellTokenKind.Operator, tokens[2].Kind);
    }

    [Fact]
    public void TryTokenize_RedirectionOperatorsWithoutSpaces_TokenizesOperators()
    {
        var success = ShellLexer.TryTokenize("echo hi>/out >>/out2", out var tokens, out _);

        Assert.True(success);
        Assert.Equal(["echo", "hi", ">", "/out", ">>", "/out2"], tokens.Select(t => t.Value).ToArray());
        Assert.Equal(ShellTokenKind.Operator, tokens[2].Kind);
        Assert.Equal(ShellTokenKind.Operator, tokens[4].Kind);
    }

    [Fact]
    public void TryTokenize_EscapedSpace_OutsideQuotes_BecomesSingleWord()
    {
        var success = ShellLexer.TryTokenize("echo hello\\ world", out var tokens, out _);

        Assert.True(success);
        Assert.Equal(["echo", "hello world"], tokens.Select(t => t.Value).ToArray());
    }

    [Fact]
    public void TryTokenize_UnsupportedOperatorInsideQuotes_DoesNotTriggerDiagnostic()
    {
        var success = ShellLexer.TryTokenize("echo \"a | b\" 'c && d'", out var tokens, out var error);

        Assert.True(success);
        Assert.True(error.Success);
        Assert.Equal(["echo", "a | b", "c && d"], tokens.Select(t => t.Value).ToArray());
    }

    [Fact]
    public void TryTokenize_UnterminatedQuote_PreservesPartialTokenAsQuoted()
    {
        var success = ShellLexer.TryTokenize("echo \"unterminated", out var tokens, out var error);

        Assert.True(success);
        Assert.True(error.Success);
        Assert.Equal(["echo", "unterminated"], tokens.Select(t => t.Value).ToArray());
        Assert.True(tokens[1].WasQuoted);
    }

    [Fact]
    public void TryTokenize_QuotedThenUnquotedSegments_FlushesEmptyQuotedToken()
    {
        var success = ShellLexer.TryTokenize("\"\" next", out var tokens, out var error);

        Assert.True(success);
        Assert.True(error.Success);
        Assert.Equal([string.Empty, "next"], tokens.Select(t => t.Value).ToArray());
        Assert.True(tokens[0].WasQuoted);
        Assert.False(tokens[1].WasQuoted);
    }

    [Fact]
    public void TryTokenize_AndAndInsideQuotes_IsLiteralText()
    {
        var success = ShellLexer.TryTokenize("echo \"a && b\"", out var tokens, out var error);

        Assert.True(success);
        Assert.True(error.Success);
        Assert.Equal(["echo", "a && b"], tokens.Select(t => t.Value).ToArray());
        Assert.True(tokens[1].WasQuoted);
    }

    [Fact]
    public void TryTokenize_SemicolonWithoutSpaces_TokenizesOperator()
    {
        var success = ShellLexer.TryTokenize("echo a;echo b", out var tokens, out var error);

        Assert.True(success);
        Assert.True(error.Success);
        Assert.Equal(["echo", "a", ";", "echo", "b"], tokens.Select(t => t.Value).ToArray());
        Assert.Equal(ShellTokenKind.Operator, tokens[2].Kind);
    }

    [Fact]
    public void TryTokenize_InputRedirectionWithoutSpaces_ReturnsUnsupportedDiagnostic()
    {
        var success = ShellLexer.TryTokenize("cat<file.txt", out _, out var error);

        Assert.False(success);
        Assert.Contains("Input redirection (<) is not supported", error.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void TryTokenize_HeredocWithoutSpaces_ReturnsUnsupportedDiagnostic()
    {
        var success = ShellLexer.TryTokenize("cat<<EOF", out _, out var error);

        Assert.False(success);
        Assert.Contains("Heredoc (<<) is not supported", error.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void TryTokenize_CommandSubstitutionInsideQuotes_IsLiteralText()
    {
        var success = ShellLexer.TryTokenize("echo \"$(pwd)\" '`pwd`'", out var tokens, out var error);

        Assert.True(success);
        Assert.True(error.Success);
        Assert.Equal(["echo", "$(pwd)", "`pwd`"], tokens.Select(t => t.Value).ToArray());
        Assert.True(tokens[1].WasQuoted);
        Assert.True(tokens[2].WasQuoted);
    }

    [Fact]
    public void TryTokenize_QuoteAdjacentToOperator_PreservesQuotedFlag()
    {
        var success = ShellLexer.TryTokenize("echo \"value\">/out.txt", out var tokens, out var error);

        Assert.True(success);
        Assert.True(error.Success);
        Assert.Equal(["echo", "value", ">", "/out.txt"], tokens.Select(t => t.Value).ToArray());
        Assert.True(tokens[1].WasQuoted);
        Assert.Equal(ShellTokenKind.Operator, tokens[2].Kind);
    }

    [Theory]
    [InlineData("echo a | cat", "Pipelines are not supported")]
    [InlineData("echo a || echo b", "Command chaining (||) is not supported")]
    [InlineData("echo a &", "Background jobs (&) are not supported")]
    [InlineData("cat << EOF", "Heredoc (<<) is not supported")]
    [InlineData("cat < file.txt", "Input redirection (<) is not supported")]
    [InlineData("echo $(pwd)", "Command substitution is not supported")]
    [InlineData("echo `pwd`", "Command substitution is not supported")]
    public void TryTokenize_UnsupportedOperators_ReturnExpectedDiagnostic(string command, string expectedMessage)
    {
        var success = ShellLexer.TryTokenize(command, out _, out var error);

        Assert.False(success);
        Assert.False(error.Success);
        Assert.Contains(expectedMessage, error.Stderr, StringComparison.Ordinal);
    }
}
