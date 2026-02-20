using System.Net;
using System.Text;
using System.Text.Json;
using AgentSandbox.Core.FileSystem;
using AgentSandbox.Core.Security;
using AgentSandbox.Core.Shell;
using AgentSandbox.Core.Shell.Extensions;

namespace AgentSandbox.Tests.ShellExtensions;

public class CurlCommandTests
{
    private readonly FileSystem _fs;
    private readonly SandboxShell _shell;

    public CurlCommandTests()
    {
        _fs = new FileSystem();
        _shell = new SandboxShell(_fs);
    }

    #region Basic Request Tests

    [Fact]
    public void Curl_WithoutUrl_ReturnsError()
    {
        _shell.RegisterCommand(new CurlCommand());
        
        var result = _shell.Execute("curl");
        
        Assert.False(result.Success);
        Assert.Contains("missing URL", result.Stderr);
    }

    [Fact]
    public void Curl_GetRequest_ReturnsResponse()
    {
        var handler = new MockHttpMessageHandler("Hello, World!", HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);
        _shell.RegisterCommand(new CurlCommand(httpClient));

        var result = _shell.Execute("curl http://example.com/api");

        Assert.True(result.Success);
        Assert.Equal("Hello, World!", result.Stdout);
        Assert.Equal(HttpMethod.Get, handler.LastRequest?.Method);
    }

    [Fact]
    public void Curl_PostRequest_SendsPostMethod()
    {
        var handler = new MockHttpMessageHandler("Created", HttpStatusCode.Created);
        var httpClient = new HttpClient(handler);
        _shell.RegisterCommand(new CurlCommand(httpClient));

        var result = _shell.Execute("curl -X POST http://example.com/api");

        Assert.True(result.Success);
        Assert.Equal(HttpMethod.Post, handler.LastRequest?.Method);
    }

    [Fact]
    public void Curl_PutRequest_SendsPutMethod()
    {
        var handler = new MockHttpMessageHandler("Updated", HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);
        _shell.RegisterCommand(new CurlCommand(httpClient));

        var result = _shell.Execute("curl -X PUT http://example.com/api/1");

        Assert.True(result.Success);
        Assert.Equal(HttpMethod.Put, handler.LastRequest?.Method);
    }

    [Fact]
    public void Curl_DeleteRequest_SendsDeleteMethod()
    {
        var handler = new MockHttpMessageHandler("", HttpStatusCode.NoContent);
        var httpClient = new HttpClient(handler);
        _shell.RegisterCommand(new CurlCommand(httpClient));

        var result = _shell.Execute("curl -X DELETE http://example.com/api/1");

        Assert.True(result.Success);
        Assert.Equal(HttpMethod.Delete, handler.LastRequest?.Method);
    }

    #endregion

    #region Header Tests

    [Fact]
    public void Curl_WithHeader_SendsHeader()
    {
        var handler = new MockHttpMessageHandler("OK", HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);
        _shell.RegisterCommand(new CurlCommand(httpClient));

        var result = _shell.Execute("curl -H \"Authorization: Bearer token123\" http://example.com/api");

        Assert.True(result.Success);
        Assert.True(handler.LastRequest?.Headers.Contains("Authorization"));
        Assert.Equal("Bearer token123", handler.LastRequest?.Headers.GetValues("Authorization").First());
    }

    [Fact]
    public void Curl_WithMultipleHeaders_SendsAllHeaders()
    {
        var handler = new MockHttpMessageHandler("OK", HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);
        _shell.RegisterCommand(new CurlCommand(httpClient));

        var result = _shell.Execute("curl -H \"Authorization: Bearer token\" -H \"X-Custom: value\" http://example.com/api");

        Assert.True(result.Success);
        Assert.True(handler.LastRequest?.Headers.Contains("Authorization"));
        Assert.True(handler.LastRequest?.Headers.Contains("X-Custom"));
    }

