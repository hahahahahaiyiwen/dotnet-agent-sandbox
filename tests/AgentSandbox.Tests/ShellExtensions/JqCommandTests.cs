using AgentSandbox.Core.FileSystem;
using AgentSandbox.Core.Shell;
using AgentSandbox.Core.Shell.Extensions;

namespace AgentSandbox.Tests.ShellExtensions;

public class JqCommandTests
{
    private readonly FileSystem _fs;
    private readonly SandboxShell _shell;

    public JqCommandTests()
    {
        _fs = new FileSystem();
        _shell = new SandboxShell(_fs);
        _shell.RegisterCommand(new JqCommand());
    }

    #region Basic Tests

    [Fact]
    public void Jq_WithoutFilter_ReturnsError()
    {
        var result = _shell.Execute("jq");
        
        Assert.False(result.Success);
        Assert.Contains("missing filter", result.Stderr);
    }

    [Fact]
    public void Jq_Identity_ReturnsInput()
    {
        _fs.WriteFile("/data.json", """{"name":"test"}""");
        
        var result = _shell.Execute("jq '.' /data.json");
        
        Assert.True(result.Success);
        Assert.Contains("name", result.Stdout);
        Assert.Contains("test", result.Stdout);
    }

    [Fact]
    public void Jq_FileNotFound_ReturnsError()
    {
        var result = _shell.Execute("jq '.' /missing.json");
        
        Assert.False(result.Success);
        Assert.Contains("No such file", result.Stderr);
    }

    #endregion

    #region Field Access Tests

    [Fact]
    public void Jq_SimpleFieldAccess_ReturnsValue()
    {
        _fs.WriteFile("/data.json", """{"name":"Alice","age":30}""");
        
        var result = _shell.Execute("jq '.name' /data.json");
        
        Assert.True(result.Success);
        Assert.Contains("Alice", result.Stdout);
    }

    [Fact]
    public void Jq_NestedFieldAccess_ReturnsValue()
    {
        _fs.WriteFile("/data.json", """{"user":{"name":"Bob","email":"bob@test.com"}}""");
        
        var result = _shell.Execute("jq '.user.name' /data.json");
        
        Assert.True(result.Success);
        Assert.Contains("Bob", result.Stdout);
    }

    [Fact]
    public void Jq_OptionalField_NoErrorOnMissing()
    {
        _fs.WriteFile("/data.json", """{"name":"test"}""");
        
        var result = _shell.Execute("jq '.missing?' /data.json");
        
        Assert.True(result.Success);
    }

    #endregion

    #region Array Access Tests

    [Fact]
    public void Jq_ArrayIndex_ReturnsElement()
    {
        _fs.WriteFile("/data.json", """[1,2,3,4,5]""");
        
        var result = _shell.Execute("jq '.[0]' /data.json");
        
        Assert.True(result.Success);
        Assert.Equal("1", result.Stdout.Trim());
    }

    [Fact]
    public void Jq_ArrayIterator_ReturnsAllElements()
    {
        _fs.WriteFile("/data.json", """[1,2,3]""");
        
        var result = _shell.Execute("jq '.[]' /data.json");
        
        Assert.True(result.Success);
        Assert.Contains("1", result.Stdout);
        Assert.Contains("2", result.Stdout);
        Assert.Contains("3", result.Stdout);
    }

    [Fact]
    public void Jq_ArraySlice_ReturnsSubset()
    {
        _fs.WriteFile("/data.json", """[1,2,3,4,5]""");
        
        var result = _shell.Execute("jq '.[1:3]' /data.json");
        
        Assert.True(result.Success);
        Assert.Contains("2", result.Stdout);
        Assert.Contains("3", result.Stdout);
        Assert.DoesNotContain("1", result.Stdout);
        Assert.DoesNotContain("4", result.Stdout);
    }

    [Fact]
    public void Jq_NegativeIndex_ReturnsFromEnd()
    {
        _fs.WriteFile("/data.json", """[1,2,3,4,5]""");
        
        var result = _shell.Execute("jq '.[-1]' /data.json");
        
        Assert.True(result.Success);
        Assert.Equal("5", result.Stdout.Trim());
    }

    #endregion

    #region Pipe Tests

