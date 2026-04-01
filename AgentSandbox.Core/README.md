# AgentSandbox.Core

A lightweight, in-memory virtual filesystem and shell for AI agents. Zero external dependencies.

## Foundational Principles
1. **Only 4 core agent APIs**:
   - `Execute(command)` for shell execution
   - `ReadFileLines(path, startLine?, endLine?)` for structured line-range reads
   - `WriteFile(path, content)` for atomic full-file writes
   - `ApplyPatch(path, patch)` for incremental context-aware edits
2. **One agent-session = one sandbox instance**:
   - Single-owner model by design
   - Phase 2 baseline allows concurrent file reads while serializing file writes
   - Phase 3 adds optional isolated parallel command execution via `SandboxOptions.EnableIsolatedParallelCommandExecution`
3. **Cross-session state transition via snapshots**:
   - Session handoff and recovery are done through snapshot create/restore
   - Filesystem, working directory, and environment state are portable checkpoint data

## Current Scope vs Direction
- **Direction**: keep the agent-facing contract centered on the 4 core APIs above.
- **Current scope**: implementation also includes orchestration and integration surfaces (e.g., `SandboxManager`, REST endpoints, DI, observability, skills/importing).

## Snapshot Semantics
- Snapshot/restore is the required cross-session state transition mechanism.
- Snapshot captures filesystem, working directory, and environment.

## Non-goals / Constraints
- Shell intentionally does not support pipes, command chaining (`||`), or stdin redirection.
- Parallel command execution is off by default and remains opt-in.

## Integration Invariant: Single Owner, Controlled Multi-Thread Access
- A sandbox instance is a single-agent execution lane.
- Public integrations can dispatch from multiple threads, but should respect lane semantics:
  - `ReadFileLines` can run concurrently with other reads.
  - `WriteFile` and `ApplyPatch` serialize against reads/writes.
  - `Execute` is exclusive by default; when `EnableIsolatedParallelCommandExecution` is enabled, isolated command executions can overlap.
  - `RestoreSnapshot` remains exclusive.
  - `CreateSnapshot` runs as a file-read operation (concurrent with reads, serialized with writes).
- For high parallel throughput with persistent mutations, allocate separate sandbox instances via `SandboxManager`.
- Conflicting command-lane overlaps fail fast with deterministic errors; file-lane overlaps are serialized.

## Phase 3 Optional Isolated Parallel Execute
- Enable with `SandboxOptions.EnableIsolatedParallelCommandExecution = true`.
- In this mode, `Execute` runs commands in isolated shell contexts with filesystem snapshots.
- Command-local cwd/environment/session cache mutations are isolated and are not written back to the primary sandbox shell context.
- Filesystem mutations made by isolated commands are confined to throwaway filesystem copies and do not affect the primary sandbox filesystem.
- Shell extensions must implement `IParallelSafeShellCommand`; otherwise execution fails fast with actionable diagnostics.
- Timed-out isolated commands are tracked and quarantined until completion to preserve lifecycle safety.
- `RestoreSnapshot` and disposal still take exclusive coordination locks to preserve deterministic lifecycle behavior.

## Design Outcome
The system optimizes for simplicity, correctness, and reproducibility over broad API surface area: agents get just enough primitives to work effectively, and orchestration-level continuity is handled explicitly via snapshot-based state transfer.

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
    MaxCommandLength = 8 * 1024, // 8KB command input max
    MaxWritePayloadBytes = 64 * 1024, // 64KB WriteFile payload max
    MaxNodeCount = 5000,
    Environment = new() { ["HOME"] = "/home/agent" }
};

var sandbox = new Sandbox("agent-1", options);
```

`ReadFileLines`, `WriteFile`, and `ApplyPatch` reject path inputs that contain `..` traversal segments.
`ReadFileLines` returns a materialized line snapshot captured at call time (it is not a lazy streaming cursor).

### Secret Policy Model

You can constrain secret usage for network-enabled commands (for example `curl`) using `SecretBroker` and `SecretPolicy`:

```csharp
var options = new SandboxOptions
{
    SecretBroker = mySecretBroker,
    SecretPolicy = new SecretResolutionPolicy
    {
        AllowedRefs = new HashSet<string>(StringComparer.Ordinal) { "api-token", "service-token" },
        MaxSecretAge = TimeSpan.FromMinutes(10),
        EgressHostAllowlistHook = context => context.DestinationUri.Host.EndsWith(".example.com", StringComparison.OrdinalIgnoreCase)
    }
};
```

`curl` also supports per-command allowlists with repeatable `--allowed-ref` flags.

For custom shell extensions, always resolve `secretRef:<ref>` placeholders via `IShellContext.TryResolveSecretReferences(...)` rather than ad hoc parsing so secret policy checks are applied uniformly; for network/egress operations, ensure `SecretAccessRequest.CommandName` and `SecretAccessRequest.DestinationUri` are populated so destination-aware egress policy is evaluated.

## Capabilities Extension Pattern

`ISandboxCapability` allows extension packages to configure `SandboxOptions` without adding dependencies to core:

```csharp
using AgentSandbox.Core;
using AgentSandbox.Extensions;
using AgentSandbox.Core.Shell.Extensions;