    [Fact]
    public void Curl_WithSecretRefHeader_ResolvesSecretJustInTime()
    {
        var handler = new MockHttpMessageHandler("OK", HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);
        var broker = new TestSecretBroker(new Dictionary<string, string>
        {
            ["api-token"] = "super-secret-token"
        });
        var shell = new SandboxShell(_fs, broker);
        shell.RegisterCommand(new CurlCommand(httpClient));

        var result = shell.Execute("curl -H \"Authorization: Bearer secretRef:api-token\" http://example.com/api");

        Assert.True(result.Success);
        Assert.Equal("Bearer super-secret-token", handler.LastRequest?.Headers.GetValues("Authorization").First());
    }

    [Fact]
    public void Curl_WithUnknownSecretRef_ReturnsError()
    {
        var handler = new MockHttpMessageHandler("OK", HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);
        var broker = new TestSecretBroker(new Dictionary<string, string>());
        var shell = new SandboxShell(_fs, broker);
        shell.RegisterCommand(new CurlCommand(httpClient));

        var result = shell.Execute("curl -H \"Authorization: Bearer secretRef:missing\" http://example.com/api");

        Assert.False(result.Success);
        Assert.Contains("unknown secretRef 'missing'", result.Stderr);
    }

    [Fact]
    public void Curl_WithMultipleSecretRefsAcrossFields_ResolvesAllSecrets()
    {
        var handler = new MockHttpMessageHandler("OK", HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);
        var broker = new TestSecretBroker(new Dictionary<string, string>
        {
            ["api-token"] = "super-secret-token",
            ["query_secret"] = "query-value",
            ["api.token"] = "dot-value"
        });
        var shell = new SandboxShell(_fs, broker);
        shell.RegisterCommand(new CurlCommand(httpClient));

        var result = shell.Execute(
            "curl -H \"Authorization: Bearer secretRef:api-token\" " +
            "\"http://example.com/api?key=secretRef:query_secret\" " +
            "-d \"code=secretRef:api.token\"");

        Assert.True(result.Success);
        Assert.Equal("Bearer super-secret-token", handler.LastRequest?.Headers.GetValues("Authorization").First());
        Assert.Equal("http://example.com/api?key=query-value", handler.LastRequest?.RequestUri?.ToString());
        Assert.Equal("code=dot-value", handler.LastRequestBody);
    }

    [Fact]
    public void Curl_WithAllowedRef_RejectsSecretRefOutsideCommandAllowlist()
    {
        var handler = new MockHttpMessageHandler("OK", HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);
        var broker = new TestSecretBroker(new Dictionary<string, string>
        {
            ["api-token"] = "super-secret-token"
        });
        var shell = new SandboxShell(_fs, broker);
        shell.RegisterCommand(new CurlCommand(httpClient));

        var result = shell.Execute("curl --allowed-ref other-token -H \"Authorization: Bearer secretRef:api-token\" http://example.com/api");

        Assert.False(result.Success);
        Assert.Contains("not allowed by command policy", result.Stderr);
    }

    [Fact]
    public void Curl_WithAllowedRef_ResolvesSecretInCommandAllowlist()
    {
        var handler = new MockHttpMessageHandler("OK", HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);
        var broker = new TestSecretBroker(new Dictionary<string, string>
        {
            ["api-token"] = "super-secret-token"
        });
        var shell = new SandboxShell(_fs, broker);
        shell.RegisterCommand(new CurlCommand(httpClient));

        var result = shell.Execute("curl --allowed-ref api-token -H \"Authorization: Bearer secretRef:api-token\" http://example.com/api");

        Assert.True(result.Success);
        Assert.Equal("Bearer super-secret-token", handler.LastRequest?.Headers.GetValues("Authorization").First());
    }