    [Fact]
    public void Jq_Pipe_ChainsFilters()
    {
        _fs.WriteFile("/data.json", """{"users":[{"name":"Alice"},{"name":"Bob"}]}""");
        
        var result = _shell.Execute("jq '.users | .[0] | .name' /data.json");
        
        Assert.True(result.Success);
        Assert.Contains("Alice", result.Stdout);
    }

    [Fact]
    public void Jq_PipeWithIterator_ProcessesEach()
    {
        _fs.WriteFile("/data.json", """{"items":[{"id":1},{"id":2}]}""");
        
        var result = _shell.Execute("jq '.items[] | .id' /data.json");
        
        Assert.True(result.Success);
        Assert.Contains("1", result.Stdout);
        Assert.Contains("2", result.Stdout);
    }

    #endregion

    #region Select Filter Tests

    [Fact]
    public void Jq_SelectEquals_FiltersMatching()
    {
        _fs.WriteFile("/data.json", """[{"name":"Alice","active":true},{"name":"Bob","active":false}]""");
        
        var result = _shell.Execute("jq '.[] | select(.active == true)' /data.json");
        
        Assert.True(result.Success);
        Assert.Contains("Alice", result.Stdout);
        Assert.DoesNotContain("Bob", result.Stdout);
    }

    [Fact]
    public void Jq_SelectGreaterThan_FiltersMatching()
    {
        _fs.WriteFile("/data.json", """[{"price":5},{"price":15},{"price":25}]""");
        
        var result = _shell.Execute("jq '.[] | select(.price > 10)' /data.json");
        
        Assert.True(result.Success, $"Expected success but got error: {result.Stderr}");
        Assert.Contains("15", result.Stdout);
        Assert.Contains("25", result.Stdout);
    }

    #endregion

    #region Map Filter Tests

    [Fact]
    public void Jq_Map_TransformsElements()
    {
        _fs.WriteFile("/data.json", """[{"name":"Alice"},{"name":"Bob"}]""");
        
        var result = _shell.Execute("jq 'map(.name)' /data.json");
        
        Assert.True(result.Success);
        Assert.Contains("Alice", result.Stdout);
        Assert.Contains("Bob", result.Stdout);
    }

    #endregion

    #region Built-in Functions Tests

    [Fact]
    public void Jq_Keys_ReturnsObjectKeys()
    {
        _fs.WriteFile("/data.json", """{"b":2,"a":1,"c":3}""");
        
        var result = _shell.Execute("jq 'keys' /data.json");
        
        Assert.True(result.Success);
        Assert.Contains("a", result.Stdout);
        Assert.Contains("b", result.Stdout);
        Assert.Contains("c", result.Stdout);
    }

    [Fact]
    public void Jq_Length_ReturnsCount()
    {
        _fs.WriteFile("/data.json", """[1,2,3,4,5]""");
        
        var result = _shell.Execute("jq 'length' /data.json");
        
        Assert.True(result.Success);
        Assert.Equal("5", result.Stdout.Trim());
    }

    [Fact]
    public void Jq_Type_ReturnsTypeName()
    {
        _fs.WriteFile("/data.json", """{"name":"test"}""");
        
        var result = _shell.Execute("jq 'type' /data.json");
        
        Assert.True(result.Success);
        Assert.Contains("object", result.Stdout);
    }

    [Fact]
    public void Jq_First_ReturnsFirstElement()
    {
        _fs.WriteFile("/data.json", """[10,20,30]""");
        
        var result = _shell.Execute("jq 'first' /data.json");
        
        Assert.True(result.Success);
        Assert.Equal("10", result.Stdout.Trim());
    }

    [Fact]
    public void Jq_Last_ReturnsLastElement()
    {
        _fs.WriteFile("/data.json", """[10,20,30]""");
        
        var result = _shell.Execute("jq 'last' /data.json");
        
        Assert.True(result.Success);
        Assert.Equal("30", result.Stdout.Trim());
    }

    [Fact]
    public void Jq_Sort_SortsArray()
    {
        _fs.WriteFile("/data.json", """[3,1,2]""");
        
        var result = _shell.Execute("jq 'sort' /data.json");
        
        Assert.True(result.Success);
        // Result should be sorted
        var idx1 = result.Stdout.IndexOf("1");
        var idx2 = result.Stdout.IndexOf("2");
        var idx3 = result.Stdout.IndexOf("3");
        Assert.True(idx1 < idx2 && idx2 < idx3);
    }

