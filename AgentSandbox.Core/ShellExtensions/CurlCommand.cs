using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AgentSandbox.Core.Security;

namespace AgentSandbox.Core.Shell.Extensions;

/// <summary>
/// Curl command implementation for making HTTP requests.
/// Supports common curl syntax: -X, -H, -d, -o, -s, -i, -L.
/// </summary>
public class CurlCommand : IShellCommand
{
    private readonly HttpClient _httpClient;

    public string Name => "curl";
    public IReadOnlyList<string> Aliases => Array.Empty<string>();
    public string Description => "Transfer data from or to a server using HTTP";
    public string Usage => "curl [options] <url>\nRun 'curl help' for available options.";

    private static string HelpText => @"curl - Transfer data from or to a server using HTTP

Usage: curl [options] <url>

Options:
  -X, --request <method>   HTTP method (GET, POST, PUT, DELETE, PATCH)
  -H, --header <header>    Add header (use secretRef:<ref> in Authorization values)
  -d, --data <data>        Request body data
  --allowed-ref <ref>      Restrict secretRef usage to explicitly allowed refs (repeatable)
  -o, --output <file>      Write output to file
  -s, --silent             Silent mode (no progress)
  -i, --include            Include response headers in output
  -L, --location           Follow redirects
  -h, --help               Show this help message

Examples:
  curl https://api.example.com/data
  curl -X POST -H ""Content-Type: application/json"" -d '{""key"":""value""}' https://api.example.com
  curl -H ""Authorization: Bearer secretRef:api-token"" https://api.example.com/data
  curl -o output.json https://api.example.com/data
  curl -i -L https://example.com";

    public CurlCommand() : this(new HttpClient())
    {
    }