    [Fact]
    public void Curl_WithSandboxPolicy_RejectsSecretRefOutsideAllowedRefs()
    {
        var handler = new MockHttpMessageHandler("OK", HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);
        var broker = new TestSecretBroker(new Dictionary<string, string>
        {
            ["api-token"] = "super-secret-token"
        });
        var shell = new SandboxShell(
            _fs,
            broker,
            new SecretResolutionPolicy
            {
                AllowedRefs = new HashSet<string>(StringComparer.Ordinal) { "other-token" }
            });
        shell.RegisterCommand(new CurlCommand(httpClient));

        var result = shell.Execute("curl -H \"Authorization: Bearer secretRef:api-token\" http://example.com/api");

        Assert.False(result.Success);
        Assert.Contains("not allowed by sandbox policy", result.Stderr);
    }

    [Fact]
    public void Curl_WithSandboxPolicy_ResolvesSecretInsideAllowedRefs()
    {
        var handler = new MockHttpMessageHandler("OK", HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);
        var broker = new TestSecretBroker(new Dictionary<string, string>
        {
            ["api-token"] = "super-secret-token"
        });
        var shell = new SandboxShell(
            _fs,
            broker,
            new SecretResolutionPolicy
            {
                AllowedRefs = new HashSet<string>(StringComparer.Ordinal) { "api-token" }
            });
        shell.RegisterCommand(new CurlCommand(httpClient));

        var result = shell.Execute("curl -H \"Authorization: Bearer secretRef:api-token\" http://example.com/api");

        Assert.True(result.Success);
        Assert.Equal("Bearer super-secret-token", handler.LastRequest?.Headers.GetValues("Authorization").First());
    }

    [Fact]
    public void Curl_WithCommandAndSandboxAllowedRefs_ResolvesSecretOnlyWhenInBothAllowlists()
    {
        var handler = new MockHttpMessageHandler("OK", HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);
        var broker = new TestSecretBroker(new Dictionary<string, string>
        {
            ["api-token"] = "super-secret-token"
        });
        var shell = new SandboxShell(
            _fs,
            broker,
            new SecretResolutionPolicy
            {
                AllowedRefs = new HashSet<string>(StringComparer.Ordinal) { "api-token" }
            });
        shell.RegisterCommand(new CurlCommand(httpClient));

        var result = shell.Execute("curl --allowed-ref api-token -H \"Authorization: Bearer secretRef:api-token\" http://example.com/api");

        Assert.True(result.Success);
        Assert.Equal("Bearer super-secret-token", handler.LastRequest?.Headers.GetValues("Authorization").First());
    }

    [Fact]
    public void Curl_WithCommandAndSandboxAllowedRefs_SecretOnlyInSandbox_IsRejectedByCommandPolicy()
    {
        var handler = new MockHttpMessageHandler("OK", HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);
        var broker = new TestSecretBroker(new Dictionary<string, string>
        {
            ["api-token"] = "super-secret-token"
        });
        var shell = new SandboxShell(
            _fs,
            broker,
            new SecretResolutionPolicy
            {
                AllowedRefs = new HashSet<string>(StringComparer.Ordinal) { "api-token" }
            });
        shell.RegisterCommand(new CurlCommand(httpClient));

        var result = shell.Execute("curl --allowed-ref other-token -H \"Authorization: Bearer secretRef:api-token\" http://example.com/api");

        Assert.False(result.Success);
        Assert.Contains("not allowed by command policy", result.Stderr);
    }

    [Fact]
    public void Curl_WithCommandAndSandboxAllowedRefs_SecretOnlyInCommand_IsRejectedBySandboxPolicy()
    {
        var handler = new MockHttpMessageHandler("OK", HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);
        var broker = new TestSecretBroker(new Dictionary<string, string>
        {
            ["api-token"] = "super-secret-token"
        });
        var shell = new SandboxShell(
            _fs,
            broker,
            new SecretResolutionPolicy
            {
                AllowedRefs = new HashSet<string>(StringComparer.Ordinal) { "other-token" }
            });
        shell.RegisterCommand(new CurlCommand(httpClient));

        var result = shell.Execute("curl --allowed-ref api-token -H \"Authorization: Bearer secretRef:api-token\" http://example.com/api");

        Assert.False(result.Success);
        Assert.Contains("not allowed by sandbox policy", result.Stderr);
    }