    [Fact]
    public void Jq_Reverse_ReversesArray()
    {
        _fs.WriteFile("/data.json", """[1,2,3]""");
        
        var result = _shell.Execute("jq 'reverse' /data.json");
        
        Assert.True(result.Success);
        var idx1 = result.Stdout.IndexOf("1");
        var idx3 = result.Stdout.IndexOf("3");
        Assert.True(idx3 < idx1);
    }

    [Fact]
    public void Jq_Unique_RemovesDuplicates()
    {
        _fs.WriteFile("/data.json", """[1,2,2,3,3,3]""");
        
        var result = _shell.Execute("jq 'unique' /data.json");
        
        Assert.True(result.Success);
        Assert.Equal(1, CountOccurrences(result.Stdout, "1"));
        Assert.Equal(1, CountOccurrences(result.Stdout, "2"));
        Assert.Equal(1, CountOccurrences(result.Stdout, "3"));
    }

    [Fact]
    public void Jq_Add_SumsNumbers()
    {
        _fs.WriteFile("/data.json", """[1,2,3,4]""");
        
        var result = _shell.Execute("jq 'add' /data.json");
        
        Assert.True(result.Success);
        Assert.Equal("10", result.Stdout.Trim());
    }

    [Fact]
    public void Jq_Flatten_FlattensNestedArrays()
    {
        _fs.WriteFile("/data.json", """[[1,2],[3,[4,5]]]""");
        
        var result = _shell.Execute("jq 'flatten' /data.json");
        
        Assert.True(result.Success);
        Assert.Contains("1", result.Stdout);
        Assert.Contains("5", result.Stdout);
    }

    #endregion

    #region Options Tests

    [Fact]
    public void Jq_RawOutput_RemovesQuotes()
    {
        _fs.WriteFile("/data.json", """{"name":"Alice"}""");
        
        var result = _shell.Execute("jq -r '.name' /data.json");
        
        Assert.True(result.Success);
        Assert.Equal("Alice", result.Stdout.Trim());
        Assert.DoesNotContain("\"", result.Stdout);
    }

    [Fact]
    public void Jq_Compact_NoIndentation()
    {
        _fs.WriteFile("/data.json", """{"name":"test","value":123}""");
        
        var result = _shell.Execute("jq -c '.' /data.json");
        
        Assert.True(result.Success);
        Assert.DoesNotContain("\n", result.Stdout.Trim());
    }

    #endregion

    #region Construction Tests

    [Fact]
    public void Jq_ArrayConstruction_CreatesArray()
    {
        _fs.WriteFile("/data.json", """{"a":1,"b":2}""");
        
        var result = _shell.Execute("jq '[.a, .b]' /data.json");
        
        Assert.True(result.Success);
        Assert.Contains("1", result.Stdout);
        Assert.Contains("2", result.Stdout);
    }

    [Fact]
    public void Jq_ObjectConstruction_CreatesObject()
    {
        _fs.WriteFile("/data.json", """{"firstName":"John","lastName":"Doe"}""");
        
        var result = _shell.Execute("jq '{name: .firstName}' /data.json");
        
        Assert.True(result.Success);
        Assert.Contains("name", result.Stdout);
        Assert.Contains("John", result.Stdout);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void Jq_NullInput_HandlesNull()
    {
        _fs.WriteFile("/data.json", "null");
        
        var result = _shell.Execute("jq '.' /data.json");
        
        Assert.True(result.Success);
        Assert.Equal("null", result.Stdout.Trim());
    }

    [Fact]
    public void Jq_EmptyArray_HandlesEmpty()
    {
        _fs.WriteFile("/data.json", "[]");
        
        var result = _shell.Execute("jq 'length' /data.json");
        
        Assert.True(result.Success);
        Assert.Equal("0", result.Stdout.Trim());
    }

    [Fact]
    public void Jq_InvalidJson_ReturnsError()
    {
        _fs.WriteFile("/data.json", "not valid json");
        
        var result = _shell.Execute("jq '.' /data.json");
        
        Assert.False(result.Success);
        Assert.Contains("parse error", result.Stderr);
    }

    #endregion

    private int CountOccurrences(string text, string pattern)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(pattern, index)) != -1)
        {
            count++;
            index += pattern.Length;
        }
        return count;
    }
}
