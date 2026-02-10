using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgentSandbox.Core.Shell.Extensions;

/// <summary>
/// jq command implementation for JSON processing.
/// Supports common jq syntax: ., .field, .[], .[n], .field.nested, pipes, and filters.
/// </summary>
public class JqCommand : IShellCommand
{
    public string Name => "jq";
    public IReadOnlyList<string> Aliases => Array.Empty<string>();
    public string Description => "Command-line JSON processor";
    public string Usage => "jq [options] <filter> [file]\nRun 'jq help' for available options.";

    private static string HelpText => """
        jq - Command-line JSON processor

        Usage: jq [options] <filter> [file]
          filter              jq filter expression
          file                Input JSON file (optional, reads from stdin/pipe if omitted)
        
        Options:
          -r, --raw-output    Output raw strings without quotes
          -c, --compact       Compact output (no pretty printing)
          -e, --exit-status   Set exit status based on output
          -s, --slurp         Read entire input into array
          -n, --null-input    Don't read input, useful with --argjson
          -h, --help          Show this help message
        
        Filter Syntax:
          .                   Identity (output input unchanged)
          .foo                Get field 'foo'
          .foo.bar            Get nested field
          .foo?               Optional field (no error if missing)
          .[0]                Get array element at index
          .[]                 Iterate array elements
          .[0:3]              Array slice
          .[] | .name         Pipe filters together
          select(expr)        Filter elements where expr is true
          keys                Get object keys
          values              Get object values
          length              Get length of array/string/object
          type                Get JSON type
          map(expr)           Apply expr to each element
          first, last         Get first/last element
          add                 Sum array elements
          sort                Sort array
          reverse             Reverse array
          unique              Remove duplicates
          flatten             Flatten nested arrays
          group_by(.field)    Group by field
          [.foo, .bar]        Construct array
          {name: .foo}        Construct object
        
        Examples:
          jq '.' data.json
          jq '.name' data.json
          jq '.users[0].email' data.json
          jq '.items[] | select(.price > 10)' data.json
          jq -r '.name' data.json
        """;