    [Fact]
    public void Curl_WithSandboxPolicy_RejectsSecretRefExceedingMaxAge()
    {
        var handler = new MockHttpMessageHandler("OK", HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);
        var broker = new TestSecretBroker(new Dictionary<string, ResolvedSecret>
        {
            ["api-token"] = new("super-secret-token", DateTimeOffset.UtcNow - TimeSpan.FromMinutes(10))
        });
        var shell = new SandboxShell(
            _fs,
            broker,
            new SecretResolutionPolicy
            {
                MaxSecretAge = TimeSpan.FromMinutes(5)
            });
        shell.RegisterCommand(new CurlCommand(httpClient));

        var result = shell.Execute("curl -H \"Authorization: Bearer secretRef:api-token\" http://example.com/api");

        Assert.False(result.Success);
        Assert.Contains("exceeds max-age policy", result.Stderr);
    }

    [Fact]
    public void Curl_WithSandboxPolicy_AllowsSecretRefWithinMaxAge()
    {
        var handler = new MockHttpMessageHandler("OK", HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);
        var broker = new TestSecretBroker(new Dictionary<string, ResolvedSecret>
        {
            ["api-token"] = new("super-secret-token", DateTimeOffset.UtcNow - TimeSpan.FromMinutes(1))
        });
        var shell = new SandboxShell(
            _fs,
            broker,
            new SecretResolutionPolicy
            {
                MaxSecretAge = TimeSpan.FromMinutes(5)
            });
        shell.RegisterCommand(new CurlCommand(httpClient));

        var result = shell.Execute("curl -H \"Authorization: Bearer secretRef:api-token\" http://example.com/api");

        Assert.True(result.Success);
        Assert.Equal("Bearer super-secret-token", handler.LastRequest?.Headers.GetValues("Authorization").First());
    }

    [Fact]
    public void Curl_WithSandboxPolicy_RejectsEgressHostDeniedByHook()
    {
        var handler = new MockHttpMessageHandler("OK", HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);
        var broker = new TestSecretBroker(new Dictionary<string, string>
        {
            ["api-token"] = "super-secret-token"
        });
        var shell = new SandboxShell(
            _fs,
            broker,
            new SecretResolutionPolicy
            {
                EgressHostAllowlistHook = context => context.DestinationUri.Host == "allowed.example.com"
            });
        shell.RegisterCommand(new CurlCommand(httpClient));

        var result = shell.Execute("curl -H \"Authorization: Bearer secretRef:api-token\" http://example.com/api");

        Assert.False(result.Success);
        Assert.Contains("egress host 'example.com' is not allowed for secretRef 'api-token'", result.Stderr);
    }

    [Fact]
    public void Curl_WithSandboxPolicy_AllowsEgressHostAllowedByHook()
    {
        var handler = new MockHttpMessageHandler("OK", HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);
        var broker = new TestSecretBroker(new Dictionary<string, string>
        {
            ["api-token"] = "super-secret-token"
        });
        var shell = new SandboxShell(
            _fs,
            broker,
            new SecretResolutionPolicy
            {
                EgressHostAllowlistHook = context => context.DestinationUri.Host == "allowed.example.com"
            });
        shell.RegisterCommand(new CurlCommand(httpClient));

        var result = shell.Execute("curl -H \"Authorization: Bearer secretRef:api-token\" http://allowed.example.com/api");

        Assert.True(result.Success);
    }