    public CurlCommand(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public ShellResult Execute(string[] args, IShellContext context)
    {
        // Handle help
        if (args.Length > 0 && (args[0] == "--help" || args[0] == "-h" || args[0] == "help"))
        {
            return ShellResult.Ok(HelpText);
        }

        if (args.Length == 0)
        {
            return ShellResult.Error($"curl: missing URL\n{Usage}");
        }

        var options = ParseArguments(args);
        
        if (string.IsNullOrEmpty(options.Url))
        {
            return ShellResult.Error("curl: missing URL");
        }

        try
        {
            var resolvedSecrets = new HashSet<string>(StringComparer.Ordinal);
            options = ResolveSecretReferences(options, context, resolvedSecrets);
            var response = ExecuteRequest(options).GetAwaiter().GetResult();
            
            var output = new StringBuilder();
            
            if (options.IncludeHeaders)
            {
                output.AppendLine($"HTTP/{response.Version} {(int)response.StatusCode} {response.ReasonPhrase}");
                foreach (var header in response.Headers)
                {
                    output.AppendLine($"{header.Key}: {string.Join(", ", header.Value)}");
                }
                foreach (var header in response.Content.Headers)
                {
                    output.AppendLine($"{header.Key}: {string.Join(", ", header.Value)}");
                }
                output.AppendLine();
            }

            var content = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            output.Append(content);

            var result = RedactResolvedSecrets(output.ToString(), resolvedSecrets);

            // Write to file if -o specified
            if (!string.IsNullOrEmpty(options.OutputFile))
            {
                var path = context.ResolvePath(options.OutputFile);
                context.FileSystem.WriteFile(path, result);
                if (!options.Silent)
                {
                    return ShellResult.Ok($"Output written to {path}");
                }
                return ShellResult.Ok();
            }

            return ShellResult.Ok(result);
        }
        catch (HttpRequestException ex)
        {
            return ShellResult.Error($"curl: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            return ShellResult.Error("curl: request timed out");
        }
        catch (Exception ex)
        {
            return ShellResult.Error($"curl: {ex.Message}");
        }
    }

    private async Task<HttpResponseMessage> ExecuteRequest(CurlOptions options)
    {
        var request = new HttpRequestMessage(options.Method, options.Url);

        // Add headers
        foreach (var header in options.Headers)
        {
            var colonIndex = header.IndexOf(':');
            if (colonIndex > 0)
            {
                var name = header[..colonIndex].Trim();
                var value = header[(colonIndex + 1)..].Trim();
                
                // Content-Type must be set on content, not request headers
                if (name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                {
                    continue; // Will be set with content
                }
                
                request.Headers.TryAddWithoutValidation(name, value);
            }
        }

        // Add body data
        if (!string.IsNullOrEmpty(options.Data))
        {
            var contentType = options.Headers
                .FirstOrDefault(h => h.StartsWith("Content-Type:", StringComparison.OrdinalIgnoreCase));
            
            if (contentType != null)
            {
                var mediaType = contentType.Split(':')[1].Trim();
                request.Content = new StringContent(options.Data, Encoding.UTF8, mediaType);
            }
            else
            {
                // Default to application/x-www-form-urlencoded
                request.Content = new StringContent(options.Data, Encoding.UTF8, "application/x-www-form-urlencoded");
            }
        }

        return await _httpClient.SendAsync(request);
    }

    private CurlOptions ParseArguments(string[] args)
    {
        var options = new CurlOptions();
        
        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            
            switch (arg)
            {
                case "-X":
                case "--request":
                    if (i + 1 < args.Length)
                    {
                        options.Method = new HttpMethod(args[++i].ToUpperInvariant());
                    }
                    break;
                    
                case "-H":
                case "--header":
                    if (i + 1 < args.Length)
                    {
                        options.Headers.Add(args[++i]);
                    }
                    break;
                    
                case "-d":
                case "--data":
                    if (i + 1 < args.Length)
                    {
                        options.Data = args[++i];
                        // Default to POST when data is provided
                        if (options.Method == HttpMethod.Get)
                        {
                            options.Method = HttpMethod.Post;
                        }
                    }
                    break;
                    
                case "-o":
                case "--output":
                    if (i + 1 < args.Length)
                    {
                        options.OutputFile = args[++i];
                    }
                    break;

                case "--allowed-ref":
                    if (i + 1 < args.Length)
                    {
                        options.AllowedRefs.Add(args[++i]);
                    }
                    break;
                    
                case "-s":
                case "--silent":
                    options.Silent = true;
                    break;
                    
                case "-i":
                case "--include":
                    options.IncludeHeaders = true;
                    break;
                    
                case "-L":
                case "--location":
                    options.FollowRedirects = true;
                    break;
                    
                default:
                    // Assume it's the URL if it doesn't start with -
                    if (!arg.StartsWith("-"))
                    {
                        options.Url = arg;
                    }
                    break;
            }
        }

        return options;
    }

    private static CurlOptions ResolveSecretReferences(CurlOptions options, IShellContext context, ISet<string> resolvedSecrets)
    {
        var baseAccessRequest = new SecretAccessRequest
        {
            AllowedRefs = options.AllowedRefs.Count > 0 ? options.AllowedRefs : null,
            CommandName = "curl"
        };

        var unresolvedUrl = options.Url;
        var resolvedUrl = ResolveSecretRefs(unresolvedUrl, context, resolvedSecrets, baseAccessRequest);

        Uri? destinationUri = null;
        if (Uri.TryCreate(resolvedUrl, UriKind.Absolute, out var parsedDestinationUri))
        {
            destinationUri = parsedDestinationUri;
            resolvedUrl = ResolveSecretRefs(
                unresolvedUrl,
                context,
                resolvedSecrets,
                new SecretAccessRequest
                {
                    AllowedRefs = baseAccessRequest.AllowedRefs,
                    DestinationUri = destinationUri,
                    CommandName = baseAccessRequest.CommandName
                });
        }

        options.Url = resolvedUrl;
        var accessRequest = new SecretAccessRequest
        {
            AllowedRefs = baseAccessRequest.AllowedRefs,
            DestinationUri = destinationUri,
            CommandName = baseAccessRequest.CommandName
        };

        if (!string.IsNullOrEmpty(options.Data))
        {
            options.Data = ResolveSecretRefs(options.Data, context, resolvedSecrets, accessRequest);
        }

        for (var i = 0; i < options.Headers.Count; i++)
        {
            options.Headers[i] = ResolveSecretRefs(options.Headers[i], context, resolvedSecrets, accessRequest);
        }

        return options;
    }

    private static string ResolveSecretRefs(string value, IShellContext context, ISet<string> resolvedSecrets, SecretAccessRequest accessRequest)
    {
        if (!context.TryResolveSecretReferences(value, accessRequest, resolvedSecrets, out var resolvedValue, out var errorMessage))
        {
            throw new InvalidOperationException(errorMessage ?? "secret resolution failed");
        }

        return resolvedValue;
    }

    private static string RedactResolvedSecrets(string value, IEnumerable<string> resolvedSecrets)
    {
        var redacted = value;
        foreach (var secret in resolvedSecrets)
        {
            if (!string.IsNullOrEmpty(secret))
            {
                redacted = redacted.Replace(secret, "***REDACTED***", StringComparison.Ordinal);
            }
        }

        return redacted;
    }

    private class CurlOptions
    {
        public string Url { get; set; } = string.Empty;
        public HttpMethod Method { get; set; } = HttpMethod.Get;
        public List<string> Headers { get; } = new();
        public string? Data { get; set; }
        public string? OutputFile { get; set; }
        public HashSet<string> AllowedRefs { get; } = new(StringComparer.Ordinal);
        public bool Silent { get; set; }
        public bool IncludeHeaders { get; set; }
        public bool FollowRedirects { get; set; }
    }
}