var options = new SandboxOptions
{
    Capabilities =
    [
        new ShellCommandsCapability([
            new CurlCommand(),
            new JqCommand(),
            new GitCommand()
        ])
    ]
};

var sandbox = new Sandbox(options: options);
```

Capability initialization is fail-fast: if any capability throws during `Initialize(...)`, sandbox construction throws an `InvalidOperationException` that includes the capability name/type and preserves the original exception as `InnerException`. Any capabilities that were already initialized in that constructor pass are disposed in reverse registration order to prevent partial-lifecycle leaks. If rollback disposal also fails, those disposal exceptions are attached to the thrown exception via `ex.Data["CapabilityDisposeExceptions"]`.

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

`GetSkills()` returns a cache snapshot of the skills discovered during the most recent discovery pass (performed during sandbox initialization after configured imports are applied). If the configured `AgentSkills.BasePath` is missing or inaccessible, discovery returns an empty result and clears previously loaded skills to prevent stale skill exposure.

`AgentSkill` factory helpers (`FromPath`, `FromAssembly`, `FromFiles`, `FromSource`) validate required inputs and throw argument exceptions for null/blank values before creating source adapters.

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
- Command sequencing: `;` (always run next command) and `&&` (run next command on success)
- Environment variables: `$VAR`, `$HOME`
- Glob patterns: `*.txt`, `src/**/*.cs`
- Shell scripts: `sh script.sh` or `./script.sh`

### Multi-file Failure Policy Matrix

Multi-target command behavior is intentionally policy-driven through shared failure handling in shell command implementations:

| Command group | Commands | Failure strategy | Partial stdout on error | Side effects before failure |
|---|---|---|---|---|
| Read-oriented | `cat`, `head`, `tail`, `wc`, `grep`, `ls` | Fail fast on first failing input | Preserved for already-processed inputs | N/A |
| Mutating copy/move | `cp`, `mv` | Fail fast on first failing input | None | Preserved (already-applied changes are not rolled back) |
| Mutating delete | `rm` | Fail fast by default; `-f` continues past missing paths | None | Preserved (already-deleted paths stay deleted) |

**Not Supported:**
- Pipelines (`|`) - use file arguments instead: `grep pattern file.txt`
- Command chaining with `||` - run fallback commands separately or use scripts
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

// Snapshot metadata (schema-versioned)
var metadata = snapshot.Metadata;
// metadata.SchemaVersion, metadata.SnapshotSizeBytes, metadata.FileCount,
// metadata.CreatedAt, metadata.SourceSandboxId, metadata.SourceSessionId

// Restore later
sandbox.RestoreSnapshot(snapshot);
```

Manager-level persistence via configurable store:

```csharp
var manager = new SandboxManager(
    defaultOptions: null,
    managerOptions: new SandboxManagerOptions
    {
        SnapshotStore = new InMemorySnapshotStore()
    });

var sandbox = manager.Get();
var snapshotId = manager.SaveSnapshot(sandbox.Id);
var snapshotMetadata = manager.GetSnapshotMetadata(snapshotId);
var restoredSandbox = manager.RestoreSnapshot(snapshotId); // new sandbox ID

// Persist and release in one lifecycle call
var releasedSnapshotId = manager.Release(restoredSandbox.Id);
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

Lifecycle telemetry includes sandbox `Created`, `Executed`, `SnapshotCreated`, `SnapshotRestored`, and `Disposed` events. Integrators can attach host correlation metadata (for example: `tenantId`, `sessionId`, `requestId`) through `SandboxTelemetryOptions.HostCorrelationMetadata`.

For compliance retention, persist emitted lifecycle events in your host logging/telemetry backend with policy driven by your regulatory requirements. Keep retention windows and archival controls in the host system rather than in sandbox memory.

`GetHistory()` and `GetStats()` are projection views over a centralized metadata journal that tracks shell and capability operations. Journal retention is configurable through `SandboxOptions.Journal`.

## See Also

- [AgentSandbox.Extensions](../AgentSandbox.Extensions) - Shell extensions (curl, git, jq) and Semantic Kernel integration
- [Agent Skills Specification](https://agentskills.io/specification)