    [Fact]
    public void Curl_WithUrlSecretRef_StillAppliesEgressPolicyAfterUrlResolution()
    {
        var handler = new MockHttpMessageHandler("OK", HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);
        var broker = new TestSecretBroker(new Dictionary<string, string>
        {
            ["api-url"] = "http://blocked.example.com/api"
        });
        var shell = new SandboxShell(
            _fs,
            broker,
            new SecretResolutionPolicy
            {
                EgressHostAllowlistHook = context => context.DestinationUri.Host == "allowed.example.com"
            });
        shell.RegisterCommand(new CurlCommand(httpClient));

        var result = shell.Execute("curl secretRef:api-url");

        Assert.False(result.Success);
        Assert.Contains("egress host 'blocked.example.com' is not allowed for secretRef 'api-url'", result.Stderr);
    }

    #endregion

    #region Data/Body Tests

    [Fact]
    public void Curl_WithData_SendsRequestBody()
    {
        var handler = new MockHttpMessageHandler("OK", HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);
        _shell.RegisterCommand(new CurlCommand(httpClient));

        var result = _shell.Execute("curl -d \"name=test\" http://example.com/api");

        Assert.True(result.Success);
        Assert.Equal(HttpMethod.Post, handler.LastRequest?.Method); // Auto POST when data provided
        Assert.Equal("name=test", handler.LastRequestBody);
    }

    [Fact]
    public void Curl_WithJsonData_SendsJsonBody()
    {
        var handler = new MockHttpMessageHandler("{\"id\": 1}", HttpStatusCode.Created);
        var httpClient = new HttpClient(handler);
        _shell.RegisterCommand(new CurlCommand(httpClient));

        var result = _shell.Execute("curl -X POST -H \"Content-Type: application/json\" -d \"{\\\"name\\\":\\\"test\\\"}\" http://example.com/api");

        Assert.True(result.Success);
        Assert.Contains("application/json", handler.LastRequest?.Content?.Headers.ContentType?.ToString());
    }

    #endregion

    #region Output Options Tests

    [Fact]
    public void Curl_WithOutputFile_WritesToFile()
    {
        var handler = new MockHttpMessageHandler("{\"data\": \"value\"}", HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);
        _shell.RegisterCommand(new CurlCommand(httpClient));

        var result = _shell.Execute("curl -o /response.json http://example.com/api");

        Assert.True(result.Success);
        Assert.True(_fs.Exists("/response.json"));
        var responseBytes = _fs.ReadFileBytes("/response.json");
        var response = Encoding.UTF8.GetString(responseBytes);
        Assert.Equal("{\"data\": \"value\"}", response);
    }

    [Fact]
    public void Curl_WithOutputFile_RedactsResolvedSecretsBeforePersisting()
    {
        var handler = new MockHttpMessageHandler("echo:super-secret-token", HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);
        var broker = new TestSecretBroker(new Dictionary<string, string>
        {
            ["api-token"] = "super-secret-token"
        });
        var shell = new SandboxShell(_fs, broker);
        shell.RegisterCommand(new CurlCommand(httpClient));

        var result = shell.Execute("curl -o /response.txt -H \"Authorization: Bearer secretRef:api-token\" http://example.com/api");

        Assert.True(result.Success);
        var responseBytes = _fs.ReadFileBytes("/response.txt");
        var response = Encoding.UTF8.GetString(responseBytes);
        Assert.DoesNotContain("super-secret-token", response);
        Assert.Contains("***REDACTED***", response);
    }

    [Fact]
    public void Curl_WithOutputFileAndSilent_NoOutput()
    {
        var handler = new MockHttpMessageHandler("data", HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);
        _shell.RegisterCommand(new CurlCommand(httpClient));

        var result = _shell.Execute("curl -s -o /output.txt http://example.com/api");

        Assert.True(result.Success);
        Assert.Equal("", result.Stdout);
        Assert.True(_fs.Exists("/output.txt"));
    }