    public ShellResult Execute(string[] args, IShellContext context)
    {
        // Handle help
        if (args.Length > 0 && (args[0] == "--help" || args[0] == "-h" || args[0] == "help"))
        {
            return ShellResult.Ok(HelpText);
        }

        if (args.Length == 0)
        {
            return ShellResult.Error($"jq: missing filter\n{Usage}");
        }

        var options = ParseOptions(args, out var filter, out var inputFile);

        if (string.IsNullOrEmpty(filter))
        {
            return ShellResult.Error("jq: missing filter expression");
        }

        string jsonInput;

        if (options.NullInput)
        {
            jsonInput = "null";
        }
        else if (!string.IsNullOrEmpty(inputFile))
        {
            var path = context.ResolvePath(inputFile);
            if (!context.FileSystem.Exists(path))
            {
                return ShellResult.Error($"jq: {inputFile}: No such file");
            }
            var jsonBytes = context.FileSystem.ReadFileBytes(path);
            jsonInput = System.Text.Encoding.UTF8.GetString(jsonBytes);
        }
        else
        {
            // Check for piped input via environment variable (set by shell piping)
            if (context.Environment.TryGetValue("__PIPE_INPUT__", out var pipeInput))
            {
                jsonInput = pipeInput;
            }
            else
            {
                return ShellResult.Error("jq: no input provided (specify file or pipe input)");
            }
        }

        try
        {
            JsonNode? input;
            
            if (options.Slurp)
            {
                // Parse each line as JSON and collect into array
                var lines = jsonInput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                var array = new JsonArray();
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                    {
                        array.Add(JsonNode.Parse(trimmed));
                    }
                }
                input = array;
            }
            else
            {
                input = JsonNode.Parse(jsonInput);
            }

            var results = ApplyFilter(input, filter);
            var output = FormatOutput(results, options);

            if (options.ExitStatus && (results.Count == 0 || (results.Count == 1 && results[0] == null)))
            {
                return ShellResult.Error("", 1);
            }

            return ShellResult.Ok(output);
        }
        catch (JsonException ex)
        {
            return ShellResult.Error($"jq: parse error: {ex.Message}");
        }
        catch (JqException ex)
        {
            return ShellResult.Error($"jq: {ex.Message}");
        }
        catch (Exception ex)
        {
            return ShellResult.Error($"jq: {ex.Message}");
        }
    }

    private List<JsonNode?> ApplyFilter(JsonNode? input, string filter)
    {
        filter = filter.Trim();

        // Handle pipe operator - split and chain filters
        if (ContainsPipeOutsideStrings(filter))
        {
            var parts = SplitByPipe(filter);
            var current = new List<JsonNode?> { input };

            foreach (var part in parts)
            {
                var next = new List<JsonNode?>();
                foreach (var item in current)
                {
                    next.AddRange(ApplyFilter(item, part.Trim()));
                }
                current = next;
            }
            return current;
        }

        // Identity
        if (filter == ".")
        {
            return new List<JsonNode?> { input?.DeepClone() };
        }

        // Array/Object construction: [expr] or {key: expr}
        // Array construction contains comma-separated expressions like [.a, .b]
        // Array index access starts with [. like .[0] or .[]
        if (filter.StartsWith('[') && filter.EndsWith(']'))
        {
            var inner = filter[1..^1].Trim();
            // If it contains a comma at the top level, it's array construction
            if (ContainsTopLevelComma(inner))
            {
                return ApplyArrayConstruction(input, filter);
            }
            // If it doesn't start with a number, negative sign, or colon (for slicing), treat as construction
            if (!string.IsNullOrEmpty(inner) && !char.IsDigit(inner[0]) && inner[0] != '-' && !inner.Contains(':'))
            {
                return ApplyArrayConstruction(input, filter);
            }
        }

        if (filter.StartsWith('{') && filter.EndsWith('}'))
        {
            return ApplyObjectConstruction(input, filter);
        }

        // Built-in functions
        if (filter == "keys")
        {
            return ApplyKeys(input);
        }
        if (filter == "values")
        {
            return ApplyValues(input);
        }
        if (filter == "length")
        {
            return ApplyLength(input);
        }
        if (filter == "type")
        {
            return ApplyType(input);
        }
        if (filter == "first")
        {
            return ApplyFirst(input);
        }
        if (filter == "last")
        {
            return ApplyLast(input);
        }
        if (filter == "add")
        {
            return ApplyAdd(input);
        }
        if (filter == "sort")
        {
            return ApplySort(input);
        }
        if (filter == "reverse")
        {
            return ApplyReverse(input);
        }
        if (filter == "unique")
        {
            return ApplyUnique(input);
        }
        if (filter == "flatten")
        {
            return ApplyFlatten(input);
        }
        if (filter.StartsWith("sort_by(") && filter.EndsWith(")"))
        {
            var expr = filter[8..^1];
            return ApplySortBy(input, expr);
        }
        if (filter.StartsWith("group_by(") && filter.EndsWith(")"))
        {
            var expr = filter[9..^1];
            return ApplyGroupBy(input, expr);
        }
        if (filter.StartsWith("map(") && filter.EndsWith(")"))
        {
            var expr = filter[4..^1];
            return ApplyMap(input, expr);
        }
        if (filter.StartsWith("select(") && filter.EndsWith(")"))
        {
            var expr = filter[7..^1];
            return ApplySelect(input, expr);
        }

        // Field access: .foo or .foo.bar or .foo?
        if (filter.StartsWith('.'))
        {
            return ApplyFieldAccess(input, filter);
        }

        // Literal values
        if (filter == "null") return new List<JsonNode?> { null };
        if (filter == "true") return new List<JsonNode?> { JsonValue.Create(true) };
        if (filter == "false") return new List<JsonNode?> { JsonValue.Create(false) };
        if (int.TryParse(filter, out var intVal)) return new List<JsonNode?> { JsonValue.Create(intVal) };
        if (double.TryParse(filter, out var dblVal)) return new List<JsonNode?> { JsonValue.Create(dblVal) };
        if (filter.StartsWith('"') && filter.EndsWith('"'))
        {
            return new List<JsonNode?> { JsonValue.Create(filter[1..^1]) };
        }

        throw new JqException($"unknown filter: {filter}");
    }

    private List<JsonNode?> ApplyFieldAccess(JsonNode? input, string filter)
    {
        var optional = filter.EndsWith('?');
        if (optional) filter = filter[..^1];

        var path = filter[1..]; // Remove leading dot

        // Handle .[] iterator
        if (path == "[]")
        {
            return IterateArray(input, optional);
        }

        // Handle .[n] or .[n:m] indexing
        if (path.StartsWith('['))
        {
            return ApplyIndexAccess(input, path, optional);
        }

        // Handle nested paths like .foo.bar or .foo[0].bar
        var results = new List<JsonNode?> { input };

        var segments = ParsePathSegments(path);
        foreach (var segment in segments)
        {
            var next = new List<JsonNode?>();
            foreach (var item in results)
            {
                if (segment == "[]")
                {
                    next.AddRange(IterateArray(item, optional));
                }
                else if (segment.StartsWith('[') && segment.EndsWith(']'))
                {
                    next.AddRange(ApplyIndexAccess(item, segment, optional));
                }
                else
                {
                    var segOptional = segment.EndsWith('?');
                    var segName = segOptional ? segment[..^1] : segment;
                    
                    if (item is JsonObject obj)
                    {
                        if (obj.TryGetPropertyValue(segName, out var val))
                        {
                            next.Add(val?.DeepClone());
                        }
                        else if (!optional && !segOptional)
                        {
                            next.Add(null);
                        }
                    }
                    else if (!optional && !segOptional)
                    {
                        throw new JqException($"Cannot index {GetTypeName(item)} with string \"{segName}\"");
                    }
                }
            }
            results = next;
        }

        return results;
    }

    private List<string> ParsePathSegments(string path)
    {
        var segments = new List<string>();
        var current = "";
        var bracketDepth = 0;

        for (int i = 0; i < path.Length; i++)
        {
            var c = path[i];

            if (c == '[')
            {
                if (bracketDepth == 0 && current.Length > 0)
                {
                    segments.Add(current);
                    current = "";
                }
                bracketDepth++;
                current += c;
            }
            else if (c == ']')
            {
                current += c;
                bracketDepth--;
                if (bracketDepth == 0)
                {
                    segments.Add(current);
                    current = "";
                }
            }
            else if (c == '.' && bracketDepth == 0)
            {
                if (current.Length > 0)
                {
                    segments.Add(current);
                    current = "";
                }
            }
            else
            {
                current += c;
            }
        }

        if (current.Length > 0)
        {
            segments.Add(current);
        }

        return segments;
    }

    private List<JsonNode?> IterateArray(JsonNode? input, bool optional)
    {
        if (input is JsonArray arr)
        {
            return arr.Select(x => x?.DeepClone()).ToList();
        }
        if (input is JsonObject obj)
        {
            return obj.Select(x => x.Value?.DeepClone()).ToList();
        }
        if (optional) return new List<JsonNode?>();
        throw new JqException($"Cannot iterate over {GetTypeName(input)}");
    }

    private List<JsonNode?> ApplyIndexAccess(JsonNode? input, string indexExpr, bool optional)
    {
        // Remove brackets
        var inner = indexExpr[1..^1];

        // Slice: [n:m]
        if (inner.Contains(':'))
        {
            var parts = inner.Split(':');
            var start = string.IsNullOrEmpty(parts[0]) ? 0 : int.Parse(parts[0]);
            
            if (input is JsonArray arr)
            {
                var end = string.IsNullOrEmpty(parts[1]) ? arr.Count : int.Parse(parts[1]);
                if (start < 0) start = arr.Count + start;
                if (end < 0) end = arr.Count + end;
                
                var result = new JsonArray();
                for (int i = Math.Max(0, start); i < Math.Min(arr.Count, end); i++)
                {
                    result.Add(arr[i]?.DeepClone());
                }
                return new List<JsonNode?> { result };
            }
            if (optional) return new List<JsonNode?>();
            throw new JqException($"Cannot slice {GetTypeName(input)}");
        }

        // Single index: [n]
        var index = int.Parse(inner);
        if (input is JsonArray array)
        {
            if (index < 0) index = array.Count + index;
            if (index >= 0 && index < array.Count)
            {
                return new List<JsonNode?> { array[index]?.DeepClone() };
            }
            return new List<JsonNode?> { null };
        }
        if (optional) return new List<JsonNode?>();
        throw new JqException($"Cannot index {GetTypeName(input)} with number");
    }

    private List<JsonNode?> ApplySelect(JsonNode? input, string expr)
    {
        var condition = EvaluateCondition(input, expr);
        return condition ? new List<JsonNode?> { input?.DeepClone() } : new List<JsonNode?>();
    }

    private bool EvaluateCondition(JsonNode? input, string expr)
    {
        expr = expr.Trim();

        // Comparison operators - check in order of length (longer first to avoid partial matches)
        foreach (var op in new[] { "==", "!=", ">=", "<=", ">", "<" })
        {
            var opIndex = FindOperatorIndex(expr, op);
            if (opIndex > 0)
            {
                var left = expr[..opIndex].Trim();
                var right = expr[(opIndex + op.Length)..].Trim();
                
                var leftVal = ApplyFilter(input, left).FirstOrDefault();
                var rightVal = ApplyFilter(input, right).FirstOrDefault();

                return op switch
                {
                    "==" => JsonNodesEqual(leftVal, rightVal),
                    "!=" => !JsonNodesEqual(leftVal, rightVal),
                    ">" => CompareNodes(leftVal, rightVal) > 0,
                    "<" => CompareNodes(leftVal, rightVal) < 0,
                    ">=" => CompareNodes(leftVal, rightVal) >= 0,
                    "<=" => CompareNodes(leftVal, rightVal) <= 0,
                    _ => false
                };
            }
        }

        // Boolean functions
        if (expr.StartsWith("has(") && expr.EndsWith(")"))
        {
            var field = expr[4..^1].Trim().Trim('"');
            return input is JsonObject obj && obj.ContainsKey(field);
        }

        // Evaluate as truthy
        var result = ApplyFilter(input, expr).FirstOrDefault();
        return IsTruthy(result);
    }

    private int FindOperatorIndex(string expr, string op)
    {
        var inString = false;
        var parenDepth = 0;
        var bracketDepth = 0;

        for (int i = 0; i <= expr.Length - op.Length; i++)
        {
            var c = expr[i];
            if (c == '"') inString = !inString;
            if (!inString)
            {
                if (c == '(') parenDepth++;
                if (c == ')') parenDepth--;
                if (c == '[') bracketDepth++;
                if (c == ']') bracketDepth--;

                if (parenDepth == 0 && bracketDepth == 0)
                {
                    if (expr.Substring(i, op.Length) == op)
                    {
                        // For single-char operators like > or <, make sure we're not part of >= or <=
                        if (op.Length == 1 && i + 1 < expr.Length && expr[i + 1] == '=')
                        {
                            continue;
                        }
                        return i;
                    }
                }
            }
        }
        return -1;
    }

    private bool JsonNodesEqual(JsonNode? a, JsonNode? b)
    {
        if (a == null && b == null) return true;
        if (a == null || b == null) return false;
        return a.ToJsonString() == b.ToJsonString();
    }

    private int CompareNodes(JsonNode? a, JsonNode? b)
    {
        if (a is JsonValue va && b is JsonValue vb)
        {
            // Try to compare as numbers (handle int, long, double)
            var aNum = GetNumericValue(va);
            var bNum = GetNumericValue(vb);
            if (aNum.HasValue && bNum.HasValue)
                return aNum.Value.CompareTo(bNum.Value);
            
            if (va.TryGetValue<string>(out var sa) && vb.TryGetValue<string>(out var sb))
                return string.Compare(sa, sb, StringComparison.Ordinal);
        }
        return 0;
    }

    private double? GetNumericValue(JsonValue val)
    {
        if (val.TryGetValue<double>(out var d)) return d;
        if (val.TryGetValue<int>(out var i)) return i;
        if (val.TryGetValue<long>(out var l)) return l;
        if (val.TryGetValue<float>(out var f)) return f;
        if (val.TryGetValue<decimal>(out var dec)) return (double)dec;
        return null;
    }

    private bool IsTruthy(JsonNode? node)
    {
        if (node == null) return false;
        if (node is JsonValue val)
        {
            if (val.TryGetValue<bool>(out var b)) return b;
            if (val.TryGetValue<string>(out var s)) return !string.IsNullOrEmpty(s);
            if (val.TryGetValue<double>(out var d)) return d != 0;
        }
        return true;
    }

    private List<JsonNode?> ApplyMap(JsonNode? input, string expr)
    {
        if (input is not JsonArray arr)
        {
            throw new JqException($"Cannot map over {GetTypeName(input)}");
        }

        var result = new JsonArray();
        foreach (var item in arr)
        {
            var mapped = ApplyFilter(item, expr);
            foreach (var m in mapped)
            {
                result.Add(m?.DeepClone());
            }
        }
        return new List<JsonNode?> { result };
    }

    private List<JsonNode?> ApplyKeys(JsonNode? input)
    {
        if (input is JsonObject obj)
        {
            var keys = new JsonArray();
            foreach (var key in obj.Select(x => x.Key).OrderBy(x => x))
            {
                keys.Add(JsonValue.Create(key));
            }
            return new List<JsonNode?> { keys };
        }
        if (input is JsonArray arr)
        {
            var keys = new JsonArray();
            for (int i = 0; i < arr.Count; i++)
            {
                keys.Add(JsonValue.Create(i));
            }
            return new List<JsonNode?> { keys };
        }
        throw new JqException($"Cannot get keys of {GetTypeName(input)}");
    }

    private List<JsonNode?> ApplyValues(JsonNode? input)
    {
        if (input is JsonObject obj)
        {
            var values = new JsonArray();
            foreach (var val in obj.Select(x => x.Value))
            {
                values.Add(val?.DeepClone());
            }
            return new List<JsonNode?> { values };
        }
        if (input is JsonArray arr)
        {
            return new List<JsonNode?> { arr.DeepClone() };
        }
        throw new JqException($"Cannot get values of {GetTypeName(input)}");
    }

    private List<JsonNode?> ApplyLength(JsonNode? input)
    {
        int len = input switch
        {
            JsonArray arr => arr.Count,
            JsonObject obj => obj.Count,
            JsonValue val when val.TryGetValue<string>(out var s) => s.Length,
            null => 0,
            _ => throw new JqException($"Cannot get length of {GetTypeName(input)}")
        };
        return new List<JsonNode?> { JsonValue.Create(len) };
    }

    private List<JsonNode?> ApplyType(JsonNode? input)
    {
        var type = input switch
        {
            null => "null",
            JsonArray => "array",
            JsonObject => "object",
            JsonValue val when val.TryGetValue<bool>(out _) => "boolean",
            JsonValue val when val.TryGetValue<double>(out _) => "number",
            JsonValue val when val.TryGetValue<long>(out _) => "number",
            JsonValue val when val.TryGetValue<int>(out _) => "number",
            JsonValue val when val.TryGetValue<string>(out _) => "string",
            _ => "unknown"
        };
        return new List<JsonNode?> { JsonValue.Create(type) };
    }

    private List<JsonNode?> ApplyFirst(JsonNode? input)
    {
        if (input is JsonArray arr && arr.Count > 0)
        {
            return new List<JsonNode?> { arr[0]?.DeepClone() };
        }
        return new List<JsonNode?> { null };
    }

    private List<JsonNode?> ApplyLast(JsonNode? input)
    {
        if (input is JsonArray arr && arr.Count > 0)
        {
            return new List<JsonNode?> { arr[^1]?.DeepClone() };
        }
        return new List<JsonNode?> { null };
    }

    private List<JsonNode?> ApplyAdd(JsonNode? input)
    {
        if (input is not JsonArray arr) throw new JqException("add requires array input");
        
        if (arr.Count == 0) return new List<JsonNode?> { null };
        
        // Check first element type
        var first = arr[0];
        if (first is JsonValue val && val.TryGetValue<double>(out _))
        {
            double sum = 0;
            foreach (var item in arr)
            {
                if (item is JsonValue v && v.TryGetValue<double>(out var d))
                    sum += d;
            }
            return new List<JsonNode?> { JsonValue.Create(sum) };
        }
        if (first is JsonValue strVal && strVal.TryGetValue<string>(out _))
        {
            var concat = string.Join("", arr.Select(x => x?.GetValue<string>() ?? ""));
            return new List<JsonNode?> { JsonValue.Create(concat) };
        }
        if (first is JsonArray)
        {
            var result = new JsonArray();
            foreach (var item in arr)
            {
                if (item is JsonArray subArr)
                {
                    foreach (var sub in subArr)
                        result.Add(sub?.DeepClone());
                }
            }
            return new List<JsonNode?> { result };
        }
        
        throw new JqException("add: cannot add these types");
    }

    private List<JsonNode?> ApplySort(JsonNode? input)
    {
        if (input is not JsonArray arr) throw new JqException("sort requires array input");
        
        var sorted = arr.OrderBy(x => x?.ToJsonString() ?? "").ToList();
        var result = new JsonArray();
        foreach (var item in sorted)
            result.Add(item?.DeepClone());
        return new List<JsonNode?> { result };
    }

    private List<JsonNode?> ApplySortBy(JsonNode? input, string expr)
    {
        if (input is not JsonArray arr) throw new JqException("sort_by requires array input");
        
        var sorted = arr.OrderBy(x => ApplyFilter(x, expr).FirstOrDefault()?.ToJsonString() ?? "").ToList();
        var result = new JsonArray();
        foreach (var item in sorted)
            result.Add(item?.DeepClone());
        return new List<JsonNode?> { result };
    }

    private List<JsonNode?> ApplyReverse(JsonNode? input)
    {
        if (input is not JsonArray arr) throw new JqException("reverse requires array input");
        
        var result = new JsonArray();
        for (int i = arr.Count - 1; i >= 0; i--)
            result.Add(arr[i]?.DeepClone());
        return new List<JsonNode?> { result };
    }

    private List<JsonNode?> ApplyUnique(JsonNode? input)
    {
        if (input is not JsonArray arr) throw new JqException("unique requires array input");
        
        var seen = new HashSet<string>();
        var result = new JsonArray();
        foreach (var item in arr)
        {
            var key = item?.ToJsonString() ?? "null";
            if (seen.Add(key))
                result.Add(item?.DeepClone());
        }
        return new List<JsonNode?> { result };
    }

    private List<JsonNode?> ApplyFlatten(JsonNode? input)
    {
        if (input is not JsonArray arr) throw new JqException("flatten requires array input");
        
        var result = new JsonArray();
        FlattenRecursive(arr, result);
        return new List<JsonNode?> { result };
    }

    private void FlattenRecursive(JsonArray arr, JsonArray result)
    {
        foreach (var item in arr)
        {
            if (item is JsonArray nested)
                FlattenRecursive(nested, result);
            else
                result.Add(item?.DeepClone());
        }
    }

    private List<JsonNode?> ApplyGroupBy(JsonNode? input, string expr)
    {
        if (input is not JsonArray arr) throw new JqException("group_by requires array input");
        
        var groups = arr.GroupBy(x => ApplyFilter(x, expr).FirstOrDefault()?.ToJsonString() ?? "null");
        var result = new JsonArray();
        foreach (var group in groups)
        {
            var groupArr = new JsonArray();
            foreach (var item in group)
                groupArr.Add(item?.DeepClone());
            result.Add(groupArr);
        }
        return new List<JsonNode?> { result };
    }

    private List<JsonNode?> ApplyArrayConstruction(JsonNode? input, string filter)
    {
        var inner = filter[1..^1].Trim();
        if (string.IsNullOrEmpty(inner))
        {
            return new List<JsonNode?> { new JsonArray() };
        }

        var elements = SplitByComma(inner);
        var result = new JsonArray();
        foreach (var elem in elements)
        {
            var values = ApplyFilter(input, elem.Trim());
            foreach (var v in values)
                result.Add(v?.DeepClone());
        }
        return new List<JsonNode?> { result };
    }

    private List<JsonNode?> ApplyObjectConstruction(JsonNode? input, string filter)
    {
        var inner = filter[1..^1].Trim();
        if (string.IsNullOrEmpty(inner))
        {
            return new List<JsonNode?> { new JsonObject() };
        }

        var pairs = SplitByComma(inner);
        var result = new JsonObject();
        foreach (var pair in pairs)
        {
            var colonIdx = pair.IndexOf(':');
            if (colonIdx > 0)
            {
                var key = pair[..colonIdx].Trim().Trim('"');
                var valueExpr = pair[(colonIdx + 1)..].Trim();
                var value = ApplyFilter(input, valueExpr).FirstOrDefault();
                result[key] = value?.DeepClone();
            }
        }
        return new List<JsonNode?> { result };
    }

    private bool ContainsTopLevelComma(string expr)
    {
        var bracketDepth = 0;
        var parenDepth = 0;
        var inString = false;

        foreach (var c in expr)
        {
            if (c == '"') inString = !inString;
            if (!inString)
            {
                if (c == '[' || c == '{') bracketDepth++;
                if (c == ']' || c == '}') bracketDepth--;
                if (c == '(') parenDepth++;
                if (c == ')') parenDepth--;
                if (c == ',' && bracketDepth == 0 && parenDepth == 0) return true;
            }
        }
        return false;
    }

    private bool ContainsPipeOutsideStrings(string filter)
    {
        var inString = false;
        var bracketDepth = 0;
        var parenDepth = 0;

        for (int i = 0; i < filter.Length; i++)
        {
            var c = filter[i];
            if (c == '"' && (i == 0 || filter[i - 1] != '\\')) inString = !inString;
            if (!inString)
            {
                if (c == '[' || c == '{') bracketDepth++;
                if (c == ']' || c == '}') bracketDepth--;
                if (c == '(') parenDepth++;
                if (c == ')') parenDepth--;
                if (c == '|' && bracketDepth == 0 && parenDepth == 0) return true;
            }
        }
        return false;
    }

    private List<string> SplitByPipe(string filter)
    {
        var parts = new List<string>();
        var current = "";
        var inString = false;
        var bracketDepth = 0;
        var parenDepth = 0;

        for (int i = 0; i < filter.Length; i++)
        {
            var c = filter[i];
            if (c == '"' && (i == 0 || filter[i - 1] != '\\')) inString = !inString;
            if (!inString)
            {
                if (c == '[' || c == '{') bracketDepth++;
                if (c == ']' || c == '}') bracketDepth--;
                if (c == '(') parenDepth++;
                if (c == ')') parenDepth--;
                if (c == '|' && bracketDepth == 0 && parenDepth == 0)
                {
                    parts.Add(current);
                    current = "";
                    continue;
                }
            }
            current += c;
        }
        if (!string.IsNullOrEmpty(current)) parts.Add(current);
        return parts;
    }

    private List<string> SplitByComma(string expr)
    {
        var parts = new List<string>();
        var current = "";
        var bracketDepth = 0;
        var parenDepth = 0;
        var inString = false;

        foreach (var c in expr)
        {
            if (c == '"') inString = !inString;
            if (!inString)
            {
                if (c == '[' || c == '{') bracketDepth++;
                if (c == ']' || c == '}') bracketDepth--;
                if (c == '(') parenDepth++;
                if (c == ')') parenDepth--;
                if (c == ',' && bracketDepth == 0 && parenDepth == 0)
                {
                    parts.Add(current.Trim());
                    current = "";
                    continue;
                }
            }
            current += c;
        }
        if (!string.IsNullOrEmpty(current)) parts.Add(current.Trim());
        return parts;
    }

    private string FormatOutput(List<JsonNode?> results, JqOptions options)
    {
        var outputs = new List<string>();

        foreach (var result in results)
        {
            if (result == null)
            {
                outputs.Add("null");
            }
            else if (options.RawOutput && result is JsonValue val && val.TryGetValue<string>(out var str))
            {
                outputs.Add(str);
            }
            else
            {
                var jsonOptions = new JsonSerializerOptions
                {
                    WriteIndented = !options.Compact
                };
                outputs.Add(result.ToJsonString(jsonOptions));
            }
        }

        return string.Join("\n", outputs);
    }

    private string GetTypeName(JsonNode? node) => node switch
    {
        null => "null",
        JsonArray => "array",
        JsonObject => "object",
        JsonValue val when val.TryGetValue<bool>(out _) => "boolean",
        JsonValue val when val.TryGetValue<string>(out _) => "string",
        JsonValue => "number",
        _ => "unknown"
    };

    private JqOptions ParseOptions(string[] args, out string filter, out string? inputFile)
    {
        var options = new JqOptions();
        filter = "";
        inputFile = null;

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "-r":
                case "--raw-output":
                    options.RawOutput = true;
                    break;
                case "-c":
                case "--compact":
                    options.Compact = true;
                    break;
                case "-e":
                case "--exit-status":
                    options.ExitStatus = true;
                    break;
                case "-s":
                case "--slurp":
                    options.Slurp = true;
                    break;
                case "-n":
                case "--null-input":
                    options.NullInput = true;
                    break;
                default:
                    if (!arg.StartsWith('-'))
                    {
                        if (string.IsNullOrEmpty(filter))
                            filter = arg;
                        else
                            inputFile = arg;
                    }
                    break;
            }
        }

        return options;
    }

    private class JqOptions
    {
        public bool RawOutput { get; set; }
        public bool Compact { get; set; }
        public bool ExitStatus { get; set; }
        public bool Slurp { get; set; }
        public bool NullInput { get; set; }
    }

    private class JqException : Exception
    {
        public JqException(string message) : base(message) { }
    }
}
