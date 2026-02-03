# Agent Sandbox

An in-memory agent sandbox with virtual filesystem and command-line interface for server-side AI agents. Built with .NET.

## Why This Exists

Server-side AI agents face a fundamental gap: **no temporary stateful execution environment**.

Client-side agents (Cursor, Claude Code, Copilot) leverage the user's machine—real filesystems, real shells, persistent state across turns. Server-side agents? They're stuck with stateless function calls, losing context between every interaction.

This project provides what's missing: **an in-memory sandbox where agents can read, write, execute, and iterate**—just like they would on a local machine, but entirely server-side.

📖 Read the full discussion: [Why Server-Side AI Agents Are Lagging Behind—And What We Need to Fix It](https://github.com/hahahahahaiyiwen/agent-sandbox/discussions/12)

## Features

- **In-Memory Virtual Filesystem**: Full POSIX-like filesystem that never touches disk
- **Sandboxed Shell**: Unix-style CLI emulator with 18+ built-in commands
- **Shell Extensions**: Extensible command system with `curl`, `jq`, `git` and more
- **Agent Skill Integration**: Easy loading of reusable agent skills into the sandbox filesystem 
- **Self-Discovery Support**: Enhanced documentation via `help` and `<command> -h` to enable agent's self-discovery
- **Snapshots**: Save and restore complete sandbox state
- **Resource Limits**: Configurable max file size, total storage, and node count
- **Zero Dependencies**: Pure .NET implementation, no external services required

## Quick Start

### Run as Interactive Console (Playground)

```bash
cd AgentSandbox
dotnet run --project samples/InteractiveSandbox
```

This starts an interactive shell where you can execute commands:

```
SandboxShell > mkdir workspace
SandboxShell > cd workspace
SandboxShell > echo "Hello World" > hello.txt
SandboxShell > cat hello.txt
SandboxShell > exit
```

### Run as Interactive Chat Agent (Agent Playground)

Requires an Azure OpenAI deployment.

```bash
setx AZURE_OPENAI_ENDPOINT "https://<your-resource>.openai.azure.com"
setx AZURE_OPENAI_DEPLOYMENT "<your-deployment-name>"
dotnet run --project samples/InteractiveAgent
```

```
Agent > Use brainstorm skill to generate 3 app ideas for a solo NYC trip planner. Pick the best one, then use the shell tool to create /project/README.md,   /project/data/itinerary.md, and /project/plan.txt with a short outline and 2-day sample itinerary. Finally, list the project tree.
Agent > Print out the content of /project/plan.txt.
Agent > exit
```

### Run the API Server

```bash
cd AgentSandbox
dotnet run --project AgentSandbox.Api
```

Navigate to `http://localhost:5000/swagger` to explore the API.

### Use as a Library

```bash
# Install the core library
dotnet add package AgentSandbox.Core

# Or install a specific version
dotnet add package AgentSandbox.Core --version 2.0.0
```

```csharp
using AgentSandbox.Core;

// Create a sandbox
var sandbox = new Sandbox();

// Execute shell commands
var result = sandbox.Execute("mkdir -p /workspace/src");
sandbox.Execute("echo 'console.log(\"Hello\")' > /workspace/src/app.js");

// Check command results
if (result.Success)
{
    Console.WriteLine(result.Stdout);
}
else
{
    Console.WriteLine($"Error: {result.Stderr}");
}

// Create snapshots for checkpointing
var snapshot = sandbox.CreateSnapshot();
// ... make changes ...
sandbox.RestoreSnapshot(snapshot); // Rollback

// Get statistics
var stats = sandbox.GetStats();
Console.WriteLine($"Files: {stats.FileCount}, Size: {stats.TotalSize} bytes");
```

### Use with Semantic Kernel

Integrate with Microsoft Semantic Kernel for AI agent tool calling:

```csharp
using Microsoft.SemanticKernel;
using AgentSandbox.Extensions.SemanticKernel;
using AgentSandbox.Core;
using AgentSandbox.Core.Shell.Extensions;

// Configure sandbox with extensions
var options = new SandboxOptions
{
    ShellExtensions = new IShellCommand[]
    {
        new CurlCommand(),
        new JqCommand(),
        new GitCommand()
    }
};

// Build kernel with sandbox
var kernel = Kernel.CreateBuilder()
    .AddSandboxManager(options)
    .AddOpenAIChatCompletion("gpt-4", apiKey)
    .Build();

// Get the sandbox function for tool calling
var sandboxFunction = kernel.GetSandboxFunction();

// Add to kernel plugins
kernel.Plugins.AddFromFunctions("Sandbox", new[] { sandboxFunction });

// Now the AI agent can execute shell commands
var result = await kernel.InvokePromptAsync(
    "Create a project structure with src and tests folders, then create a README.md file");
```

**Available Extension Methods:**

| Method | Description |
|--------|-------------|
| `AddSandboxManager(options?)` | Registers SandboxManager as singleton, Sandbox as scoped service |
| `GetSandboxFunction(options?)` | Creates a KernelFunction for command execution from DI |
| `CreateSandboxFunction(sandbox)` | Static factory to create AIFunction from a Sandbox instance |

### Use with Dependency Injection

Register AgentSandbox services in any .NET application:

```csharp
using AgentSandbox.Extensions.DependencyInjection;
using AgentSandbox.Core;

// In Program.cs or Startup.cs
builder.Services.AddAgentSandbox(options =>
{
    options.MaxTotalSize = 50 * 1024 * 1024; // 50 MB
    options.MaxFileSize = 5 * 1024 * 1024;   // 5 MB
});

// Inject Sandbox into your services
public class MyService
{
    private readonly Sandbox _sandbox;
    
    public MyService(Sandbox sandbox)
    {
        _sandbox = sandbox;
    }
    
    public string RunCommand(string command)
    {
        var result = _sandbox.Execute(command);
        return result.Success ? result.Stdout : result.Stderr;
    }
}
```

**Available DI Extension Methods:**

| Method | Description |
|--------|-------------|
| `AddAgentSandbox(configure?)` | Registers SandboxManager (singleton) and Sandbox (scoped) |
| `AddAgentSandbox(factory)` | Uses factory for dynamic options from IServiceProvider |
| `AddTransientSandbox(configure?)` | Registers Sandbox as transient (new instance per request) |
| `AddSandboxManager(configure?)` | Registers only SandboxManager for manual control |

### Use with Application Insights

Add observability to your sandbox with Application Insights:

```csharp
using AgentSandbox.Extensions.Observability;
using Microsoft.ApplicationInsights;

var telemetryClient = new TelemetryClient(configuration);

// Enable telemetry in sandbox options
var options = new SandboxOptions
{
    Telemetry = new SandboxTelemetryOptions { Enabled = true }
};
var sandbox = new Sandbox(options: options);

// Subscribe to Application Insights
using var subscription = sandbox.AddApplicationInsights(telemetryClient, opts =>
{
    opts.TrackCommands = true;
    opts.TrackFileChanges = false; // Reduce noise
    opts.TrackLifecycle = true;
});
```

### Use with Structured Logging (ILogger)

Add structured logging using Microsoft.Extensions.Logging:

```csharp
using AgentSandbox.Core;
using AgentSandbox.Core.Telemetry;
using AgentSandbox.Extensions.Observability;
using Microsoft.Extensions.Logging;

// Create sandbox with telemetry enabled
var sandbox = new Sandbox(options: new SandboxOptions
{
    Telemetry = new SandboxTelemetryOptions { Enabled = true }
});

// Subscribe structured logging observer
using var subscription = sandbox.AddLogging(logger, opts =>
{
    opts.LogCommands = true;
    opts.LogFileChanges = false; // Debug level, can be noisy
    opts.LogLifecycle = true;
    opts.LogSkills = true;
});

// Commands are now logged with structured properties:
// Information: Command executed: mkdir in 0.5ms [SandboxId=abc123, ExitCode=0, Cwd=/]
// Warning: Command failed: rm with exit code 1 in 0.3ms [SandboxId=abc123, Error=No such file]
```

### Use with OpenTelemetry

Integrate with any OpenTelemetry-compatible backend (Jaeger, Zipkin, Prometheus, etc.):

```csharp
using AgentSandbox.Extensions.Observability;
using OpenTelemetry;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;

// Configure tracing
var tracerProvider = Sdk.CreateTracerProviderBuilder()
    .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("MyApp"))
    .AddSandboxInstrumentation()  // Add sandbox tracing
    .AddOtlpExporter(o => o.Endpoint = new Uri("http://localhost:4317"))
    .Build();

// Configure metrics
var meterProvider = Sdk.CreateMeterProviderBuilder()
    .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("MyApp"))
    .AddSandboxInstrumentation()  // Add sandbox metrics
    .AddOtlpExporter(o => o.Endpoint = new Uri("http://localhost:4317"))
    .Build();

// Create sandbox with telemetry enabled
var sandbox = new Sandbox(options: new SandboxOptions
{
    Telemetry = new SandboxTelemetryOptions { Enabled = true }
});

// Commands are now traced and metrics collected automatically
sandbox.Execute("mkdir project");
sandbox.Execute("echo 'Hello' > project/readme.txt");
```

## API Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/sandbox` | Create new sandbox |
| GET | `/api/sandbox` | List all sandboxes |
| GET | `/api/sandbox/{id}` | Get sandbox info |
| DELETE | `/api/sandbox/{id}` | Destroy sandbox |
| POST | `/api/sandbox/{id}/exec` | Execute command |
| GET | `/api/sandbox/{id}/history` | Get command history |
| GET | `/api/sandbox/{id}/fs?path=` | Read file |
| PUT | `/api/sandbox/{id}/fs` | Write file |
| GET | `/api/sandbox/{id}/ls?path=` | List directory |
| POST | `/api/sandbox/{id}/snapshot` | Create snapshot |
| POST | `/api/sandbox/{id}/restore?snapshotId=` | Restore snapshot |
| GET | `/api/sandbox/{id}/stats` | Get statistics |

### Example: Create and Use Sandbox via API

```bash
# Create a sandbox
curl -X POST http://localhost:5000/api/sandbox \
  -H "Content-Type: application/json" \
  -d '{"id": "agent-1", "workingDirectory": "/workspace"}'

# Execute a command
curl -X POST http://localhost:5000/api/sandbox/agent-1/exec \
  -H "Content-Type: application/json" \
  -d '{"command": "echo Hello World"}'

# Write a file
curl -X PUT http://localhost:5000/api/sandbox/agent-1/fs \
  -H "Content-Type: application/json" \
  -d '{"path": "/workspace/test.txt", "content": "file content"}'

# Read a file
curl "http://localhost:5000/api/sandbox/agent-1/fs?path=/workspace/test.txt"

# List directory
curl "http://localhost:5000/api/sandbox/agent-1/ls?path=/workspace"
```

## Supported Shell Commands

### Built-in Commands

| Command | Description | Example |
|---------|-------------|---------|
| `pwd` | Print working directory | `pwd` |
| `cd` | Change directory | `cd /home/user` |
| `ls` | List directory contents | `ls -la /path` |
| `cat` | Display file contents | `cat file.txt` |
| `echo` | Print text | `echo Hello $USER` |
| `mkdir` | Create directory | `mkdir -p /a/b/c` |
| `rm` | Remove file/directory | `rm -rf /dir` |
| `cp` | Copy file/directory | `cp src.txt dest.txt` |
| `mv` | Move/rename | `mv old.txt new.txt` |
| `touch` | Create empty file | `touch file.txt` |
| `head` | Show first N lines | `head -n 10 file.txt` |
| `tail` | Show last N lines | `tail -n 5 file.txt` |
| `wc` | Count lines/words/bytes | `wc file.txt` |
| `grep` | Search pattern in files | `grep -i pattern file.txt` |
| `find` | Find files | `find /path -name "*.txt"` |
| `env` | Show environment | `env` |
| `export` | Set environment variable | `export KEY=value` |
| `clear` | Clear screen | `clear` |
| `sh` | Execute shell script | `sh script.sh arg1 arg2` |
| `help` | Show available commands | `help` |

**Shell Features:**

| Feature | Supported | Notes |
|---------|-----------|-------|
| Output redirection (`>`, `>>`) | ✅ | `echo "text" > file.txt` |
| Environment variables | ✅ | `$VAR`, `$HOME`, `$PWD` |
| Glob patterns | ✅ | `*.txt`, `src/*.js` |
| Shell scripts | ✅ | `sh script.sh` or `./script.sh` |
| Command chaining (`&&`) | ❌ | Run sequential commands in separate calls or scripts |
| Pipelines (`\|`) | ❌ | Use file arguments instead |
| Input redirection (`<`, `<<`) | ❌ | Pass files as arguments |
| Background jobs (`&`) | ❌ | Not applicable |
| Command substitution | ❌ | `` `cmd` `` and `$(cmd)` not supported |

**Common Commands Not Available (examples):**
- `sed`, `awk`, `sort`, `uniq`, `cut`, `tr`, `xargs`, `tee`
- `less`, `more`, `diff`, `chmod`, `chown`, `ln`
- `du`, `df`, `ps`, `kill`, `whoami`, `date`, `uname`, `which`, `whereis`, `man`

**Shell Script Execution:**

The `sh` command executes shell scripts with support for:
- Positional parameters: `$1`, `$2`, ... `$9`
- All arguments: `$@`, `$*`
- Argument count: `$#`
- Comments and shebang lines are skipped
- Stops on first error (set -e behavior)

```bash
# Direct execution also works for .sh files
./script.sh arg1 arg2
/path/to/script.sh
```

### Shell Extensions

Extensions provide additional commands and must be registered via `SandboxOptions.ShellExtensions`:

| Extension | Description | Example |
|-----------|-------------|---------|
| `curl` | HTTP client for web requests | `curl -X GET https://api.example.com/data` |
| `jq` | JSON processor and query tool | `jq '.users[].name' data.json` |
| `git` | Simulated version control | `git init && git add . && git commit -m "Initial"` |

**Using Extensions:**

```csharp
using AgentSandbox.Core;
using AgentSandbox.Core.Shell.Extensions;

var options = new SandboxOptions
{
    ShellExtensions = new IShellCommand[]
    {
        new CurlCommand(),
        new JqCommand(),
        new GitCommand()
    }
};

var sandbox = new Sandbox(options: options);

// Now you can use extension commands
sandbox.Execute("git init");
sandbox.Execute("echo '{\"name\": \"test\"}' > data.json");
sandbox.Execute("jq '.name' data.json");
```

Each extension supports `--help` for usage information:
```bash
curl --help
jq --help
git help
```

## Configuration

```csharp
var options = new SandboxOptions
{
    MaxTotalSize = 128 * 1024,  // 128 KB total storage
    MaxFileSize = 10 * 1024,     // 10 KB per file
    MaxNodeCount = 1000,                // Max files/directories
    CommandTimeout = TimeSpan.FromSeconds(30),
    WorkingDirectory = "/workspace",
    Environment = new Dictionary<string, string>
    {
        ["PROJECT"] = "MyAgent",
        ["DEBUG"] = "true"
    }
};

var sandbox = new Sandbox("my-agent", options);
```

## Multi-Sandbox Management

```csharp
var manager = new SandboxManager();

// Create multiple isolated sandboxes
var sandbox1 = manager.Create("agent-1");
var sandbox2 = manager.Create("agent-2");

// Get existing sandbox
var existing = manager.Get("agent-1");

// Get or create (idempotent)
var sandbox = manager.GetOrCreate("agent-3");

// List all active sandboxes
foreach (var id in manager.List())
{
    var stats = manager.Get(id)?.GetStats();
    Console.WriteLine($"{id}: {stats?.FileCount} files");
}

// Cleanup inactive sandboxes (default: 1 hour timeout)
int cleaned = manager.CleanupInactive();

// Destroy specific sandbox
manager.Destroy("agent-1");
```

## Use Cases

1. **AI Agent Execution**: Provide agents with isolated file/command environments
2. **Code Sandboxing**: Execute untrusted code without filesystem risk
3. **Testing**: Create reproducible test environments with snapshots
4. **Simulation**: Simulate filesystem operations for training/evaluation
5. **Multi-tenant Services**: Isolate per-user/per-session state

## Project Structure

```
AgentSandbox/
├── AgentSandbox.sln
├── nuget.config
├── README.md
├── AgentSandbox.Core/               # Core library
│   ├── Sandbox.cs                   # Main sandbox class
│   ├── SandboxManager.cs            # Multi-sandbox manager
│   ├── SandboxOptions.cs            # Configuration options
│   ├── FileSystem/
│   │   ├── FileSystem.cs            # In-memory VFS
│   │   └── IFileSystem.cs           # Filesystem interfaces
│   ├── Shell/
│   │   ├── SandboxShell.cs          # CLI emulator
│   │   ├── ShellResult.cs           # Command result model
│   │   └── IShellCommand.cs         # Extension interface
│   └── ShellExtensions/
│       ├── CurlCommand.cs           # HTTP client
│       ├── JqCommand.cs             # JSON processor
│       └── GitCommand.cs            # Simulated git
├── AgentSandbox.Extensions/     # Semantic Kernel integration
│   └── KernelExtensions.cs          # Kernel builder extensions
├── samples/
│   ├── InteractiveSandbox/          # Interactive console app
│   │   └── Program.cs
│   └── InteractiveAgent/            # Interactive agent chat app
│       └── Program.cs
├── AgentSandbox.Api/                # REST API
│   ├── Endpoints/
│   │   └── SandboxEndpoints.cs      # API routes
│   └── Program.cs
└── AgentSandbox.Tests/              # Unit tests
    ├── VirtualFileSystemTests.cs
    ├── SandboxShellTests.cs
    └── ShellExtensions/
        ├── CurlCommandTests.cs
        ├── JqCommandTests.cs
        └── GitCommandTests.cs
```

## Building

```bash
dotnet build
dotnet test
```

## Building and Referencing as a NuGet Package

### Build the Package

```bash
# Pack the core library
dotnet pack AgentSandbox.Core -c Release -o ./nupkgs

# Pack the Semantic Kernel integration (optional)
dotnet pack AgentSandbox.Extensions -c Release -o ./nupkgs
```

## License

MIT