    [Fact]
    public void Curl_WithIncludeHeaders_ShowsResponseHeaders()
    {
        var handler = new MockHttpMessageHandler("Body", HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);
        _shell.RegisterCommand(new CurlCommand(httpClient));

        var result = _shell.Execute("curl -i http://example.com/api");

        Assert.True(result.Success);
        Assert.Contains("HTTP/", result.Stdout);
        Assert.Contains("200", result.Stdout);
        Assert.Contains("Body", result.Stdout);
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public void Curl_HttpError_ReturnsError()
    {
        var handler = new MockHttpMessageHandler(new HttpRequestException("Connection refused"));
        var httpClient = new HttpClient(handler);
        _shell.RegisterCommand(new CurlCommand(httpClient));

        var result = _shell.Execute("curl http://example.com/api");

        Assert.False(result.Success);
        Assert.Contains("Connection refused", result.Stderr);
    }

    [Fact]
    public void Curl_InvalidUrl_ReturnsError()
    {
        var handler = new MockHttpMessageHandler(new InvalidOperationException("Invalid URI"));
        var httpClient = new HttpClient(handler);
        _shell.RegisterCommand(new CurlCommand(httpClient));

        var result = _shell.Execute("curl not-a-valid-url");

        Assert.False(result.Success);
    }

    #endregion

    #region Long Form Options Tests

    [Fact]
    public void Curl_LongFormOptions_Work()
    {
        var handler = new MockHttpMessageHandler("OK", HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);
        _shell.RegisterCommand(new CurlCommand(httpClient));

        var result = _shell.Execute("curl --request POST --header \"X-Test: value\" --data \"body\" http://example.com/api");

        Assert.True(result.Success);
        Assert.Equal(HttpMethod.Post, handler.LastRequest?.Method);
        Assert.Equal("body", handler.LastRequestBody);
    }

    #endregion

    #region Registration Tests

    [Fact]
    public void CurlCommand_IsRegistered_CanBeExecuted()
    {
        _shell.RegisterCommand(new CurlCommand(new HttpClient(new MockHttpMessageHandler("OK", HttpStatusCode.OK))));

        var commands = _shell.GetAvailableCommands();

        Assert.Contains("curl", commands);
    }

    [Fact]
    public void CurlCommand_NotRegistered_ReturnsCommandNotFound()
    {
        var result = _shell.Execute("curl http://example.com");

        Assert.False(result.Success);
        Assert.Contains("command not found", result.Stderr);
    }

    #endregion

    #region Mock HttpMessageHandler

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _response;
        private readonly HttpStatusCode _statusCode;
        private readonly Exception? _exception;

        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }

        public MockHttpMessageHandler(string response, HttpStatusCode statusCode)
        {
            _response = response;
            _statusCode = statusCode;
        }

        public MockHttpMessageHandler(Exception exception)
        {
            _response = "";
            _statusCode = HttpStatusCode.InternalServerError;
            _exception = exception;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            
            if (request.Content != null)
            {
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            if (_exception != null)
            {
                throw _exception;
            }

            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_response),
                RequestMessage = request
            };
        }
    }

    private sealed class TestSecretBroker : ISecretBroker
    {
        private readonly IReadOnlyDictionary<string, string> _secrets;
        private readonly IReadOnlyDictionary<string, ResolvedSecret>? _resolvedSecrets;

        public TestSecretBroker(IReadOnlyDictionary<string, string> secrets)
        {
            _secrets = secrets;
        }

        public TestSecretBroker(IReadOnlyDictionary<string, ResolvedSecret> secrets)
        {
            _resolvedSecrets = secrets;
            _secrets = secrets.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Value, StringComparer.Ordinal);
        }

        public bool TryResolve(string secretRef, out string secretValue)
        {
            return _secrets.TryGetValue(secretRef, out secretValue!);
        }

        public bool TryResolve(string secretRef, out ResolvedSecret secret)
        {
            if (_resolvedSecrets != null)
            {
                return _resolvedSecrets.TryGetValue(secretRef, out secret);
            }

            if (_secrets.TryGetValue(secretRef, out var secretValue))
            {
                secret = new ResolvedSecret(secretValue);
                return true;
            }

            secret = default;
            return false;
        }
    }

    #endregion
}
