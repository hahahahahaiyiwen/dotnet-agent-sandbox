# AgentSandbox.Extensions

Extensions for AgentSandbox including AI function generation, dependency injection, and observability.

## Installation

```bash
dotnet add package AgentSandbox.Extensions
```

## AI Function Generation

Convert sandbox operations into `AIFunction` objects for use with AI chat APIs, Semantic Kernel, or agents:

```csharp
using AgentSandbox.Core;
using AgentSandbox.Extensions;

var sandbox = new Sandbox();

// Get all sandbox functions as AIFunction
var functions = sandbox.GetSandboxFunctions();

// Or create individual functions
var bashFunction = sandbox.GetBashFunction();
var readFileFunction = sandbox.GetReadFileFunction();
var writeFileFunction = sandbox.GetWriteFileFunction();
var patchFunction = sandbox.GetApplyPatchFunction();
var skillFunction = sandbox.GetSkillFunction();

// Use with ChatClient or Semantic Kernel
var options = new ChatOptions { Tools = functions.ToList() };
```

### Available Functions

- **`bash_shell`** - Execute shell commands with dynamic help text
  - Parameters: `command` (string)
  - Returns: Structured response with `success`, `message`, and optional `output`

- **`read_file`** - Read file contents with line-range support
  - Parameters: `path` (string), `startLine` (int?, default null), `endLine` (int?, default null)
  - Returns: Structured response with `success`, `message`, and `output` (joined file content)
  - Line indexes are 1-based (`startLine` inclusive, `endLine` exclusive)
  - Example: `read_file('/logs.txt', 100, 120)` returns lines 100-119

- **`write_file`** - Write or create files
  - Parameters: `path` (string), `content` (string)
  - Returns: Structured success response; validation failures surface as tool errors
  - Auto-creates parent directories

- **`edit_file`** - Apply unified diff patches
  - Parameters: `path` (string), `patch` (string)
  - Returns: Structured success response; validation failures surface as tool errors
  - Supports standard unified diff format

- **`get_skill`** - Retrieve skill information and instructions
  - Parameters: `skillName` (string)
  - Returns: Skill metadata and instructions, or error if not found

### Usage Example

```csharp
using AgentSandbox.Core;
using AgentSandbox.Extensions;
using Microsoft.Extensions.AI;

var sandbox = new Sandbox();
var functions = sandbox.GetSandboxFunctions();

// Register with chat client
var client = new ChatClient(model: openAiModel);
var response = await client.CompleteAsync(
    messages: new[] { new ChatMessage(ChatRole.User, "List files in /tmp") },
    tools: functions.ToList()
);
```

## Dependency Injection

```csharp
using AgentSandbox.Extensions.DependencyInjection;

services.AddAgentSandbox(options =>
{
    options.WorkingDirectory = "/workspace";
    options.MaxTotalSize = 1024 * 1024;
});

// Or with sandbox manager for multi-tenant scenarios
services.AddSandboxManager();
```

## Observability

### OpenTelemetry

```csharp
using AgentSandbox.Extensions.Observability;

services.AddOpenTelemetry()
    .WithTracing(builder => builder.AddSandboxInstrumentation())
    .WithMetrics(builder => builder.AddSandboxInstrumentation());
```

### Application Insights

```csharp
services.AddApplicationInsightsTelemetry();
services.AddSandboxApplicationInsights();
```

### Logging

```csharp
services.AddSandboxLogging();
```

## Shell Extensions

Shell command extensions are in `AgentSandbox.Core.ShellExtensions`:

```csharp
using AgentSandbox.Core;
using AgentSandbox.Core.ShellExtensions;

var options = new SandboxOptions
{
    ShellExtensions = [
        new CurlCommand(),  // HTTP requests
        new JqCommand(),    // JSON processing
        new GitCommand()    // Git operations
    ]
};

var sandbox = new Sandbox(options: options);

// Now available in shell
sandbox.Execute("curl https://api.example.com/data");
sandbox.Execute("echo '{\"name\":\"test\"}'");
sandbox.Execute("git init");
```

## See Also

- [AgentSandbox.Core](../AgentSandbox.Core) - Core sandbox functionality
- [Semantic Kernel Documentation](https://learn.microsoft.com/semantic-kernel/)
