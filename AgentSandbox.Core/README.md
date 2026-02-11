# AgentSandbox.Core

A lightweight, in-memory virtual filesystem and shell for AI agents. Zero external dependencies.

## Installation

```bash
dotnet add package AgentSandbox.Core
```

## Quick Start

```csharp
using AgentSandbox.Core;

// Create a sandbox
var sandbox = new Sandbox();

// Execute shell commands
var result = sandbox.Execute("echo 'Hello, Agent!'");
Console.WriteLine(result.Stdout); // Hello, Agent!

// File operations
sandbox.Execute("mkdir -p /workspace/src");
sandbox.Execute("echo 'console.log(1)' > /workspace/src/app.js");
sandbox.Execute("cat /workspace/src/app.js");
```

## Configuration

```csharp
var options = new SandboxOptions
{
    WorkingDirectory = "/workspace",
    MaxTotalSize = 1024 * 1024,  // 1MB total storage
    MaxFileSize = 64 * 1024,     // 64KB per file
    MaxNodeCount = 5000,
    Environment = new() { ["HOME"] = "/home/agent" }
};

var sandbox = new Sandbox("agent-1", options);
```

## Importing Files

```csharp
var options = new SandboxOptions
{
    Imports = [
        new FileImportOptions { Path = "/data", Source = new FileSystemSource("C:/my-data") },
        new FileImportOptions { Path = "/config", Source = new InMemorySource()
            .AddFile("settings.json", """{"debug": true}""") }
    ]
};
```

## Agent Skills

Load [agentskills.io](https://agentskills.io) compatible skill packages. Skills are discovered by scanning the skill base path for `SKILL.md` files after imports:

```csharp
var options = new SandboxOptions
{
    Imports = [
        // Import skill files from filesystem
        new FileImportOptions("/skills/python-dev", new FileSystemSource("C:/skills/python-dev")),
        // Or import from in-memory sources
        new FileImportOptions("/skills/custom", new InMemorySource()
            .AddFile("SKILL.md", "---\nname: my-skill\ndescription: Custom skill\n---\n# Instructions...")
            .AddFile("scripts/setup.sh", "#!/bin/bash\necho 'Setting up'"))
    ],
    AgentSkills = new AgentSkillOptions
    {
        BasePath = "/skills"  // Where to discover skills in sandbox file system
    }
};

var sandbox = new Sandbox(options: options);

// Skills are automatically discovered from BasePath
var skills = sandbox.GetSkills();
Console.WriteLine(skills[0].Metadata.Instructions);

// Execute skill scripts
sandbox.Execute("sh /skills/python-dev/scripts/setup.sh");
```

## Built-in Commands

| Command | Description |
|---------|-------------|
| `ls`, `cd`, `pwd` | Directory navigation |
| `cat`, `head`, `tail` | File viewing |
| `mkdir`, `touch`, `rm`, `cp`, `mv` | File management |
| `echo`, `grep`, `find`, `wc` | Text processing |
| `which`, `date` | Utilities |
| `env`, `export` | Environment variables |
| `clear` | Clear screen |
| `sh` | Script execution |
| `help` | List commands (`<cmd> -h` for details) |

**Grep Features:**
- Basic: `-i` (case insensitive), `-n` (line numbers), `-r` (recursive)
- Output: `-l` (files only), `-c` (count), `-o` (only matching)
- Filtering: `-v` (invert), `-w` (word match), `-m N` (max count)
- Context: `-A N` (after), `-B N` (before), `-C N` (around)

**Shell Features:**
- Output redirection: `>` (write) and `>>` (append)
- Environment variables: `$VAR`, `$HOME`
- Glob patterns: `*.txt`, `src/**/*.cs`
- Shell scripts: `sh script.sh` or `./script.sh`

**Not Supported:**
- Pipelines (`|`) - use file arguments instead: `grep pattern file.txt`
- Command chaining (`&&`) - run commands separately or use scripts
- Input redirection (`<`, `<<`) - pass files as arguments
- Background jobs (`&`) - returns an error if used
- Command substitution (`` `cmd` `` or `$(cmd)`) - returns an error if used

**Common Commands Not Available (examples):**
- `sed`, `awk`, `sort`, `uniq`, `cut`, `tr`, `xargs`, `tee`
- `less`, `more`, `diff`, `chmod`, `chown`, `ln`
- `du`, `df`, `ps`, `kill`, `whoami`, `uname`, `whereis`, `man`

## Snapshots

```csharp
// Save state
var snapshot = sandbox.CreateSnapshot();

// Restore later
sandbox.RestoreSnapshot(snapshot);
```

## History and Observability

```csharp
// Inspect command history
var history = sandbox.GetHistory();

// Build an AI tool description for the sandbox
var toolDescription = sandbox.GetToolDescription();

// Subscribe to sandbox events (commands, files, lifecycle)
using var subscription = sandbox.Subscribe(myObserver);
```

Telemetry hooks live in `AgentSandbox.Core.Telemetry` and are configured via `SandboxOptions.Telemetry`.

## Thread Safety

Sandbox instances are thread-safe for concurrent command execution.

## See Also

- [AgentSandbox.Extensions](../AgentSandbox.Extensions) - Shell extensions (curl, git, jq) and Semantic Kernel integration
- [Agent Skills Specification](https://agentskills.io/specification)
